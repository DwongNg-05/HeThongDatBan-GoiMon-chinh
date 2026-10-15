using System.Buffers.Binary;

namespace RestaurantManagement.Web.Services.Pdf;

/// <summary>
/// S4-01 Task 4: đọc một font TrueType (.ttf) đủ để nhúng vào PDF — bảng ký tự (cmap định dạng 12 / 4), độ rộng chữ (hmtx),
/// và tạo bản "chỉ chứa các chữ đã dùng" (subset) để tệp PDF nhỏ. Đối tượng này chỉ đọc, dùng chung giữa các yêu cầu;
/// chữ đã dùng của từng tệp PDF nằm ở <see cref="FontUsage"/>.
/// </summary>
public sealed class TrueTypeFont
{
    private readonly byte[] _data;
    private readonly Dictionary<string, (int Offset, int Length)> _tables = new(StringComparer.Ordinal);
    private readonly Dictionary<int, ushort> _cmap;
    private readonly ushort[] _advances;
    private readonly int[] _loca;

    public string Name { get; }
    public int UnitsPerEm { get; }
    public short Ascent { get; }
    public short Descent { get; }
    public short[] BoundingBox { get; }
    public int GlyphCount { get; }
    public bool IsItalic { get; }

    public TrueTypeFont(string name, byte[] data, bool isItalic = false)
    {
        Name = name;
        IsItalic = isItalic;
        _data = data;
        var tableCount = U16(4);
        for (var i = 0; i < tableCount; i++)
        {
            var record = 12 + 16 * i;
            var tag = System.Text.Encoding.ASCII.GetString(data, record, 4);
            _tables[tag] = ((int)U32(record + 8), (int)U32(record + 12));
        }
        foreach (var required in new[] { "head", "hhea", "hmtx", "maxp", "loca", "glyf", "cmap" })
            if (!_tables.ContainsKey(required)) throw new InvalidDataException($"Font {name} thiếu bảng {required}.");

        var head = _tables["head"].Offset;
        UnitsPerEm = U16(head + 18);
        BoundingBox = [S16(head + 36), S16(head + 38), S16(head + 40), S16(head + 42)];
        var longLoca = S16(head + 50) == 1;
        GlyphCount = U16(_tables["maxp"].Offset + 4);
        var hhea = _tables["hhea"].Offset;
        Ascent = S16(hhea + 4);
        Descent = S16(hhea + 6);
        var metricCount = U16(hhea + 34);

        var hmtx = _tables["hmtx"].Offset;
        _advances = new ushort[GlyphCount];
        for (var g = 0; g < GlyphCount; g++)
            _advances[g] = U16(hmtx + 4 * Math.Min(g, metricCount - 1));

        var loca = _tables["loca"].Offset;
        _loca = new int[GlyphCount + 1];
        for (var i = 0; i <= GlyphCount; i++)
            _loca[i] = longLoca ? (int)U32(loca + 4 * i) : 2 * U16(loca + 2 * i);

        _cmap = ReadCmap();
    }

    public static TrueTypeFont Load(string path, bool isItalic = false) =>
        new(Path.GetFileNameWithoutExtension(path), File.ReadAllBytes(path), isItalic);

    /// <summary>Mã chữ (glyph) của một ký tự Unicode; 0 (.notdef) khi font không có.</summary>
    public ushort GlyphId(int codePoint) => _cmap.TryGetValue(codePoint, out var glyph) ? glyph : (ushort)0;

    public bool HasGlyph(int codePoint) => _cmap.ContainsKey(codePoint);

    public int Advance(ushort glyph) => _advances[Math.Min(glyph, (ushort)(GlyphCount - 1))];

    /// <summary>Độ rộng (pt) của chuỗi ở cỡ chữ <paramref name="size"/>.</summary>
    public double Width(string text, double size)
    {
        double units = 0;
        foreach (var rune in text.EnumerateRunes()) units += Advance(GlyphId(rune.Value));
        return units * size / UnitsPerEm;
    }

    /// <summary>Bản font chỉ giữ dữ liệu nét của các chữ đã dùng (giữ nguyên mã chữ, nên CIDToGIDMap = Identity).</summary>
    public byte[] Subset(IEnumerable<ushort> usedGlyphs)
    {
        var keep = new HashSet<int> { 0 };
        var todo = new Stack<int>(usedGlyphs.Select(g => (int)g));
        while (todo.Count > 0)
        {
            var glyph = todo.Pop();
            if (glyph >= GlyphCount) continue;
            if (!keep.Add(glyph) && glyph != 0) continue;
            foreach (var component in CompositeComponents(glyph)) todo.Push(component);
        }
        // .notdef luôn giữ; thành phần của chữ ghép (dấu tiếng Việt) đã được thêm ở trên.
        var glyf = _tables["glyf"].Offset;
        using var glyphs = new MemoryStream();
        var newLoca = new byte[4 * (GlyphCount + 1)];
        for (var g = 0; g < GlyphCount; g++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(newLoca.AsSpan(4 * g), (uint)glyphs.Length);
            if (!keep.Contains(g)) continue;
            var length = _loca[g + 1] - _loca[g];
            glyphs.Write(_data, glyf + _loca[g], length);
            while (glyphs.Length % 4 != 0) glyphs.WriteByte(0);
        }
        BinaryPrimitives.WriteUInt32BigEndian(newLoca.AsSpan(4 * GlyphCount), (uint)glyphs.Length);

        var head = Table("head");
        BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(8), 0);          // checkSumAdjustment
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(50), 1);          // indexToLocFormat = long
        var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["head"] = head, ["hhea"] = Table("hhea"), ["hmtx"] = Table("hmtx"), ["maxp"] = Table("maxp"),
            ["loca"] = newLoca, ["glyf"] = glyphs.ToArray()
        };
        foreach (var hinting in new[] { "cvt ", "fpgm", "prep" })
            if (_tables.ContainsKey(hinting)) tables[hinting] = Table(hinting);
        return WriteFont(tables);
    }

    private IEnumerable<int> CompositeComponents(int glyph)
    {
        var start = _tables["glyf"].Offset + _loca[glyph];
        var length = _loca[glyph + 1] - _loca[glyph];
        if (length < 10 || S16(start) >= 0) yield break;   // chữ đơn (không ghép)
        var p = start + 10;
        while (true)
        {
            var flags = U16(p);
            yield return U16(p + 2);
            p += 4;
            p += (flags & 0x0001) != 0 ? 4 : 2;                 // ARG_1_AND_2_ARE_WORDS
            if ((flags & 0x0008) != 0) p += 2;                  // WE_HAVE_A_SCALE
            else if ((flags & 0x0040) != 0) p += 4;             // WE_HAVE_AN_X_AND_Y_SCALE
            else if ((flags & 0x0080) != 0) p += 8;             // WE_HAVE_A_TWO_BY_TWO
            if ((flags & 0x0020) == 0) yield break;             // MORE_COMPONENTS
        }
    }

    private static byte[] WriteFont(SortedDictionary<string, byte[]> tables)
    {
        var count = tables.Count;
        var power = 1;
        var log = 0;
        while (power * 2 <= count) { power *= 2; log++; }
        using var output = new MemoryStream();
        var header = new byte[12 + 16 * count];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0), 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4), (ushort)count);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(6), (ushort)(16 * power));
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(8), (ushort)log);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(10), (ushort)(16 * count - 16 * power));
        var offset = header.Length;
        var index = 0;
        foreach (var (tag, bytes) in tables)
        {
            var record = 12 + 16 * index++;
            System.Text.Encoding.ASCII.GetBytes(tag, header.AsSpan(record, 4));
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(record + 4), Checksum(bytes));
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(record + 8), (uint)offset);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(record + 12), (uint)bytes.Length);
            offset += (bytes.Length + 3) & ~3;
        }
        output.Write(header);
        foreach (var bytes in tables.Values)
        {
            output.Write(bytes);
            for (var pad = bytes.Length; pad % 4 != 0; pad++) output.WriteByte(0);
        }
        return output.ToArray();
    }

    private static uint Checksum(byte[] bytes)
    {
        uint sum = 0;
        for (var i = 0; i < bytes.Length; i += 4)
        {
            uint word = 0;
            for (var j = 0; j < 4; j++) word = (word << 8) | (i + j < bytes.Length ? bytes[i + j] : (byte)0);
            sum = unchecked(sum + word);
        }
        return sum;
    }

    private Dictionary<int, ushort> ReadCmap()
    {
        var cmap = _tables["cmap"].Offset;
        var count = U16(cmap + 2);
        int format12 = -1, format4 = -1;
        for (var i = 0; i < count; i++)
        {
            var record = cmap + 4 + 8 * i;
            var platform = U16(record);
            var encoding = U16(record + 2);
            var subtable = cmap + (int)U32(record + 4);
            var format = U16(subtable);
            if (platform == 3 && encoding == 10 && format == 12) format12 = subtable;
            else if (platform == 3 && encoding == 1 && format == 4 && format4 < 0) format4 = subtable;
        }
        var map = new Dictionary<int, ushort>();
        if (format12 >= 0)
        {
            var groups = (int)U32(format12 + 12);
            for (var i = 0; i < groups; i++)
            {
                var group = format12 + 16 + 12 * i;
                var start = (int)U32(group);
                var end = (int)U32(group + 4);
                var glyph = (int)U32(group + 8);
                for (var c = start; c <= end; c++) map[c] = (ushort)(glyph + c - start);
            }
            return map;
        }
        if (format4 < 0) throw new InvalidDataException($"Font {Name} không có bảng ký tự Unicode.");
        var segX2 = U16(format4 + 6);
        var ends = format4 + 14;
        var starts = ends + segX2 + 2;
        var deltas = starts + segX2;
        var ranges = deltas + segX2;
        for (var i = 0; i < segX2 / 2; i++)
        {
            var end = U16(ends + 2 * i);
            var start = U16(starts + 2 * i);
            var delta = S16(deltas + 2 * i);
            var rangeOffset = U16(ranges + 2 * i);
            for (int c = start; c <= end && c != 0xFFFF; c++)
            {
                int glyph;
                if (rangeOffset == 0) glyph = (c + delta) & 0xFFFF;
                else
                {
                    glyph = U16(ranges + 2 * i + rangeOffset + 2 * (c - start));
                    if (glyph != 0) glyph = (glyph + delta) & 0xFFFF;
                }
                if (glyph != 0) map[c] = (ushort)glyph;
            }
        }
        return map;
    }

    private byte[] Table(string tag)
    {
        var (offset, length) = _tables[tag];
        return _data.AsSpan(offset, length).ToArray();
    }

    private ushort U16(int offset) => BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(offset));
    private short S16(int offset) => BinaryPrimitives.ReadInt16BigEndian(_data.AsSpan(offset));
    private uint U32(int offset) => BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan(offset));
}

/// <summary>Các chữ một tệp PDF đã dùng với một font (để nhúng bản rút gọn, khai độ rộng và bảng ToUnicode cho phép chép / tìm chữ).</summary>
public sealed class FontUsage(TrueTypeFont font, string resourceName)
{
    public TrueTypeFont Font { get; } = font;
    public string ResourceName { get; } = resourceName;
    public SortedDictionary<ushort, string> Glyphs { get; } = new();
    public HashSet<int> Missing { get; } = new();

    /// <summary>Chuỗi → mã chữ 2 byte dạng hex cho toán tử Tj (mã hoá Identity-H).</summary>
    public string Encode(string text)
    {
        var hex = new System.Text.StringBuilder("<", text.Length * 4 + 2);
        foreach (var rune in text.EnumerateRunes())
        {
            var glyph = Font.GlyphId(rune.Value);
            if (glyph == 0) Missing.Add(rune.Value);
            else Glyphs.TryAdd(glyph, rune.ToString());
            hex.Append(glyph.ToString("X4"));
        }
        return hex.Append('>').ToString();
    }
}
