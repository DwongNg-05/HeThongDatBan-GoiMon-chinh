using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using static RestaurantManagement.DbTool.BillPreviewVerification;
using static RestaurantManagement.DbTool.BookingConfirmationVerification;

namespace RestaurantManagement.DbTool;

/// <summary>
/// S4-01 Task 4 (SQL Server thật + web thật): xuất hoá đơn tạm tính thành tệp PDF khổ 80mm (docs/S4-01-Task4.md).
/// - Nút "Xuất PDF" trên màn hình tạm tính; tệp tải về là PDF mở được, khổ rộng 80mm, một trang, tên đúng quy tắc
///   TamTinh_{mã bàn}_{yyyyMMdd-HHmm}.pdf theo giờ in.
/// - Nội dung tệp (đọc lại chữ thật trong PDF) khớp màn hình xem trước: phần đầu, từng dòng món (tên, SL, thành tiền, ghi chú dài),
///   nhóm "ĐÃ HUỶ – CÓ TÍNH TIỀN", tổng cộng; món huỷ trước khi nấu không có; tiếng Việt có dấu đúng.
/// - Xuất PDF không đổi bàn / phiên / dòng món; xuất xong vẫn gọi thêm món được và tệp kế tiếp có món mới.
/// - Lỗi: bàn không còn khách → thông báo (JSON cho nút, chuyển về màn hình khi mở thẳng); Bếp, Thu ngân, khách bị chặn.
/// Cần các tài khoản manager/waiter/kitchen/cashier do ApiAuthorizationVerification tạo.
/// </summary>
internal static class BillPdfVerification
{
    private static readonly TimeZoneInfo Vietnam = FindVietnam();

    internal static async Task Run(string connection)
    {
        var password = ApiAuthorizationVerification.Password;
        await using var web = await Web.Start(connection, new() { ["Email__RetryPollSeconds"] = "0" });
        var waiter = await SignIn(web, "waiter", password);
        var kitchen = await SignIn(web, "kitchen", password);
        var cashier = await SignIn(web, "cashier", password);
        var guest = NewClient(web);
        var waiterId = await Scalar(connection, "SELECT Id FROM dbo.Users WHERE UserName=N'waiter'");
        var managerId = await Scalar(connection, "SELECT Id FROM dbo.Users WHERE UserName=N'manager'");
        var kitchenId = await Scalar(connection, "SELECT Id FROM dbo.Users WHERE UserName=N'kitchen'");
        var dish = await Scalar(connection, """
            SELECT TOP(1) m.Id FROM dbo.MenuItems m JOIN dbo.MenuCategories c ON c.Id=m.CategoryId
            WHERE m.IsActive=1 AND c.IsActive=1 AND m.IsSoldOut=0 AND m.IsTemporarilyOut=0 ORDER BY LEN(m.Name) DESC, m.Id
            """);
        var dishName = await Text(connection, $"SELECT Name FROM dbo.MenuItems WHERE Id={dish}");

        const string longNote = "S401T4 khách dị ứng đậu phộng, làm riêng nước chấm, mang ra sau món khai vị, thêm chanh ớt riêng cho bé ở ghế số 3";
        var code = await OpenTable(connection, managerId, waiterId);
        var sessionId = await Scalar(connection, $"SELECT st.SessionId FROM dbo.SessionTables st JOIN dbo.DiningTables t ON t.Id=st.TableId WHERE t.Code='{code}' AND st.ReleasedAt IS NULL");
        await Submit(connection, sessionId, waiterId, $$"""
            [{"MenuItemId":{{dish}},"Quantity":3,"Notes":"{{longNote}}"},
             {"MenuItemId":{{dish}},"Quantity":1},
             {"MenuItemId":{{dish}},"Quantity":2,"Notes":"S401T4 huỷ trước"},
             {"MenuItemId":{{dish}},"Quantity":1,"Notes":"S401T4 huỷ sau"}]
            """);
        var freeCancel = await Scalar(connection, "SELECT Id FROM dbo.OrderItems WHERE Notes=N'S401T4 huỷ trước'");
        var chargedCancel = await Scalar(connection, "SELECT Id FROM dbo.OrderItems WHERE Notes=N'S401T4 huỷ sau'");
        await DatabaseTool.Execute(connection, $"""
            EXEC dbo.usp_CancelOrderItem @OrderItemId={freeCancel},@Reason='ChangedMind',@ActorUserId={waiterId};
            EXEC dbo.usp_TransitionOrderItem @OrderItemId={chargedCancel},@ToStatus='Preparing',@ActorUserId={kitchenId};
            EXEC dbo.usp_CancelOrderItem @OrderItemId={chargedCancel},@Reason='SoldOut',@ActorUserId={managerId},@ConfirmCharged=1;
            """);

        var previewUrl = $"/ProvisionalBill/Preview/{Uri.EscapeDataString(code)}";
        var pdfUrl = previewUrl + "/Pdf";
        var before = await Snapshot(connection, code, sessionId);

        // 1. Nút "Xuất PDF".
        var page = await Html(waiter, previewUrl);
        Assert(page.Contains($"href=\"{pdfUrl}\" data-preview-pdf>Xuất PDF</a>") && Regex.IsMatch(page, "js/bill-pdf\\.js")
               && Regex.IsMatch(page, "data-preview-error hidden=\"hidden\""), "S4-01 PDF: the preview has the \"Xuất PDF\" button (no error shown)");

        // 2. Tải tệp như nút "Xuất PDF": PDF mở được, 80mm, một trang, tên đúng quy tắc.
        var startUtc = DateTime.UtcNow;
        var (bytes, fileName) = await Download(waiter, pdfUrl);
        var endUtc = DateTime.UtcNow;
        var info = PdfInspector.Read(bytes);
        Assert(info.PageCount == 1 && Math.Abs(info.WidthMm - 80) < 0.05 && info.EmbeddedFonts == 3 && info.UnmappedGlyphs == 0,
            $"S4-01 PDF: the file opens as a one-page PDF, 80mm wide ({info.WidthMm:0.0} × {info.HeightMm:0} mm)");
        var expectedNames = new[] { startUtc, endUtc }.Select(t => $"TamTinh_{Regex.Replace(code, "[^A-Za-z0-9]+", "-").Trim('-')}_{Local(t).ToString("yyyyMMdd'-'HHmm", CultureInfo.InvariantCulture)}.pdf").Distinct().ToArray();
        Assert(expectedNames.Contains(fileName), $"S4-01 PDF: the file name follows TamTinh_{{table}}_{{yyyyMMdd-HHmm}}.pdf ({fileName})");

        // 3. Nội dung tệp khớp màn hình xem trước (mở lại ngay sau khi tải).
        page = await Html(waiter, previewUrl);
        var screen = page[..page.IndexOf("<section class=\"bill-print\"", StringComparison.Ordinal)];
        string Inner(string html, string name) => Regex.Match(html, $"{name}[^>]*>([^<]*)<").Groups[1].Value.Trim();
        string? PdfLine(string start) => info.Lines.FirstOrDefault(l => l.StartsWith(start, StringComparison.Ordinal));
        Assert(info.Compact.Contains(Regex.Replace(Inner(screen, "data-preview-restaurant"), "\\s+", "")) && PdfLine("Bàn") == "Bàn " + Inner(screen, "<dd data-preview-tables")
               && PdfLine("Bắt đầu phục vụ") == "Bắt đầu phục vụ " + Inner(screen, "data-preview-service-start")
               && PdfLine("Người in") == "Người in " + Inner(screen, "data-preview-printed-by")
               && PdfLine("Thời điểm in") is { } printed && Regex.IsMatch(printed, @"^Thời điểm in \d{2}/\d{2}/\d{4} \d{2}:\d{2}$")
               && info.Compact.Contains("TẠMTÍNH–KHÔNGPHẢIHOÁĐƠNTHANHTOÁN"),
            "S4-01 PDF: the header matches the preview (restaurant, table, service start, printer, print time, notice)");
        var rows = Regex.Matches(screen, "<tr data-preview-line=\"(\\d+)\"[^>]*data-quantity=\"(\\d+)\"[\\s\\S]*?data-preview-amount>([^<]*)<")
            .Select(m => (Id: long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), Quantity: m.Groups[2].Value, Amount: m.Groups[3].Value.Trim())).ToList();
        var dishColumn = Regex.Replace(string.Concat(info.Lines.Select(l => Regex.Replace(l, " \\d+ [\\d.]+ đ$", ""))), "\\s+", "");
        Assert(rows.Count == 3 && rows.All(r => r.Id != freeCancel)
               && rows.All(r => info.Lines.Count(l => l.EndsWith($" {r.Quantity} {r.Amount}", StringComparison.Ordinal)) >= 1)
               && info.Lines.Count(l => Regex.IsMatch(l, " \\d+ [\\d.]+ đ$") && !l.StartsWith("Cộng", StringComparison.Ordinal) && !l.StartsWith("TỔNG", StringComparison.Ordinal)) == 3
               && dishColumn.Contains(Regex.Replace(dishName, "\\s+", "")),
            $"S4-01 PDF: every dish line of the preview is in the file with its quantity and amount ({dishName})");
        Assert(dishColumn.Contains(Regex.Replace("Ghi chú: " + longNote, "\\s+", "")) && !info.Text.Contains("S401T4 huỷ trước"),
            "S4-01 PDF: the long note is printed in full with correct Vietnamese; the dish cancelled before cooking is absent");
        var group = info.Lines.ToList().IndexOf("ĐÃ HUỶ – CÓ TÍNH TIỀN");
        Assert(group > 0 && info.Lines.Skip(group).Contains("[Đã huỷ – có tính tiền]") && dishColumn.Contains("Ghichú:S401T4huỷsau"),
            "S4-01 PDF: the charged cancellation group is in the file with its label");
        Assert(PdfLine("TỔNG CỘNG") == "TỔNG CỘNG " + Inner(screen, "data-preview-total"),
            $"S4-01 PDF: the total matches the preview ({Inner(screen, "data-preview-total")})");

        // 4. Xuất PDF không đổi gì; vẫn gọi thêm món được, tệp kế tiếp có món mới.
        await Download(waiter, pdfUrl);
        using (var direct = await waiter.GetAsync(pdfUrl))
            Assert(direct.StatusCode == HttpStatusCode.OK && direct.Content.Headers.ContentType?.MediaType == "application/pdf",
                "S4-01 PDF: opening the PDF link directly also downloads the file");
        Assert(await Snapshot(connection, code, sessionId) == before, "S4-01 PDF: exporting the PDF leaves the table and order unchanged");
        await Submit(connection, sessionId, waiterId, $$"""[{"MenuItemId":{{dish}},"Quantity":4,"Notes":"S401T4 gọi thêm sau khi xuất"}]""");
        var again = PdfInspector.Read((await Download(waiter, pdfUrl)).Bytes);
        var newTotal = Inner(await Html(waiter, previewUrl), "data-preview-total");
        Assert(again.Compact.Contains("S401T4gọithêmsaukhixuất") && again.Lines.FirstOrDefault(l => l.StartsWith("TỔNG CỘNG", StringComparison.Ordinal)) == "TỔNG CỘNG " + newTotal,
            $"S4-01 PDF: after exporting a dish can still be added and the next file includes it ({newTotal})");
        await Check(connection, $"""
            SELECT CASE WHEN t.Status='Serving' AND s.Status='Open' THEN 1 ELSE 0 END
            FROM dbo.DiningTables t JOIN dbo.SessionTables st ON st.TableId=t.Id AND st.ReleasedAt IS NULL
            JOIN dbo.DiningSessions s ON s.Id=st.SessionId WHERE t.Code='{code}'
            """, "S4-01 PDF: the table is still being served after exporting");

        // 5. Lỗi và quyền.
        using (var missing = await Fetch(waiter, "/ProvisionalBill/Preview/ZZ99X/Pdf"))
            Assert(missing.StatusCode == HttpStatusCode.NotFound && (await missing.Content.ReadAsStringAsync()).Contains("Không tìm thấy bàn ZZ99X"),
                "S4-01 PDF: when the file cannot be created the button gets a message to show");
        using (var redirect = await waiter.GetAsync("/ProvisionalBill/Preview/ZZ99X/Pdf"))
            Assert(redirect.StatusCode == HttpStatusCode.Redirect && Location(redirect).EndsWith("/ProvisionalBill/Preview/ZZ99X", StringComparison.Ordinal),
                "S4-01 PDF: opening the PDF link of a table without a bill goes back to the preview with the message");
        foreach (var (client, who) in new[] { (kitchen, "the kitchen"), (cashier, "the cashier"), (guest, "a visitor") })
            using (var blocked = await client.GetAsync(pdfUrl))
                Assert(Blocked(blocked), $"S4-01 PDF: {who} cannot export the PDF");
        Console.WriteLine($"PASS: S4-01 Task 4 provisional bill PDF checks ({fileName}, {bytes.Length / 1024} KB).");
    }

    private static Task<HttpResponseMessage> Fetch(HttpClient client, string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Requested-With", "fetch");
        return client.SendAsync(request);
    }

    private static async Task<(byte[] Bytes, string FileName)> Download(HttpClient client, string url)
    {
        using var response = await Fetch(client, url);
        Assert(response.StatusCode == HttpStatusCode.OK && response.Content.Headers.ContentType?.MediaType == "application/pdf",
            $"S4-01 PDF: {url} returns a PDF ({(int)response.StatusCode} {response.Content.Headers.ContentType})");
        var disposition = response.Content.Headers.ContentDisposition;
        var name = (disposition?.FileNameStar ?? disposition?.FileName ?? "").Trim('"');
        Assert(disposition?.DispositionType == "attachment" && name.Length > 0, "S4-01 PDF: the response is a file download with a name");
        return (await response.Content.ReadAsByteArrayAsync(), name);
    }

    private static DateTime Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Vietnam);

    private static TimeZoneInfo FindVietnam()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time"); }
    }
}
