using System.Net;
using System.Text.RegularExpressions;
using static RestaurantManagement.DbTool.BillPreviewVerification;
using static RestaurantManagement.DbTool.BookingConfirmationVerification;

namespace RestaurantManagement.DbTool;

/// <summary>
/// S4-01 Task 3 (SQL Server thật + web thật): in hoá đơn tạm tính khổ 80mm (docs/S4-01-Task3.md).
/// - Màn hình tạm tính có nút "In" và bản in 80mm; CSS/JS in được phục vụ.
/// - Nội dung bản in KHỚP màn hình xem trước: cùng phần đầu, cùng dòng món theo thứ tự, số lượng, thành tiền, ghi chú (không cắt),
///   cùng nhóm "Đã huỷ – có tính tiền" (in rõ nhãn), cùng tổng cộng; món huỷ trước khi nấu không có trên bản in.
/// - In (hay huỷ hộp thoại in) không đổi gì: trạng thái bàn, phiên, dòng món giữ nguyên; in xong vẫn gọi thêm món được,
///   món mới có ngay trên bản in kế tiếp.
/// Bố cục trên trình duyệt thật (vừa khổ, không cắt cột, một trang): tools/tests/bill-print-layout.check.cjs.
/// Cần các tài khoản manager/waiter/kitchen do ApiAuthorizationVerification tạo.
/// </summary>
internal static class BillPrintVerification
{
    internal static async Task Run(string connection)
    {
        var password = ApiAuthorizationVerification.Password;
        await using var web = await Web.Start(connection, new() { ["Email__RetryPollSeconds"] = "0" });
        var waiter = await SignIn(web, "waiter", password);
        var waiterId = await Scalar(connection, "SELECT Id FROM dbo.Users WHERE UserName=N'waiter'");
        var managerId = await Scalar(connection, "SELECT Id FROM dbo.Users WHERE UserName=N'manager'");
        var kitchenId = await Scalar(connection, "SELECT Id FROM dbo.Users WHERE UserName=N'kitchen'");
        var dish = await Scalar(connection, """
            SELECT TOP(1) m.Id FROM dbo.MenuItems m JOIN dbo.MenuCategories c ON c.Id=m.CategoryId
            WHERE m.IsActive=1 AND c.IsActive=1 AND m.IsSoldOut=0 AND m.IsTemporarilyOut=0 ORDER BY LEN(m.Name) DESC, m.Id
            """);

        // Bàn: 2 món (một món ghi chú dài gần 200 ký tự), 1 món huỷ trước khi nấu, 1 món huỷ sau khi nấu (có tính tiền).
        var longNote = "S401T3 " + string.Join(" ", Enumerable.Repeat("khách dị ứng đậu phộng làm riêng nước chấm", 4));
        longNote = longNote[..Math.Min(200, longNote.Length)].Trim();
        var code = await OpenTable(connection, managerId, waiterId);
        var sessionId = await Scalar(connection, $"SELECT st.SessionId FROM dbo.SessionTables st JOIN dbo.DiningTables t ON t.Id=st.TableId WHERE t.Code='{code}' AND st.ReleasedAt IS NULL");
        await Submit(connection, sessionId, waiterId, $$"""
            [{"MenuItemId":{{dish}},"Quantity":3,"Notes":"{{longNote}}"},
             {"MenuItemId":{{dish}},"Quantity":1},
             {"MenuItemId":{{dish}},"Quantity":2,"Notes":"S401T3 huỷ trước"},
             {"MenuItemId":{{dish}},"Quantity":1,"Notes":"S401T3 huỷ sau"}]
            """);
        var ids = await Scalar(connection, $"SELECT COUNT(*) FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId WHERE b.SessionId={sessionId}");
        Assert(ids == 4, "S4-01 print: the table has 4 order lines");
        var freeCancel = await Scalar(connection, "SELECT Id FROM dbo.OrderItems WHERE Notes=N'S401T3 huỷ trước'");
        var chargedCancel = await Scalar(connection, "SELECT Id FROM dbo.OrderItems WHERE Notes=N'S401T3 huỷ sau'");
        await DatabaseTool.Execute(connection, $"""
            EXEC dbo.usp_CancelOrderItem @OrderItemId={freeCancel},@Reason='ChangedMind',@ActorUserId={waiterId};
            EXEC dbo.usp_TransitionOrderItem @OrderItemId={chargedCancel},@ToStatus='Preparing',@ActorUserId={kitchenId};
            EXEC dbo.usp_CancelOrderItem @OrderItemId={chargedCancel},@Reason='SoldOut',@ActorUserId={managerId},@ConfirmCharged=1;
            """);

        var url = $"/ProvisionalBill/Preview/{Uri.EscapeDataString(code)}";
        var before = await Snapshot(connection, code, sessionId);
        var page = await Html(waiter, url);

        // 1. Nút "In", bản in, CSS/JS in.
        Assert(page.Contains("data-preview-print>In</button>") && page.Contains("data-print-receipt") && Regex.IsMatch(page, "bill-print\\.css")
               && Regex.IsMatch(page, "js/bill-print\\.js"), "S4-01 print: the preview has the \"In\" button and the 80mm printout");
        using (var css = await waiter.GetAsync("/css/bill-print.css"))
            Assert(css.StatusCode == HttpStatusCode.OK && (await css.Content.ReadAsStringAsync()).Contains("body > :not(.bill-print) { display: none !important; }"),
                "S4-01 print: the print stylesheet hides everything but the receipt");
        using (var js = await waiter.GetAsync("/js/bill-print.js"))
            Assert(js.StatusCode == HttpStatusCode.OK && (await js.Content.ReadAsStringAsync()).Contains("size: 80mm"), "S4-01 print: the print script sets the 80mm page");

        // 2. Bản in khớp màn hình.
        var printout = Regex.Match(page, "<section class=\"bill-print\" data-print-receipt[\\s\\S]*?</section>").Value;
        var screen = page[..page.IndexOf("<section class=\"bill-print\"", StringComparison.Ordinal)];
        Assert(printout.Length > 0 && !printout.Contains("<button") && !printout.Contains("<form") && !printout.Contains("<a "),
            "S4-01 print: the printout has no buttons, forms or links");
        var screenLines = Lines(screen, "data-preview-line");
        var printLines = Lines(printout, "data-print-line");
        Assert(screenLines.SequenceEqual(printLines) && printLines.Count == 3 && printLines.All(l => l.Id != freeCancel)
               && printLines[^1].Id == chargedCancel,
            $"S4-01 print: the printout has the same lines, order and amounts as the preview ({string.Join(", ", printLines)})");
        string Attr(string html, string name) => Regex.Match(html, $"{name}=\"([^\"]*)\"").Groups[1].Value;
        string Inner(string html, string name) => Regex.Match(html, $"{name}[^>]*>([^<]*)<").Groups[1].Value.Trim();
        Assert(Attr(screen, "data-preview-total") == Attr(printout, "data-print-total") && Inner(screen, "data-preview-total") == Inner(printout, "data-print-total"),
            $"S4-01 print: the printed total equals the preview total ({Inner(printout, "data-print-total")})");
        foreach (var (screenField, printField) in new[] { ("data-preview-restaurant", "data-print-restaurant"), ("<dd data-preview-tables", "data-print-tables"),
                     ("data-preview-service-start", "data-print-service-start"), ("data-preview-printed-at", "data-print-printed-at"),
                     ("data-preview-printed-by", "data-print-printed-by"), ("data-preview-notice", "data-print-notice") })
            Assert(Inner(screen, screenField) == Inner(printout, printField) && Inner(printout, printField).Length > 0,
                $"S4-01 print: the printed header matches the preview ({printField} = {Inner(printout, printField)})");
        Assert(printout.Contains($"data-print-note>Ghi chú: {longNote}<") && Regex.Matches(printout, "data-print-note>").Count == 2,
            "S4-01 print: notes are printed in full (not cut), including a long note");
        var group = Regex.Match(printout, "<tbody class=\"bp-charged\" data-print-group=\"charged-cancelled\">[\\s\\S]*?</tbody>").Value;
        Assert(group.Contains("data-print-group-label>ĐÃ HUỶ – CÓ TÍNH TIỀN<") && group.Contains($"data-print-line=\"{chargedCancel}\"")
               && group.Contains("data-print-charged-label>[Đã huỷ – có tính tiền]<"),
            "S4-01 print: the charged cancellation group is printed with a clear label");

        // 3. In / huỷ hộp thoại in (chỉ ở trình duyệt) không đổi gì; mở lại để in nhiều lần cũng vậy.
        for (var i = 0; i < 3; i++) await Html(waiter, url);
        Assert(await Snapshot(connection, code, sessionId) == before, "S4-01 print: printing or cancelling the print dialog leaves the table and order unchanged");

        // 4. In xong vẫn gọi thêm món được; bản in kế tiếp có món mới.
        await Submit(connection, sessionId, waiterId, $$"""[{"MenuItemId":{{dish}},"Quantity":2,"Notes":"S401T3 gọi thêm sau khi in"}]""");
        var added = await Scalar(connection, "SELECT Id FROM dbo.OrderItems WHERE Notes=N'S401T3 gọi thêm sau khi in'");
        var again = await Html(waiter, url);
        var againPrint = Regex.Match(again, "<section class=\"bill-print\" data-print-receipt[\\s\\S]*?</section>").Value;
        var againScreen = again[..again.IndexOf("<section class=\"bill-print\"", StringComparison.Ordinal)];
        Assert(Lines(againPrint, "data-print-line").Any(l => l.Id == added) && Lines(againScreen, "data-preview-line").SequenceEqual(Lines(againPrint, "data-print-line"))
               && Attr(againScreen, "data-preview-total") == Attr(againPrint, "data-print-total"),
            "S4-01 print: after printing a dish can still be added and the next printout includes it");
        await Check(connection, $"""
            SELECT CASE WHEN t.Status='Serving' AND s.Status='Open' THEN 1 ELSE 0 END
            FROM dbo.DiningTables t JOIN dbo.SessionTables st ON st.TableId=t.Id AND st.ReleasedAt IS NULL
            JOIN dbo.DiningSessions s ON s.Id=st.SessionId WHERE t.Code='{code}'
            """, "S4-01 print: the table is still being served after printing");
        Console.WriteLine($"PASS: S4-01 Task 3 provisional bill print checks (table {code}).");
    }

    private sealed record PrintedLine(long Id, long Amount)
    {
        public override string ToString() => $"#{Id}={Amount}";
    }

    /// <summary>Các dòng món theo thứ tự (mã dòng + thành tiền) của màn hình (data-preview-line) hoặc bản in (data-print-line).</summary>
    private static List<PrintedLine> Lines(string html, string attribute) =>
        Regex.Matches(html, $"<tr[^>]*{attribute}=\"(\\d+)\"[^>]*data-amount=\"(\\d+)\"")
            .Select(m => new PrintedLine(long.Parse(m.Groups[1].Value), long.Parse(m.Groups[2].Value)))
            .ToList();
}
