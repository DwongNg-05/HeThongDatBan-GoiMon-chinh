using System.Text;
using QRCoder;

namespace RestaurantManagement.Web.Services;

/// <summary>
/// Produces a compact, dependency-free A4 PDF. QR modules are vector squares,
/// so the resulting code stays sharp when printed.
/// </summary>
public sealed class TableQrPdfBuilder
{
    private const float PageWidth = 595f;   // A4 in PDF points
    private const float PageHeight = 842f;
    private const float QrSize = 170f;
    private const int QrPerPage = 4;

    public byte[] Build(IEnumerable<TableQrPdfDocumentItem> source)
    {
        var items = source.ToList();
        if (items.Count == 0) throw new ArgumentException("Cần ít nhất một mã QR để tạo PDF.", nameof(source));

        var pageContents = items.Chunk(QrPerPage).Select(BuildPageContent).ToList();
        return WriteDocument(pageContents);
    }

    private static byte[] BuildPageContent(TableQrPdfDocumentItem[] items)
    {
        var content = new StringBuilder();
        for (var index = 0; index < items.Length; index++)
        {
            var column = index % 2;
            var row = index / 2;
            var cellLeft = 40f + column * 275f;
            var cellBottom = row == 0 ? 435f : 55f;
            WriteText(content, cellLeft + 50f, cellBottom + 335f, 16f, "TABLE " + Escape(items[index].TableCode));
            WriteText(content, cellLeft + 50f, cellBottom + 315f, 9f, "Scan to confirm your table");
            WriteQr(content, items[index].QrUrl, cellLeft + 50f, cellBottom + 115f, QrSize);
            content.Append("0.75 w 0.75 0.75 0.75 RG ")
                .Append(Format(cellLeft)).Append(' ').Append(Format(cellBottom)).Append(" 240 350 re S\n");
        }
        return Encoding.ASCII.GetBytes(content.ToString());
    }

    private static void WriteQr(StringBuilder content, string qrUrl, float left, float bottom, float size)
    {
        var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(qrUrl, QRCodeGenerator.ECCLevel.Q);
        var modules = data.ModuleMatrix;
        var moduleCount = modules.Count;
        var moduleSize = size / (moduleCount + 8); // four modules of quiet zone on every edge
        var originX = left + 4 * moduleSize;
        var originY = bottom + 4 * moduleSize;

        content.Append("q 0 0 0 rg\n");
        for (var row = 0; row < moduleCount; row++)
        {
            var bits = modules[row];
            for (var column = 0; column < moduleCount; column++)
            {
                if (!bits[column]) continue;
                var x = originX + column * moduleSize;
                var y = originY + (moduleCount - row - 1) * moduleSize;
                content.Append(Format(x)).Append(' ').Append(Format(y)).Append(' ')
                    .Append(Format(moduleSize)).Append(' ').Append(Format(moduleSize)).Append(" re f\n");
            }
        }
        content.Append("Q\n");
    }

    private static void WriteText(StringBuilder content, float x, float y, float size, string value)
        => content.Append("BT /F1 ").Append(Format(size)).Append(" Tf ")
            .Append(Format(x)).Append(' ').Append(Format(y)).Append(" Td (")
            .Append(value).Append(") Tj ET\n");

    private static byte[] WriteDocument(IReadOnlyList<byte[]> pageContents)
    {
        var objectCount = 3 + pageContents.Count * 2;
        var objects = new byte[objectCount + 1][];
        objects[1] = Bytes("<< /Type /Catalog /Pages 2 0 R >>");

        var pageIds = Enumerable.Range(0, pageContents.Count).Select(index => 4 + index * 2).ToArray();
        objects[2] = Bytes($"<< /Type /Pages /Kids [{string.Join(' ', pageIds.Select(id => id + " 0 R"))}] /Count {pageIds.Length} >>");
        objects[3] = Bytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>");

        for (var index = 0; index < pageContents.Count; index++)
        {
            var pageId = pageIds[index];
            var contentId = pageId + 1;
            objects[pageId] = Bytes($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth:0} {PageHeight:0}] /Resources << /Font << /F1 3 0 R >> >> /Contents {contentId} 0 R >>");
            objects[contentId] = Combine(Bytes($"<< /Length {pageContents[index].Length} >>\nstream\n"), pageContents[index], Bytes("\nendstream"));
        }

        using var pdf = new MemoryStream();
        pdf.Write(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34, 0x0A, 0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A });
        var offsets = new long[objectCount + 1];
        for (var id = 1; id <= objectCount; id++)
        {
            offsets[id] = pdf.Position;
            Write(pdf, $"{id} 0 obj\n");
            pdf.Write(objects[id]);
            Write(pdf, "\nendobj\n");
        }

        var xref = pdf.Position;
        Write(pdf, $"xref\n0 {objectCount + 1}\n0000000000 65535 f \n");
        for (var id = 1; id <= objectCount; id++)
            Write(pdf, $"{offsets[id]:D10} 00000 n \n");
        Write(pdf, $"trailer\n<< /Size {objectCount + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return pdf.ToArray();
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    private static string Format(float value) => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    private static byte[] Bytes(string value) => Encoding.ASCII.GetBytes(value);
    private static void Write(Stream stream, string value) => stream.Write(Bytes(value));

    private static byte[] Combine(params byte[][] chunks)
    {
        var length = chunks.Sum(chunk => chunk.Length);
        var result = new byte[length];
        var offset = 0;
        foreach (var chunk in chunks)
        {
            Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length);
            offset += chunk.Length;
        }
        return result;
    }
}

public sealed record TableQrPdfDocumentItem(string TableCode, string QrUrl);
