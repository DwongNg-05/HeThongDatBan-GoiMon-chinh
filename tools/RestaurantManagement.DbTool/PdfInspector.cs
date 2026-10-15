using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace RestaurantManagement.DbTool;

/// <summary>
/// S4-01 Task 4 (kiểm thử): đọc lại tệp PDF hoá đơn tạm tính do web tạo — số trang, khổ giấy, font nhúng, và CHỮ THẬT trong tệp
/// (giải mã qua bảng ToUnicode của từng font, đúng như trình đọc PDF làm khi chép / tìm chữ). Chữ được gom theo dòng
/// (cùng toạ độ y, xếp trái → phải) để so với màn hình xem trước. Chỉ đọc đúng định dạng BillPdfBuilder ghi ra.
/// </summary>
internal static partial class PdfInspector
{
    internal sealed record Info(int PageCount, double WidthPt, double HeightPt, int EmbeddedFonts, int ToUnicodeMaps,
        IReadOnlyList<string> Lines, int UnmappedGlyphs)
    {
        public double WidthMm => WidthPt * 25.4 / 72;
        public double HeightMm => HeightPt * 25.4 / 72;
        /// <summary>Toàn bộ chữ, mỗi dòng một dòng.</summary>
        public string Text => string.Join("\n", Lines);
        /// <summary>Chữ bỏ hết khoảng trắng (so khớp tên món / ghi chú bị xuống dòng).</summary>
        public string Compact => Regex.Replace(Text, "\\s+", "");
    }

    internal static Info Read(byte[] pdf)
    {
        if (pdf.Length < 8 || Encoding.ASCII.GetString(pdf, 0, 5) != "%PDF-")
            throw new InvalidDataException("Không phải tệp PDF.");
        var text = Encoding.Latin1.GetString(pdf);
        var objects = ObjectPattern().Matches(text).ToDictionary(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), m => m.Groups[2].Value);
        string Stream(int id)
        {
            var body = objects[id];
            var head = StreamHeadPattern().Match(body);
            var length = int.Parse(LengthPattern().Match(head.Groups[1].Value).Groups[1].Value, CultureInfo.InvariantCulture);
            var raw = Encoding.Latin1.GetBytes(body.Substring(head.Length, length));
            using var input = new ZLibStream(new MemoryStream(raw), CompressionMode.Decompress);
            using var output = new MemoryStream();
            input.CopyTo(output);
            return Encoding.Latin1.GetString(output.ToArray());
        }

        var pages = objects.Values.Where(v => PagePattern().IsMatch(v)).ToList();
        var page = pages.First();
        var box = MediaBoxPattern().Match(page);
        var fonts = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (Match resource in FontResourcePattern().Matches(page))
        {
            var fontObject = objects[int.Parse(resource.Groups[2].Value, CultureInfo.InvariantCulture)];
            var cmap = Stream(int.Parse(ToUnicodePattern().Match(fontObject).Groups[1].Value, CultureInfo.InvariantCulture));
            fonts[resource.Groups[1].Value] = BfCharPattern().Matches(cmap).ToDictionary(m => m.Groups[1].Value,
                m => Encoding.BigEndianUnicode.GetString(Convert.FromHexString(m.Groups[2].Value)));
        }

        var content = Stream(int.Parse(ContentsPattern().Match(page).Groups[1].Value, CultureInfo.InvariantCulture));
        var unmapped = 0;
        var runs = new List<(double Y, double X, string Text)>();
        foreach (Match run in TextRunPattern().Matches(content))
        {
            var map = fonts[run.Groups[1].Value];
            var hex = run.Groups[4].Value;
            var decoded = new StringBuilder();
            for (var i = 0; i + 4 <= hex.Length; i += 4)
            {
                if (map.TryGetValue(hex.Substring(i, 4), out var value)) decoded.Append(value);
                else { decoded.Append('�'); unmapped++; }
            }
            runs.Add((Math.Round(double.Parse(run.Groups[3].Value, CultureInfo.InvariantCulture), 1),
                double.Parse(run.Groups[2].Value, CultureInfo.InvariantCulture), decoded.ToString()));
        }
        var lines = runs.GroupBy(r => r.Y).OrderByDescending(g => g.Key)
            .Select(g => string.Join(" ", g.OrderBy(r => r.X).Select(r => r.Text))).ToList();
        return new Info(pages.Count, double.Parse(box.Groups[1].Value, CultureInfo.InvariantCulture),
            double.Parse(box.Groups[2].Value, CultureInfo.InvariantCulture), Regex.Matches(text, "/FontFile2 ").Count,
            Regex.Matches(text, "/ToUnicode ").Count, lines, unmapped);
    }

    [GeneratedRegex("(\\d+) 0 obj\\n(.*?)\\nendobj", RegexOptions.Singleline)] private static partial Regex ObjectPattern();
    [GeneratedRegex("^<<(.*?)>>\\nstream\\n", RegexOptions.Singleline)] private static partial Regex StreamHeadPattern();
    [GeneratedRegex("/Length (\\d+)")] private static partial Regex LengthPattern();
    [GeneratedRegex("/Type /Page(?!s)")] private static partial Regex PagePattern();
    [GeneratedRegex("/MediaBox \\[0 0 ([\\d.]+) ([\\d.]+)\\]")] private static partial Regex MediaBoxPattern();
    [GeneratedRegex("/(F\\d+) (\\d+) 0 R")] private static partial Regex FontResourcePattern();
    [GeneratedRegex("/ToUnicode (\\d+) 0 R")] private static partial Regex ToUnicodePattern();
    [GeneratedRegex("<([0-9A-F]{4})> <([0-9A-F]+)>")] private static partial Regex BfCharPattern();
    [GeneratedRegex("/Contents (\\d+) 0 R")] private static partial Regex ContentsPattern();
    [GeneratedRegex("BT /(F\\d+) [\\d.]+ Tf ([\\d.]+) ([\\d.]+) Td <([0-9A-F]*)> Tj ET")] private static partial Regex TextRunPattern();
}
