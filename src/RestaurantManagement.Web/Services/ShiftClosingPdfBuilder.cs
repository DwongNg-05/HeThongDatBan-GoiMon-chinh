using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
using RestaurantManagement.Web.Models.Cashier;

namespace RestaurantManagement.Web.Services;

public sealed class ShiftClosingPdfBuilder
{
    private static readonly object FontLock = new();

    private static readonly CultureInfo VietnameseCulture =
        CultureInfo.GetCultureInfo("vi-VN");

    public byte[] Build(ShiftClosingPdfViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var closing = model.SelectedClosing
            ?? throw new InvalidOperationException(
                "Bản chốt chưa có đủ dữ liệu để xuất PDF.");

        EnsureFonts();

        var document = new Document();

        document.Info.Title =
            $"Chốt ca {closing.ShiftId} - lần {closing.ClosingNumber}";

        document.Info.Author = closing.RestaurantName;
        document.Info.Subject = "Bản chốt ca đã lưu";

        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = "ShiftPdfFont";
        normal.Font.Size = 11;
        normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(5);

        var section = document.AddSection();

        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.TopMargin = Unit.FromCentimeter(1.8);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(1.8);
        section.PageSetup.LeftMargin = Unit.FromCentimeter(2);
        section.PageSetup.RightMargin = Unit.FromCentimeter(2);

        var restaurant = section.AddParagraph(
            Clean(closing.RestaurantName));

        restaurant.Format.Font.Size = 16;
        restaurant.Format.Font.Bold = true;
        restaurant.Format.Alignment = ParagraphAlignment.Center;

        var address = section.AddParagraph(
            Clean(closing.RestaurantAddress));

        address.Format.Alignment = ParagraphAlignment.Center;

        var phone = section.AddParagraph(
            $"Điện thoại: {Clean(closing.RestaurantPhone)}");

        phone.Format.Alignment = ParagraphAlignment.Center;

        var title = section.AddParagraph("BIÊN BẢN CHỐT CA");

        title.Format.Font.Size = 18;
        title.Format.Font.Bold = true;
        title.Format.Alignment = ParagraphAlignment.Center;
        title.Format.SpaceBefore = Unit.FromPoint(12);
        title.Format.SpaceAfter = Unit.FromPoint(12);

        AddInformation(
            section,
            "Mã ca",
            $"#{closing.ShiftId}");

        AddInformation(
            section,
            "Tên ca",
            closing.ShiftName);

        AddInformation(
            section,
            "Ngày kinh doanh",
            closing.BusinessDate.ToString("dd/MM/yyyy"));

        AddInformation(
            section,
            "Lần chốt",
            closing.ClosingNumber.ToString(
                CultureInfo.InvariantCulture));

        AddInformation(
            section,
            "Mã bản chốt",
            $"#{closing.ClosingEventId}");

        AddInformation(
            section,
            "Mở ca lúc",
            FormatTime(closing.OpenedAt, closing.TimeZoneId));

        AddInformation(
            section,
            "Chốt ca lúc",
            FormatTime(closing.ClosedAt, closing.TimeZoneId));

        AddInformation(
            section,
            "Người chốt",
            $"{closing.ClosedByName} "
            + $"(tài khoản #{closing.ClosedBy})");

        AddInformation(
            section,
            "Múi giờ",
            closing.TimeZoneId);

        AddInformation(
            section,
            "Trạng thái bản chốt",
            model.ClosingStatusLabel);

        if (model.WasReopened)
        {
            var warning = section.AddParagraph(
                "Ca đã được mở lại sau lần chốt này. "
                + "Số liệu dưới đây là số liệu đã lưu tại "
                + "thời điểm chốt, không phải số liệu hiện tại.");

            warning.Format.Font.Bold = true;
            warning.Format.SpaceBefore = Unit.FromPoint(6);
        }

        AddHeading(section, "1. Tổng hợp trong ca");

        var totals = CreateTable(section);

        AddRow(
            totals,
            "Số hoá đơn đã thanh toán",
            closing.InvoiceCount.ToString("N0", VietnameseCulture));

        AddRow(
            totals,
            "Tổng doanh thu",
            Money(closing.Revenue));

        AddRow(
            totals,
            "Doanh thu tiền mặt",
            Money(closing.CashRevenue));

        AddRow(
            totals,
            "Doanh thu chuyển khoản",
            Money(closing.TransferRevenue));

        AddRow(
            totals,
            "Tổng giảm giá",
            Money(closing.DiscountTotal));

        AddRow(
            totals,
            "Số bàn đã phục vụ",
            closing.ServedTableCount.ToString(
                "N0", VietnameseCulture));

        AddRow(totals, "Tiền đầu ca", Money(closing.OpeningCash));

        AddRow(
            totals,
            "Tiền mặt cần có cuối ca",
            Money(closing.ExpectedCash));

        AddHeading(section, "2. Xác nhận");

        var signatures = section.AddTable();
        signatures.Borders.Visible = false;
        signatures.AddColumn(Unit.FromCentimeter(8.5));
        signatures.AddColumn(Unit.FromCentimeter(8.5));

        var signatureRow = signatures.AddRow();

        signatureRow.Cells[0].Format.Alignment =
            ParagraphAlignment.Center;

        signatureRow.Cells[1].Format.Alignment =
            ParagraphAlignment.Center;

        signatureRow.Cells[0].AddParagraph("Người chốt ca");
        signatureRow.Cells[1].AddParagraph("Quản lý xác nhận");

        signatureRow.Cells[0].AddParagraph(
            "(Ký, ghi rõ họ tên)");

        signatureRow.Cells[1].AddParagraph(
            "(Ký, ghi rõ họ tên)");

        var name = signatureRow.Cells[0].AddParagraph(
            Clean(closing.ClosedByName));

        name.Format.SpaceBefore = Unit.FromCentimeter(1.5);

        var footer = section.Footers.Primary.AddParagraph();

        footer.Format.Font.Size = 9;
        footer.Format.Alignment = ParagraphAlignment.Center;

        footer.AddText(
            $"Ca #{closing.ShiftId}"
            + $" | Lần chốt {closing.ClosingNumber}"
            + " | Trang ");

        footer.AddPageField();
        footer.AddText(" / ");
        footer.AddNumPagesField();

        var renderer = new PdfDocumentRenderer
        {
            Document = document
        };

        renderer.RenderDocument();

        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, false);

        return stream.ToArray();
    }

    private static void EnsureFonts()
    {
        lock (FontLock)
        {
            if (GlobalFontSettings.FontResolver is null)
            {
                GlobalFontSettings.FontResolver =
                    new ShiftPdfFontResolver();
            }
        }
    }

    private static void AddInformation(
        Section section,
        string label,
        string value)
    {
        var paragraph = section.AddParagraph();

        paragraph.AddFormattedText(
            label + ": ",
            TextFormat.Bold);

        paragraph.AddText(Clean(value));
    }

    private static void AddHeading(
        Section section,
        string text)
    {
        var paragraph = section.AddParagraph(text);

        paragraph.Format.Font.Bold = true;
        paragraph.Format.Font.Size = 12;
        paragraph.Format.SpaceBefore = Unit.FromPoint(12);
        paragraph.Format.SpaceAfter = Unit.FromPoint(6);
        paragraph.Format.KeepWithNext = true;
    }

    private static Table CreateTable(Section section)
    {
        var table = section.AddTable();

        table.Borders.Width = Unit.FromPoint(0.5);
        table.Borders.Color = Colors.Gray;

        table.AddColumn(Unit.FromCentimeter(10.5));
        table.AddColumn(Unit.FromCentimeter(6.5));

        table.TopPadding = Unit.FromPoint(5);
        table.BottomPadding = Unit.FromPoint(5);

        return table;
    }

    private static void AddRow(
        Table table,
        string label,
        string value)
    {
        var row = table.AddRow();

        row.Cells[0].AddParagraph(label);
        row.Cells[1].AddParagraph(value);

        row.Cells[1].Format.Alignment =
            ParagraphAlignment.Right;
    }

    private static string Money(decimal value) =>
        value.ToString("N0", VietnameseCulture) + " VND";

    private static string FormatTime(
        DateTime value,
        string timeZoneId)
    {
        var utc = DateTime.SpecifyKind(value, DateTimeKind.Utc);

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(
            timeZoneId);

        return TimeZoneInfo.ConvertTimeFromUtc(utc, timeZone)
            .ToString("dd/MM/yyyy HH:mm:ss");
    }

    private static string Clean(string value) =>
        new string(value
            .Where(character =>
                !char.IsControl(character)
                || character is '\r' or '\n' or '\t')
            .ToArray())
        .Normalize();
}

/// <summary>
/// Đọc phông chữ trên máy chạy ứng dụng.
/// Windows dùng Arial; Linux dùng DejaVu Sans nếu đã cài.
/// </summary>
internal sealed class ShiftPdfFontResolver : IFontResolver
{
    public FontResolverInfo? ResolveTypeface(
        string familyName,
        bool bold,
        bool italic)
    {
        var face = (bold, italic) switch
        {
            (true, true) => "shift-bold-italic",
            (true, false) => "shift-bold",
            (false, true) => "shift-italic",
            _ => "shift-regular"
        };

        return new FontResolverInfo(face);
    }

    public byte[]? GetFont(string faceName)
    {
        var windowsFile = faceName switch
        {
            "shift-bold-italic" => "arialbi.ttf",
            "shift-bold" => "arialbd.ttf",
            "shift-italic" => "ariali.ttf",
            "shift-regular" => "arial.ttf",
            _ => throw new InvalidOperationException(
                "Phông chữ PDF không hợp lệ.")
        };

        var linuxFile = faceName switch
        {
            "shift-bold-italic" => "DejaVuSans-BoldOblique.ttf",
            "shift-bold" => "DejaVuSans-Bold.ttf",
            "shift-italic" => "DejaVuSans-Oblique.ttf",
            _ => "DejaVuSans.ttf"
        };

        var fontsDirectory = Environment.GetFolderPath(
            Environment.SpecialFolder.Fonts);

        if (!string.IsNullOrWhiteSpace(fontsDirectory))
        {
            var windowsPath = Path.Combine(
                fontsDirectory,
                windowsFile);

            if (File.Exists(windowsPath))
            {
                return File.ReadAllBytes(windowsPath);
            }
        }

        var linuxPath = Path.Combine(
            "/usr/share/fonts/truetype/dejavu",
            linuxFile);

        if (File.Exists(linuxPath))
        {
            return File.ReadAllBytes(linuxPath);
        }

        throw new InvalidOperationException(
            "Không tìm thấy phông chữ để xuất PDF tiếng Việt. "
            + "Máy Windows cần có Arial; "
            + "máy Linux cần có DejaVu Sans.");
    }
}
