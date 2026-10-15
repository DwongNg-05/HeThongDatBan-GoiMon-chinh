using RestaurantManagement.DbTool;
using RestaurantManagement.Web.Models.Orders;
using RestaurantManagement.Web.Services.Pdf;

/// <summary>
/// S4-01 Task 4 (không cần database): xuất hoá đơn tạm tính thành tệp PDF khổ 80mm. Tạo PDF thật bằng font trong
/// src/RestaurantManagement.Web/Fonts rồi đọc lại (PdfInspector): khổ 80mm, một trang dài theo nội dung, font nhúng,
/// tiếng Việt có dấu đọc ra đúng, nội dung khớp màn hình (phần đầu, bảng món, nhóm món huỷ, tổng cộng), tên tệp đúng quy tắc.
/// Phần chạy thật trên SQL Server + web: verify-bill-pdf (hoặc verify); JS nút "Xuất PDF": tools/tests/bill-pdf.test.cjs.
/// </summary>
internal static class BillPdfTests
{
    internal static void Run(Action<bool, string> check)
    {
        var root = DatabaseTool.Root;
        var fonts = Path.Combine(root, "src", "RestaurantManagement.Web", "Fonts");
        var service = new BillPdfService(fonts);
        var opened = new DateTime(2026, 10, 15, 12, 5, 0, DateTimeKind.Utc);       // 19:05 giờ Việt Nam
        var printedAt = new DateTime(2026, 10, 15, 15, 30, 42, DateTimeKind.Utc);  // 22:30 giờ Việt Nam
        const string longName = "Lẩu thái hải sản đặc biệt nhiều tôm mực nghêu cho bàn 6 người kèm rau nấm và bún tươi";
        const string noSpaces = "Cánhgàchiênnướcmắmsiêucayđặcbiệtkhôngkhoảngtrắngnàocả";
        const string longNote = "khách dị ứng đậu phộng, làm riêng nước chấm, mang ra sau món khai vị và nhớ thêm chanh ớt riêng cho bé";

        BillSourceItem Item(long id, string name, decimal price, int quantity, string status = "Pending", bool charged = false, string? notes = null,
            string? quick = null) => new(id, name, "phần", price, quantity, status, charged, notes, quick, opened.AddMinutes(id));
        (TableBillPreview Preview, TableBillHeader Header) Bill(string tables, IEnumerable<BillSourceItem> items)
        {
            var preview = TableBillPreviewCalculator.Build(tables.Split(',')[0], tables, 10, "Open", "Serving", items) with
            {
                RestaurantName = "Bếp Nhà – chi nhánh Quận Một",
                ServiceOpenedAtUtc = opened
            };
            var header = TableBillHeader.Create(preview, printedAt, "Nguyễn Thị Phương Thảo");
            return (preview with { Header = header }, header);
        }

        var (preview, header) = Bill("A03, A04", new[]
        {
            Item(1, "Phở bò tái nạm", 65_000m, 2, notes: longNote, quick: "Ít cay, Không hành"),
            Item(2, longName, 1_299_000m, 2, "Preparing"),
            Item(3, noSpaces, 145_000m, 3, "Served"),
            Item(4, "Bào ngư sốt dầu hào", 9_990_000m, 99, "Ready"),
            Item(5, "Gỏi cuốn tôm thịt", 40_000m, 2, "Cancelled", charged: false, notes: "huỷ trước khi nấu"),
            Item(6, "Cá kho tộ niêu đất", 150_000m, 1, "Cancelled", charged: true, notes: "khách vội")
        });
        var pdf = service.Build(preview, header);
        var info = PdfInspector.Read(pdf.Content);
        var text = info.Text;

        // Tệp mở được, khổ 80mm, một trang dài theo nội dung.
        check(pdf.Content.Length > 1000 && System.Text.Encoding.ASCII.GetString(pdf.Content, 0, 8) == "%PDF-1.7"
              && System.Text.Encoding.ASCII.GetString(pdf.Content[^6..]).Contains("%%EOF") && info.PageCount == 1,
            "S4-01 PDF: the file is a valid one-page PDF");
        check(Math.Abs(info.WidthMm - 80) < 0.05 && Math.Abs(info.HeightPt - pdf.HeightPt) < 0.01 && info.HeightMm > 80,
            $"S4-01 PDF: the page is 80mm wide and as long as its content ({info.WidthMm:0.0} × {info.HeightMm:0} mm)");
        check(info.EmbeddedFonts == 3 && info.ToUnicodeMaps == 3 && pdf.Content.Length < 200_000,
            $"S4-01 PDF: the three fonts are embedded as subsets with Unicode maps ({pdf.Content.Length / 1024} KB)");

        // Tiếng Việt có dấu: mọi chữ đều có trong font và đọc ra đúng từ tệp.
        check(pdf.MissingCharacters.Count == 0 && info.UnmappedGlyphs == 0
              && new[] { "TẠM TÍNH – KHÔNG PHẢI HOÁ ĐƠN THANH", "Bếp Nhà – chi nhánh Quận Một", "Nguyễn Thị Phương Thảo", "Phở bò tái nạm",
                         "Bào ngư sốt dầu hào", "ĐÃ HUỶ – CÓ TÍNH TIỀN", "[Đã huỷ – có tính tiền]", "TỔNG CỘNG", "Thành tiền" }.All(text.Contains),
            "S4-01 PDF: Vietnamese with diacritics is embedded and reads back exactly");

        // Nội dung khớp màn hình: phần đầu.
        string Line(string startsWith) => info.Lines.FirstOrDefault(l => l.StartsWith(startsWith, StringComparison.Ordinal)) ?? "";
        check(Line("Bàn") == "Bàn " + header.Tables && Line("Bắt đầu phục vụ") == "Bắt đầu phục vụ " + header.ServiceStartedAtText
              && Line("Thời điểm in") == "Thời điểm in 15/10/2026 22:30" && Line("Người in") == "Người in " + header.PrintedBy
              && info.Compact.Contains(BillHeaderRules.ProvisionalNotice.Replace(" ", "")),
            "S4-01 PDF: the header (notice, restaurant, table, service start, print time, printer) matches the preview");

        // Bảng món: mỗi dòng món có tên (kể cả bị ngắt dòng), đơn giá, số lượng và thành tiền trên cùng dòng với dòng đầu của tên.
        var compact = info.Compact;
        // Cột "Món" liền mạch: bỏ "SL Thành tiền" nằm cuối dòng đầu của mỗi dòng món rồi bỏ khoảng trắng.
        var dishColumn = System.Text.RegularExpressions.Regex.Replace(
            string.Concat(info.Lines.Select(l => System.Text.RegularExpressions.Regex.Replace(l, " \\d+ [\\d.]+ đ$", ""))), "\\s+", "");
        foreach (var line in preview.Lines.Concat(preview.ChargedCancelledLines))
        {
            var first = info.Lines.FirstOrDefault(l => l.EndsWith(" " + line.Quantity + " " + BillRules.Format(line.Amount), StringComparison.Ordinal));
            check(first is not null && dishColumn.Contains(line.ItemName.Replace(" ", "")) && dishColumn.Contains((BillRules.Format(line.UnitPrice) + "/" + line.Unit).Replace(" ", "")),
                $"S4-01 PDF: line {line.Id} ({line.ItemName[..Math.Min(20, line.ItemName.Length)]}…) has its name, unit price, quantity and amount");
        }
        check(dishColumn.Contains(("Ít cay · Không hành | Ghi chú: " + longNote).Replace(" ", "")) && dishColumn.Contains("Ghichú:kháchvội")
              && !text.Contains("Gỏi cuốn") && !text.Contains("huỷ trước khi nấu"),
            "S4-01 PDF: notes are printed in full; the dish cancelled before cooking is not in the file");

        // Nhóm món huỷ có tính tiền: sau tiêu đề nhóm, có nhãn; tổng cộng đúng.
        var groupAt = info.Lines.ToList().IndexOf("ĐÃ HUỶ – CÓ TÍNH TIỀN");
        var chargedAt = info.Lines.ToList().FindIndex(l => l.StartsWith("Cá kho tộ", StringComparison.Ordinal));
        check(groupAt > 0 && chargedAt > groupAt && info.Lines.Skip(chargedAt).Contains("[Đã huỷ – có tính tiền]"),
            "S4-01 PDF: the charged cancellation group has its label and lists the cancelled dish");
        check(Line("Cộng món đã gọi") == "Cộng món đã gọi " + BillRules.Format(preview.ItemsSubtotal)
              && Line("TỔNG CỘNG") == "TỔNG CỘNG " + BillRules.Format(preview.Total) && info.Lines.ToList().IndexOf(Line("TỔNG CỘNG")) > chargedAt,
            $"S4-01 PDF: subtotals and the grand total match the preview ({BillRules.Format(preview.Total)})");

        // Bố cục giống bản in: tên món dài và tên không dấu cách xuống dòng trong cột Món, không chèn sang cột số.
        var longFirst = info.Lines.First(l => l.StartsWith("Lẩu thái", StringComparison.Ordinal));
        var noSpaceFirst = info.Lines.First(l => l.StartsWith("Cánhgà", StringComparison.Ordinal));
        check(longFirst.EndsWith(" 2 2.598.000 đ", StringComparison.Ordinal) && !longFirst.Contains("bún tươi")
              && noSpaceFirst.EndsWith(" 3 435.000 đ", StringComparison.Ordinal) && !noSpaceFirst.Contains("nàocả"),
            "S4-01 PDF: long names and names without spaces wrap inside the dish column like the printout");
        check(pdf.Texts.All(t => compact.Contains(t.Replace(" ", ""))),
            "S4-01 PDF: everything laid out is in the file");

        // Nhiều món: vẫn một trang (liền một dải), dài hơn.
        var many = Bill("A05", Enumerable.Range(1, 60).Select(i => Item(i, i % 2 == 0 ? longName : "Trà đá", 5_000m * i, i % 9 + 1, notes: i % 3 == 0 ? longNote : null)));
        var manyPdf = service.Build(many.Preview, many.Header);
        var manyInfo = PdfInspector.Read(manyPdf.Content);
        check(manyInfo.PageCount == 1 && manyInfo.HeightMm > 1000 && Math.Abs(manyInfo.WidthMm - 80) < 0.05
              && manyInfo.Lines.Last(l => l.StartsWith("TỔNG CỘNG", StringComparison.Ordinal)) == "TỔNG CỘNG " + BillRules.Format(many.Preview.Total),
            $"S4-01 PDF: a 60-dish bill is one long page ({manyInfo.HeightMm:0} mm)");

        // Bàn chưa gọi món.
        var empty = Bill("B01", Array.Empty<BillSourceItem>());
        var emptyInfo = PdfInspector.Read(service.Build(empty.Preview, empty.Header).Content);
        check(emptyInfo.Lines.Contains(BillRules.EmptyMessage) && !emptyInfo.Text.Contains("TỔNG CỘNG"), "S4-01 PDF: a table with no dishes shows the empty message");

        // Tên tệp theo quy tắc: TamTinh_{mã bàn}_{yyyyMMdd-HHmm}.pdf (giờ in, giờ Việt Nam), chỉ ký tự an toàn.
        check(pdf.FileName == "TamTinh_A03-A04_20261015-2230.pdf" && BillPdfFileName.For("A01", new DateTime(2026, 1, 5, 7, 3, 0)) == "TamTinh_A01_20260105-0703.pdf"
              && BillPdfFileName.For("Phòng VIP 1/2", new DateTime(2026, 1, 5, 7, 3, 0)) == "TamTinh_Phong-VIP-1-2_20260105-0703.pdf"
              && BillPdfFileName.For("Đ1", new DateTime(2026, 1, 5, 7, 3, 0)) == "TamTinh_D1_20260105-0703.pdf"
              && BillPdfFileName.For(" , ", new DateTime(2026, 1, 5, 7, 3, 0)) == "TamTinh_Ban_20260105-0703.pdf",
            "S4-01 PDF: the file name is TamTinh_{table}_{yyyyMMdd-HHmm}.pdf with safe characters only");

        // Thiếu font → lỗi (controller đổi thành thông báo thân thiện), lần sau vẫn thử lại được.
        var broken = new BillPdfService(Path.Combine(root, "khong-co-thu-muc-font"));
        var failed = false;
        try { broken.Build(preview, header); } catch (FileNotFoundException) { failed = true; } catch (DirectoryNotFoundException) { failed = true; }
        check(failed && BillPdfService.FailureMessage.StartsWith("Không tạo được tệp PDF.", StringComparison.Ordinal),
            "S4-01 PDF: a missing font is reported as a failure, shown to the user as a clear message");

        // Nguồn: nút "Xuất PDF", lỗi hiện trên màn hình, controller chỉ đọc + báo lỗi, font được chép kèm bản build.
        string Source(params string[] parts) => File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray())).Replace("\r\n", "\n");
        var view = Source("src", "RestaurantManagement.Web", "Views", "ProvisionalBill", "Preview.cshtml");
        var controller = Source("src", "RestaurantManagement.Web", "Controllers", "Orders", "ProvisionalBillController.cs");
        var js = Source("src", "RestaurantManagement.Web", "wwwroot", "js", "bill-pdf.js");
        var project = Source("src", "RestaurantManagement.Web", "RestaurantManagement.Web.csproj");
        check(view.Contains("<a class=\"btn btn-outline-primary btn-sm\" href=\"@pdfUrl\" data-preview-pdf>Xuất PDF</a>") && view.Contains("data-preview-error")
              && view.Contains("~/js/bill-pdf.js") && js.Contains(BillPdfService.FailureMessage) && js.Contains("'X-Requested-With': 'fetch'"),
            "S4-01 PDF: the preview has the \"Xuất PDF\" button and shows an error message when the file cannot be created");
        check(controller.Contains("[HttpGet(\"Preview/{code}/Pdf\")]") && controller.Contains("return File(file.Content, \"application/pdf\", file.FileName);")
              && controller.Contains("return Fail(StatusCodes.Status500InternalServerError, BillPdfService.FailureMessage);")
              && project.Contains("<None Update=\"Fonts\\**\\*\" CopyToOutputDirectory=\"PreserveNewest\" CopyToPublishDirectory=\"PreserveNewest\" />"),
            "S4-01 PDF: the download is a read-only GET with friendly errors; fonts ship with the build");
        var pdfApi = ApiAccessMatrix.All.Single(e => e.Key == "ProvisionalBill.Pdf GET");
        check(ApiAccessMatrix.IsAllowed(pdfApi, "Manager") && ApiAccessMatrix.IsAllowed(pdfApi, "Waiter") && !ApiAccessMatrix.IsAllowed(pdfApi, "Kitchen")
              && !ApiAccessMatrix.IsAllowed(pdfApi, "Cashier") && !ApiAccessMatrix.IsAllowed(pdfApi, null),
            "S4-01 PDF: only the manager and the waiter can export the PDF");
    }
}
