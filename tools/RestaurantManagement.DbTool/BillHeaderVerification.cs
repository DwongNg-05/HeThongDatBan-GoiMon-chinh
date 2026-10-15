using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using static RestaurantManagement.DbTool.BillPreviewVerification;
using static RestaurantManagement.DbTool.BookingConfirmationVerification;

namespace RestaurantManagement.DbTool;

/// <summary>
/// S4-01 Task 2 (SQL Server thật + web thật): phần đầu hoá đơn tạm tính (docs/S4-01-Task2.md).
/// - Tên quán đúng theo cấu hình (dbo.RestaurantSettings.Name); mã bàn khớp bàn đang mở.
/// - Bắt đầu phục vụ = lúc mở bàn (định nghĩa đã chốt), dạng ngày/tháng/năm giờ:phút giờ Việt Nam.
/// - Thời điểm in đổi mỗi lần mở lại; người in là nhân viên đang đăng nhập — đổi nhân viên thì đổi tên (màn hình và API).
/// - Bàn được chuyển sang bàn khác, rồi gộp với bàn khác: phần đầu vẫn hiện đúng mã bàn hiện tại.
/// Cần các tài khoản manager/waiter do ApiAuthorizationVerification tạo.
/// </summary>
internal static class BillHeaderVerification
{
    private static readonly TimeZoneInfo Vietnam = FindVietnam();

    internal static async Task Run(string connection)
    {
        var password = ApiAuthorizationVerification.Password;
        var originalName = await Text(connection, "SELECT Name FROM dbo.RestaurantSettings WHERE Id=1");
        const string configuredName = "Bếp Nhà – chi nhánh kiểm thử S4-01";
        await DatabaseTool.Execute(connection, $"UPDATE dbo.RestaurantSettings SET Name=N'{configuredName}' WHERE Id=1;");
        try
        {
            await using var web = await Web.Start(connection, new() { ["Email__RetryPollSeconds"] = "0" });
            var waiter = await SignIn(web, "waiter", password);
            var manager = await SignIn(web, "manager", password);
            var waiterId = await Scalar(connection, "SELECT Id FROM dbo.Users WHERE UserName=N'waiter'");
            var managerId = await Scalar(connection, "SELECT Id FROM dbo.Users WHERE UserName=N'manager'");
            var waiterName = await Text(connection, "SELECT FullName FROM dbo.Users WHERE UserName=N'waiter'");
            var managerName = await Text(connection, "SELECT FullName FROM dbo.Users WHERE UserName=N'manager'");
            Assert(waiterName != managerName, "S4-01 header: the two employees have different names");

            var dish = await Scalar(connection, """
                SELECT TOP(1) m.Id FROM dbo.MenuItems m JOIN dbo.MenuCategories c ON c.Id=m.CategoryId
                WHERE m.IsActive=1 AND c.IsActive=1 AND m.IsSoldOut=0 AND m.IsTemporarilyOut=0 ORDER BY m.Id
                """);
            var code = await OpenTable(connection, managerId, waiterId);
            var sessionId = await Scalar(connection, $"SELECT st.SessionId FROM dbo.SessionTables st JOIN dbo.DiningTables t ON t.Id=st.TableId WHERE t.Code='{code}' AND st.ReleasedAt IS NULL");
            await Task.Delay(1100);   // món đầu gọi sau lúc mở bàn: phân biệt hai định nghĩa
            await Submit(connection, sessionId, waiterId, $$"""[{"MenuItemId":{{dish}},"Quantity":1,"Notes":"S401T2 món đầu"}]""");
            var openedUtc = await Utc(connection, $"SELECT OpenedAt FROM dbo.DiningSessions WHERE Id={sessionId}");
            var firstOrderUtc = await Utc(connection, $"SELECT MIN(i.SubmittedAt) FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId WHERE b.SessionId={sessionId}");
            Assert(firstOrderUtc > openedUtc, "S4-01 header: the first dish is ordered after the table was opened");

            // 1. Nhân viên A (Phục vụ) mở tạm tính.
            var url = Preview(code);
            var before = DateTime.UtcNow.AddSeconds(-1);
            var pageA = await Html(waiter, url);
            var after = DateTime.UtcNow.AddSeconds(1);
            var headA = Header(pageA);
            Assert(headA.Restaurant == configuredName, $"S4-01 header: the restaurant name comes from the configuration ({headA.Restaurant})");
            Assert(headA.Tables == code, $"S4-01 header: the table code matches the open table ({headA.Tables})");
            Assert(headA.ServiceStartUtc == openedUtc && headA.ServiceStartText == Format(openedUtc) && headA.ServiceStartUtc != firstOrderUtc,
                $"S4-01 header: the service start is when the table was opened ({headA.ServiceStartText})");
            Assert(headA.PrintedBy == waiterName, $"S4-01 header: the printer is the signed-in waiter ({headA.PrintedBy})");
            Assert(headA.PrintedUtc >= before && headA.PrintedUtc <= after && headA.PrintedText == Format(headA.PrintedUtc)
                   && Regex.IsMatch(headA.PrintedText, @"^\d{2}/\d{2}/\d{4} \d{2}:\d{2}$"),
                $"S4-01 header: the print time is now, as day/month/year hour:minute ({headA.PrintedText})");
            Assert(pageA.Contains("data-preview-notice>TẠM TÍNH – KHÔNG PHẢI HOÁ ĐƠN THANH TOÁN<")
                   && pageA.IndexOf("data-preview-header", StringComparison.Ordinal) < pageA.IndexOf("<table", StringComparison.Ordinal),
                "S4-01 header: the provisional notice and the header are above the item table");

            // 2. Mở lại: thời điểm in cập nhật.
            await Task.Delay(1100);
            var headA2 = Header(await Html(waiter, url));
            Assert(headA2.PrintedUtc > headA.PrintedUtc && headA2.PrintedBy == waiterName && headA2.ServiceStartUtc == openedUtc,
                "S4-01 header: opening the bill again updates the print time; the service start stays the same");

            // 3. Nhân viên B (Quản lý) mở lại cùng bàn: tên đổi thành B, giờ in cập nhật.
            await Task.Delay(1100);
            var headB = Header(await Html(manager, url));
            Assert(headB.PrintedBy == managerName && headB.PrintedUtc > headA2.PrintedUtc && headB.Tables == code,
                $"S4-01 header: another employee opening the same table shows their own name ({headB.PrintedBy})");

            // 4. API trả cùng phần đầu, người in theo người gọi.
            var apiA = (await Json(waiter, Api(code))).GetProperty("header");
            var apiB = (await Json(manager, Api(code))).GetProperty("header");
            Assert(apiA.GetProperty("restaurantName").GetString() == configuredName && apiA.GetProperty("tables").GetString() == code
                   && apiA.GetProperty("serviceStartedAtText").GetString() == Format(openedUtc) && apiA.GetProperty("serviceStartDefinition").GetString() == "TableOpened"
                   && apiA.GetProperty("printedBy").GetString() == waiterName && apiB.GetProperty("printedBy").GetString() == managerName
                   && apiA.GetProperty("notice").GetString() == "TẠM TÍNH – KHÔNG PHẢI HOÁ ĐƠN THANH TOÁN",
                "S4-01 header API: the header is returned with the right restaurant, table, service start and printer");

            // 5. Chuyển bàn: phiên chuyển sang một bàn trống khác → phần đầu hiện mã bàn mới; bàn cũ không còn tạm tính.
            // Cùng khu vực với bàn cũ (usp_MergeSessions ở bước 6 kiểm tra khu vực của mọi bàn từng thuộc phiên).
            var newTableId = await Scalar(connection, $"""
                SELECT TOP(1) t.Id FROM dbo.DiningTables t JOIN dbo.Areas a ON a.Id=t.AreaId
                WHERE t.IsActive=1 AND a.IsActive=1 AND t.Status='Available' AND t.MaxCapacity>=2
                  AND t.AreaId=(SELECT AreaId FROM dbo.DiningTables WHERE Code='{code}')
                  AND NOT EXISTS(SELECT 1 FROM dbo.SessionTables st WHERE st.TableId=t.Id AND st.ReleasedAt IS NULL)
                ORDER BY t.Id
                """);
            await DatabaseTool.Execute(connection, $"""
                BEGIN TRANSACTION;
                DECLARE @old int=(SELECT TableId FROM dbo.SessionTables WHERE SessionId={sessionId} AND ReleasedAt IS NULL);
                UPDATE dbo.SessionTables SET ReleasedAt=SYSUTCDATETIME() WHERE SessionId={sessionId} AND ReleasedAt IS NULL;
                INSERT dbo.SessionTables(SessionId,TableId) VALUES({sessionId},{newTableId});
                UPDATE dbo.DiningTables SET Status='Available',StatusChangedAt=SYSUTCDATETIME() WHERE Id=@old;
                UPDATE dbo.DiningTables SET Status='Serving',StatusChangedAt=SYSUTCDATETIME() WHERE Id={newTableId};
                COMMIT;
                """);
            var movedCode = await Text(connection, $"SELECT Code FROM dbo.DiningTables WHERE Id={newTableId}");
            var moved = Header(await Html(waiter, Preview(movedCode)));
            Assert(moved.Tables == movedCode && moved.ServiceStartUtc == openedUtc,
                $"S4-01 header: after moving the guests from {code} to {movedCode} the header shows the current table");
            using (var oldTable = await waiter.GetAsync(Preview(code)))
                Assert(oldTable.StatusCode == HttpStatusCode.NotFound, "S4-01 header: the table the guests left no longer has a provisional bill");

            // 6. Gộp bàn: một bàn cùng khu vực gộp vào → mở từ bàn nào cũng thấy mã các bàn hiện tại của nhóm.
            var areaId = await Scalar(connection, $"SELECT AreaId FROM dbo.DiningTables WHERE Id={newTableId}");
            var childCode = await OpenTable(connection, managerId, waiterId, areaId);
            var childSession = await Scalar(connection, $"SELECT st.SessionId FROM dbo.SessionTables st JOIN dbo.DiningTables t ON t.Id=st.TableId WHERE t.Code='{childCode}' AND st.ReleasedAt IS NULL");
            await DatabaseTool.Execute(connection, $"EXEC dbo.usp_MergeSessions @MainSessionId={sessionId},@ChildSessionId={childSession},@ActorUserId={managerId};");
            var expectedTables = string.Join(", ", new[] { movedCode, childCode }.Order(StringComparer.Ordinal));
            var fromChild = Header(await Html(waiter, Preview(childCode)));
            var fromMain = Header(await Html(manager, Preview(movedCode)));
            Assert(fromChild.Tables == expectedTables && fromMain.Tables == expectedTables && fromChild.ServiceStartUtc == openedUtc
                   && fromMain.PrintedBy == managerName && fromChild.PrintedBy == waiterName,
                $"S4-01 header: merged tables show every current table code ({expectedTables}), opened from either table");
            Console.WriteLine($"PASS: S4-01 Task 2 provisional bill header checks ({expectedTables}).");
        }
        finally
        {
            await DatabaseTool.Execute(connection, $"UPDATE dbo.RestaurantSettings SET Name=N'{originalName.Replace("'", "''")}' WHERE Id=1;");
        }
    }

    private sealed record HeaderView(string Restaurant, string Tables, DateTime ServiceStartUtc, string ServiceStartText,
        DateTime PrintedUtc, string PrintedText, string PrintedBy);

    private static string Preview(string code) => $"/ProvisionalBill/Preview/{Uri.EscapeDataString(code)}";
    private static string Api(string code) => $"/api/tables/{Uri.EscapeDataString(code)}/provisional-bill";

    private static HeaderView Header(string page)
    {
        string Value(string pattern) => Regex.Match(page, pattern).Groups[1].Value.Trim();
        var serviceStart = Regex.Match(page, "data-preview-service-start=\"([^\"]+)\">([^<]*)<");
        var printed = Regex.Match(page, "data-preview-printed-at=\"([^\"]+)\">([^<]*)<");
        Assert(serviceStart.Success && printed.Success, "S4-01 header: the page has the service start and print time");
        return new HeaderView(Value("data-preview-restaurant>([^<]*)<"), Value("<dd data-preview-tables>([^<]*)<"),
            ParseUtc(serviceStart.Groups[1].Value), serviceStart.Groups[2].Value.Trim(),
            ParseUtc(printed.Groups[1].Value), printed.Groups[2].Value.Trim(), Value("data-preview-printed-by>([^<]*)<"));
    }

    private static DateTime ParseUtc(string iso) =>
        DateTime.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    private static async Task<DateTime> Utc(string connection, string sql) =>
        DateTime.SpecifyKind(DateTime.Parse(await Text(connection, $"SELECT CONVERT(varchar(30),({sql}),126)"), CultureInfo.InvariantCulture), DateTimeKind.Utc);

    /// <summary>Định dạng đã chốt, viết độc lập với BillHeaderRules để đối chiếu.</summary>
    private static string Format(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(utc, Vietnam).ToString("dd'/'MM'/'yyyy HH':'mm", CultureInfo.InvariantCulture);

    private static TimeZoneInfo FindVietnam()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time"); }
    }
}
