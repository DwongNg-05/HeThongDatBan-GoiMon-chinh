using System.Globalization;
using System.IO.Compression;
using System.Text;
using RestaurantManagement.Web.Models.Orders;

namespace RestaurantManagement.Web.Services.Pdf;

/// <summary>Tệp PDF đã tạo + các đoạn chữ đã vẽ (để kiểm thử so khớp nội dung với màn hình).</summary>
public sealed record BillPdf(byte[] Content, string FileName, double WidthPt, double HeightPt, IReadOnlyList<string> Texts,
    IReadOnlyCollection<int> MissingCharacters);

/// <summary>
/// S4-01 Task 4: tạo tệp PDF hoá đơn tạm tính khổ 80mm (docs/S4-01-Task4.md) — CÙNG bố cục với bản in (bill-print.css):
/// rộng 80mm, lề trái/phải 4mm (vùng in 72mm), một trang dài đúng bằng nội dung; dòng khung "TẠM TÍNH – …", tên quán, phần đầu,
/// bảng Món | SL (8mm) | Thành tiền (22mm), đơn giá + nhãn huỷ + ghi chú (nghiêng) xuống dòng dưới tên món, nhóm
/// "ĐÃ HUỶ – CÓ TÍNH TIỀN", các dòng cộng, TỔNG CỘNG, chú thích cuối. Cỡ chữ đổi từ px của bản in sang pt (12px = 9pt).
/// Chữ là chữ thật (chép / tìm được): font DejaVu Sans Condensed (Fonts/, giấy phép tự do) nhúng bản rút gọn, mã hoá Identity-H
/// kèm bảng ToUnicode — tiếng Việt có dấu hiển thị đúng trên mọi máy, không phụ thuộc font cài sẵn. Không dùng thư viện ngoài.
/// </summary>
public sealed class BillPdfBuilder
{
    public const string RegularFontFile = "DejaVuSansCondensed.ttf";
    public const string BoldFontFile = "DejaVuSansCondensed-Bold.ttf";
    public const string ItalicFontFile = "DejaVuSansCondensed-Oblique.ttf";

    private const double Mm = 72 / 25.4;
    public const double PageWidthPt = 80 * Mm;
    private const double Side = 4 * Mm, ContentWidth = 72 * Mm, Top = 3 * Mm, Bottom = 6 * Mm;
    private const double QtyWidth = 8 * Mm, AmountWidth = 22 * Mm, NameGap = 1.5 * Mm, LineHeight = 1.35;
    private const double Base = 9, Small = 8.25, Restaurant = 12, Grand = 10.5, Foot = 7.5;   // 12px, 11px, 16px, 14px, 10px

    private readonly TrueTypeFont _regular, _bold, _italic;

    public BillPdfBuilder(TrueTypeFont regular, TrueTypeFont bold, TrueTypeFont italic)
    {
        (_regular, _bold, _italic) = (regular, bold, italic);
    }

    /// <summary>Nạp ba font từ thư mục Fonts của web (bản build / publish chép kèm, xem .csproj).</summary>
    public static BillPdfBuilder FromDirectory(string directory) => new(
        TrueTypeFont.Load(Path.Combine(directory, RegularFontFile)),
        TrueTypeFont.Load(Path.Combine(directory, BoldFontFile)),
        TrueTypeFont.Load(Path.Combine(directory, ItalicFontFile), isItalic: true));

    public BillPdf Build(TableBillPreview preview, TableBillHeader header)
    {
        var page = new Page(new FontUsage(_regular, "F1"), new FontUsage(_bold, "F2"), new FontUsage(_italic, "F3"));
        Layout(page, preview, header);
        var height = page.Y + Bottom;
        var content = page.Render(height);
        var bytes = Write(page, content, height, $"Tạm tính bàn {header.Tables} – {header.PrintedAtText}");
        var missing = page.Fonts.SelectMany(f => f.Missing).Distinct().ToArray();
        return new BillPdf(bytes, BillPdfFileName.For(header), PageWidthPt, height, page.Texts, missing);
    }

    // ---------------- Bố cục (cùng thứ tự, cùng cột với _PrintReceipt.cshtml / bill-print.css) ----------------

    private static void Layout(Page p, TableBillPreview preview, TableBillHeader header)
    {
        const double L = Side, W = ContentWidth;
        p.Y = Top;
        if (header.Notice is not null)
        {
            var lines = p.Wrap(header.Notice, p.Bold, Small, W - 2 * Mm);
            var boxHeight = lines.Count * Small * LineHeight + 2 * Mm;
            p.Rect(L, p.Y, W, boxHeight);
            for (var i = 0; i < lines.Count; i++) p.Text(p.Bold, Small, L, p.Y + 1 * Mm + i * Small * LineHeight, lines[i], Align.Center, W);
            p.Y += boxHeight + 2 * Mm;
        }
        p.Y += p.Paragraph(p.Bold, Restaurant, L, p.Y, header.RestaurantName, W, Align.Center, 1.25) + 2 * Mm;

        foreach (var (label, value) in new[]
                 {
                     ("Bàn", header.Tables), (header.ServiceStartLabel, header.ServiceStartedAtText),
                     ("Thời điểm in", header.PrintedAtText), ("Người in", header.PrintedBy)
                 })
        {
            var labelWidth = p.Regular.Font.Width(label, Base);
            p.Text(p.Regular, Base, L, p.Y, label);
            var valueWidth = W - labelWidth - 2 * Mm;
            var valueLines = p.Wrap(value, p.Bold, Base, valueWidth);
            for (var i = 0; i < valueLines.Count; i++)
                p.Text(p.Bold, Base, L + labelWidth + 2 * Mm, p.Y + i * Base * LineHeight, valueLines[i], Align.Right, valueWidth);
            p.Y += valueLines.Count * Base * LineHeight;
        }
        p.Y += 2 * Mm;
        p.Line(p.Y, Stroke.Dashed);
        p.Y += 2 * Mm;

        if (preview.IsEmpty)
        {
            p.Y += 3 * Mm;
            p.Y += p.Paragraph(p.Regular, Base, L, p.Y, BillRules.EmptyMessage, W, Align.Center) + 3 * Mm;
        }
        else
        {
            const double qtyX = L + W - AmountWidth - QtyWidth, amountX = L + W - AmountWidth, nameWidth = W - QtyWidth - AmountWidth - NameGap;
            p.Text(p.Bold, Small, L, p.Y, "Món");
            p.Text(p.Bold, Small, qtyX, p.Y, "SL", Align.Center, QtyWidth);
            p.Text(p.Bold, Small, amountX, p.Y, "Thành tiền", Align.Right, AmountWidth);
            p.Y += Small * LineHeight + 1 * Mm;
            p.Line(p.Y, Stroke.Solid);

            void Row(TableBillPreviewLine line)
            {
                var top = p.Y + 1.2 * Mm;
                var y = top;
                y += p.Paragraph(p.Bold, Base, L, y, line.ItemName, nameWidth);
                y += p.Paragraph(p.Regular, Small, L, y, BillRules.Format(line.UnitPrice) + "/" + line.Unit, nameWidth);
                if (line.Status == "Cancelled")
                    y += p.Paragraph(p.Bold, Small, L, y, "[" + BillRules.ChargedCancellationLabel + "]", nameWidth);
                if (line.Requests.HasAny)
                    y += p.Paragraph(p.Italic, Small, L, y, line.Requests.Summary, nameWidth);
                p.Text(p.Regular, Base, qtyX, top, line.Quantity.ToString(CultureInfo.InvariantCulture), Align.Center, QtyWidth);
                p.Text(p.Regular, Base, amountX, top, BillRules.Format(line.Amount), Align.Right, AmountWidth);
                p.Y = y + 1.2 * Mm;
                p.Line(p.Y, Stroke.Dotted);
            }

            foreach (var line in preview.Lines) Row(line);
            if (preview.ChargedCancelledLines.Count > 0)
            {
                p.Line(p.Y, Stroke.Dashed);
                p.Y += 2 * Mm;
                p.Text(p.Bold, Base, L, p.Y, BillRules.ChargedCancellationLabel.ToUpperInvariant());
                p.Y += Base * LineHeight + 1 * Mm;
                p.Line(p.Y, Stroke.Solid);
                foreach (var line in preview.ChargedCancelledLines) Row(line);
            }

            p.Y += 2 * Mm;
            var totals = new List<(string Label, decimal Amount, bool Grand)>();
            if (preview.ChargedCancelledLines.Count > 0)
            {
                totals.Add(("Cộng món đã gọi", preview.ItemsSubtotal, false));
                totals.Add(("Cộng món đã huỷ – có tính tiền", preview.ChargedCancelledSubtotal, false));
            }
            totals.Add(("TỔNG CỘNG", preview.Total, true));
            foreach (var (label, amount, grand) in totals)
            {
                var (font, size) = grand ? (p.Bold, Grand) : (p.Regular, Base);
                if (grand)
                {
                    p.Line(p.Y, Stroke.Solid);
                    p.Y += 1.5 * Mm;
                }
                var text = BillRules.Format(amount);
                var amountWidth = font.Font.Width(text, size);
                p.Text(font, size, L, p.Y, text, Align.Right, W);
                p.Y += p.Paragraph(font, size, L, p.Y, label, W - amountWidth - 2 * Mm) + 0.5 * Mm;
            }
        }
        p.Y += 3 * Mm;
        p.Y += p.Paragraph(p.Regular, Foot, L, p.Y, "Chưa trừ giảm giá. Số tiền chính thức theo hoá đơn khi thanh toán.", W, Align.Center);
    }

    // ---------------- Ghi tệp PDF 1.7 (một trang, ba font Type0 / CIDFontType2 nhúng rút gọn) ----------------

    private static byte[] Write(Page page, byte[] content, double height, string title)
    {
        var objects = new List<byte[]>();
        int Reserve() { objects.Add([]); return objects.Count; }
        int Add(byte[] value) { objects.Add(value); return objects.Count; }
        int AddText(string value) => Add(Encoding.ASCII.GetBytes(value));

        var catalog = Reserve();
        var pages = Reserve();
        var pageObject = Reserve();
        var contents = Add(Stream("", content));
        var fontObjects = new List<(string Resource, int Id)>();
        var tagIndex = 0;
        foreach (var usage in page.Fonts)
        {
            var font = usage.Font;
            var tag = "AAAAA" + (char)('A' + tagIndex++);
            var baseFont = $"{tag}+{font.Name}";
            string Scale(double units) => Math.Round(units * 1000.0 / font.UnitsPerEm).ToString(CultureInfo.InvariantCulture);
            var subset = font.Subset(usage.Glyphs.Keys);
            var file = Add(Stream($"/Length1 {subset.Length}", subset));
            var box = string.Join(" ", font.BoundingBox.Select(v => Scale(v)));
            var descriptor = AddText($"<< /Type /FontDescriptor /FontName /{baseFont} /Flags 32 /FontBBox [{box}] /ItalicAngle {(font.IsItalic ? -11 : 0)} "
                + $"/Ascent {Scale(font.Ascent)} /Descent {Scale(font.Descent)} /CapHeight {Scale(font.Ascent)} /StemV 80 /FontFile2 {file} 0 R >>");
            var widths = string.Join(" ", usage.Glyphs.Keys.Select(g => $"{g} [{Scale(font.Advance(g))}]"));
            var cidFont = AddText($"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /{baseFont} /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> "
                + $"/FontDescriptor {descriptor} 0 R /DW 1000 /W [{widths}] /CIDToGIDMap /Identity >>");
            var toUnicode = Add(Stream("", Encoding.ASCII.GetBytes(ToUnicodeCMap(usage))));
            fontObjects.Add((usage.ResourceName, AddText($"<< /Type /Font /Subtype /Type0 /BaseFont /{baseFont} /Encoding /Identity-H "
                + $"/DescendantFonts [{cidFont} 0 R] /ToUnicode {toUnicode} 0 R >>")));
        }
        var info = AddText($"<< /Title <FEFF{Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(title))}> /Producer (RestaurantManagement S4-01) >>");

        objects[catalog - 1] = Encoding.ASCII.GetBytes($"<< /Type /Catalog /Pages {pages} 0 R >>");
        objects[pages - 1] = Encoding.ASCII.GetBytes($"<< /Type /Pages /Kids [{pageObject} 0 R] /Count 1 >>");
        var fontResources = string.Join(" ", fontObjects.Select(f => $"/{f.Resource} {f.Id} 0 R"));
        objects[pageObject - 1] = Encoding.ASCII.GetBytes($"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 {N(PageWidthPt)} {N(height)}] "
            + $"/Resources << /Font << {fontResources} >> >> /Contents {contents} 0 R >>");

        using var output = new MemoryStream();
        output.Write("%PDF-1.7\n%âãÏÓ\n"u8);
        var offsets = new List<long>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(output.Length);
            output.Write(Encoding.ASCII.GetBytes($"{i + 1} 0 obj\n"));
            output.Write(objects[i]);
            output.Write("\nendobj\n"u8);
        }
        var xref = output.Length;
        var table = new StringBuilder($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) table.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        table.Append($"trailer\n<< /Size {objects.Count + 1} /Root {catalog} 0 R /Info {info} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        output.Write(Encoding.ASCII.GetBytes(table.ToString()));
        return output.ToArray();
    }

    private static byte[] Stream(string extra, byte[] data)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) zlib.Write(data);
        var body = compressed.ToArray();
        var head = Encoding.ASCII.GetBytes($"<< /Filter /FlateDecode /Length {body.Length}{(extra.Length > 0 ? " " + extra : "")} >>\nstream\n");
        return [.. head, .. body, .. "\nendstream"u8.ToArray()];
    }

    private static string ToUnicodeCMap(FontUsage usage)
    {
        var map = new StringBuilder();
        map.Append("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n")
            .Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n")
            .Append("1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");
        foreach (var chunk in usage.Glyphs.Chunk(100))
        {
            map.Append(chunk.Length).Append(" beginbfchar\n");
            foreach (var (glyph, text) in chunk)
                map.Append('<').Append(glyph.ToString("X4")).Append("> <").Append(Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(text))).Append(">\n");
            map.Append("endbfchar\n");
        }
        map.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend");
        return map.ToString();
    }

    private static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private enum Align { Left, Center, Right }
    private enum Stroke { Solid, Dashed, Dotted }

    /// <summary>Một trang đang dựng: toạ độ tính từ mép trên (Y tăng dần xuống), đổi sang toạ độ PDF lúc ghi.</summary>
    private sealed class Page(FontUsage regular, FontUsage bold, FontUsage italic)
    {
        private readonly List<Action<StringBuilder, double>> _operations = [];
        public FontUsage Regular { get; } = regular;
        public FontUsage Bold { get; } = bold;
        public FontUsage Italic { get; } = italic;
        public IEnumerable<FontUsage> Fonts => [Regular, Bold, Italic];
        public List<string> Texts { get; } = [];
        public double Y { get; set; }

        /// <summary>Chia chữ thành các dòng vừa <paramref name="maxWidth"/>; từ dài hơn cả dòng (không có dấu cách) được ngắt giữa từ.</summary>
        public List<string> Wrap(string text, FontUsage font, double size, double maxWidth)
        {
            var lines = new List<string>();
            var current = "";
            foreach (var rawWord in text.Replace("\r", "").Replace('\n', ' ').Split(' '))
            {
                var word = rawWord;
                var candidate = current.Length == 0 ? word : current + " " + word;
                if (font.Font.Width(candidate, size) <= maxWidth) { current = candidate; continue; }
                if (current.Length > 0) { lines.Add(current); current = ""; }
                while (word.Length > 1 && font.Font.Width(word, size) > maxWidth)
                {
                    var take = word.Length;
                    while (take > 1 && font.Font.Width(word[..take], size) > maxWidth) take--;
                    if (char.IsHighSurrogate(word[take - 1]) && take > 1) take--;
                    lines.Add(word[..take]);
                    word = word[take..];
                }
                current = word;
            }
            lines.Add(current);
            return lines;
        }

        public void Text(FontUsage font, double size, double x, double top, string text, Align align = Align.Left, double width = 0)
        {
            var textWidth = font.Font.Width(text, size);
            if (align == Align.Right) x += width - textWidth;
            else if (align == Align.Center) x += (width - textWidth) / 2;
            var ascent = font.Font.Ascent * size / font.Font.UnitsPerEm;
            var descent = -font.Font.Descent * size / font.Font.UnitsPerEm;
            var baseline = top + (size * LineHeight - (ascent + descent)) / 2 + ascent;
            var glyphs = font.Encode(text);
            Texts.Add(text);
            _operations.Add((sb, height) => sb.Append("BT /").Append(font.ResourceName).Append(' ').Append(N(size)).Append(" Tf ")
                .Append(N(x)).Append(' ').Append(N(height - baseline)).Append(" Td ").Append(glyphs).Append(" Tj ET\n"));
        }

        public double Paragraph(FontUsage font, double size, double x, double top, string text, double width, Align align = Align.Left, double lineHeight = LineHeight)
        {
            var lines = Wrap(text, font, size, width);
            for (var i = 0; i < lines.Count; i++)
            {
                var lineTop = top + i * size * lineHeight + (size * lineHeight - size * LineHeight) / 2;
                Text(font, size, x, lineTop, lines[i], align, width);
            }
            return lines.Count * size * lineHeight;
        }

        public void Line(double y, Stroke stroke) => _operations.Add((sb, height) =>
        {
            var (width, dash) = stroke switch { Stroke.Solid => ("0.75", "[] 0"), Stroke.Dashed => ("0.5", "[1.5 1.5] 0"), _ => ("0.5", "[0.5 1] 0") };
            sb.Append(width).Append(" w ").Append(dash).Append(" d ").Append(N(Side)).Append(' ').Append(N(height - y)).Append(" m ")
                .Append(N(Side + ContentWidth)).Append(' ').Append(N(height - y)).Append(" l S\n");
        });

        public void Rect(double x, double y, double width, double height) => _operations.Add((sb, pageHeight) =>
            sb.Append("0.75 w [] 0 d ").Append(N(x)).Append(' ').Append(N(pageHeight - y - height)).Append(' ')
                .Append(N(width)).Append(' ').Append(N(height)).Append(" re S\n"));

        public byte[] Render(double height)
        {
            var sb = new StringBuilder();
            foreach (var operation in _operations) operation(sb, height);
            return Encoding.ASCII.GetBytes(sb.ToString());
        }
    }
}
