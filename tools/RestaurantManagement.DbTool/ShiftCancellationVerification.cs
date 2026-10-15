using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using static RestaurantManagement.DbTool.BookingConfirmationVerification;

namespace RestaurantManagement.DbTool;

/// <summary>
/// S3-05 Task 2 (SQL Server thật + web thật): Quản lý xem lại các lần huỷ món trong báo cáo cuối ca.
/// - Đúng ca: mọi lần huỷ của phiên thuộc ca đang mở nằm trong báo cáo của ca đó và không nằm ở ca khác; số dòng báo cáo khớp database.
/// - Đủ thông tin: đơn (hoá đơn, bàn, đợt), dòng món, người thực hiện, thời điểm, lý do (kèm ghi chú), cách tính tiền.
/// - Không trùng khi xác nhận lặp: bấm lại "huỷ" cho dòng đã huỷ không thêm dòng nhật ký nào.
/// - Loại sự kiện theo phạm vi đã chốt: huỷ không tính tiền, huỷ vẫn tính tiền (Quản lý huỷ món bếp đã bắt đầu) và yêu cầu bị từ chối
///   (thiếu lý do, lý do chỉ dành cho Quản lý, trạng thái giả, bếp đã bắt đầu, dòng của hoá đơn khác).
/// - Ca không có lượt huỷ: thông báo "Ca này không có lần huỷ món nào."; không tải được báo cáo: thông báo lỗi (trang) / 503 (API).
/// - Phục vụ, Bếp, Thu ngân, khách bị chặn cả trang lẫn API dữ liệu; gọi thẳng thủ tục SQL bằng tài khoản không phải Quản lý bị từ chối (51001).
/// Cần các tài khoản manager/waiter/kitchen/cashier do ApiAuthorizationVerification tạo.
/// </summary>
internal static class ShiftCancellationVerification
{
    private const string PageUrl = "/Reports/Shift/Cancellations";
    private const string DataUrl = "/api/reports/shift-cancellations";

    internal static async Task Run(string connection)
    {
        var password = ApiAuthorizationVerification.Password;
        await using var web = await Web.Start(connection, new() { ["Email__RetryPollSeconds"] = "0" });
        var waiter = await SignIn(web, "waiter", password);
        var manager = await SignIn(web, "manager", password);
        var kitchen = await SignIn(web, "kitchen", password);
        var cashier = await SignIn(web, "cashier", password);
        var guest = NewClient(web);
        var waiterId = await Scalar(connection, "SELECT Id FROM dbo.Users WHERE UserName=N'waiter'");
        var managerId = await Scalar(connection, "SELECT Id FROM dbo.Users WHERE UserName=N'manager'");
        var kitchenId = await Scalar(connection, "SELECT Id FROM dbo.Users WHERE UserName=N'kitchen'");
        var waiterName = await Text(connection, "SELECT FullName FROM dbo.Users WHERE UserName=N'waiter'");
        var managerName = await Text(connection, "SELECT FullName FROM dbo.Users WHERE UserName=N'manager'");

        // Ca đang mở (mở mới nếu chưa có) + một ca đã chốt không có lượt huỷ nào; hai bàn, bàn chính gọi 3 dòng.
        var dishId = await Scalar(connection, """
            SELECT TOP(1) m.Id FROM dbo.MenuItems m JOIN dbo.MenuCategories c ON c.Id=m.CategoryId
            WHERE m.IsActive=1 AND c.IsActive=1 AND m.IsSoldOut=0 AND m.IsTemporarilyOut=0 ORDER BY m.Id
            """);
        var mainItems = JsonSerializer.Serialize(Enumerable.Range(1, 3)
            .Select(n => new { MenuItemId = dishId, Quantity = n, Notes = $"S305R dòng {n}" })).Replace("'", "''");
        var otherItems = JsonSerializer.Serialize(new[] { new { MenuItemId = dishId, Quantity = 1, Notes = "S305R bàn khác" } }).Replace("'", "''");
        await DatabaseTool.Execute(connection, $$"""
            IF NOT EXISTS(SELECT 1 FROM dbo.Shifts WHERE Status='Open')
                EXEC dbo.usp_OpenShift @Name=N'Ca kiểm thử S3-05 báo cáo', @OpeningCash=0, @ActorUserId={{managerId}};
            INSERT dbo.Shifts(Name, BusinessDate, OpenedBy, OpenedAt, OpeningCash, Status, ClosedBy, ClosedAt)
            VALUES(N'S305R ca trống', CAST(DATEADD(day,-1,SYSUTCDATETIME()) AS date), {{managerId}}, DATEADD(hour,-30,SYSUTCDATETIME()), 0, 'Closed',
                   {{managerId}}, DATEADD(hour,-26,SYSUTCDATETIME()));
            DECLARE @tables TABLE(N int IDENTITY, Id int);
            INSERT @tables(Id) SELECT TOP(2) t.Id FROM dbo.DiningTables t JOIN dbo.Areas a ON a.Id=t.AreaId
              WHERE t.IsActive=1 AND a.IsActive=1 AND t.Status='Available' AND t.MaxCapacity>=2
                AND NOT EXISTS(SELECT 1 FROM dbo.SessionTables st WHERE st.TableId=t.Id AND st.ReleasedAt IS NULL)
                AND NOT EXISTS(SELECT 1 FROM dbo.Reservations r WHERE r.TableId=t.Id AND r.Status='Confirmed'
                               AND r.StartsAt<DATEADD(minute,(SELECT DefaultBookingMinutes FROM dbo.RestaurantSettings WHERE Id=1),SYSUTCDATETIME())
                               AND r.EndsAt>SYSUTCDATETIME())
              ORDER BY t.Id;
            DECLARE @t1 int=(SELECT Id FROM @tables WHERE N=1), @t2 int=(SELECT Id FROM @tables WHERE N=2);
            EXEC dbo.usp_OpenSession @TableId=@t1,@GuestCount=2,@ActorUserId={{waiterId}};
            EXEC dbo.usp_OpenSession @TableId=@t2,@GuestCount=2,@ActorUserId={{waiterId}};
            DECLARE @s1 bigint=(SELECT SessionId FROM dbo.SessionTables WHERE TableId=@t1 AND ReleasedAt IS NULL);
            DECLARE @s2 bigint=(SELECT SessionId FROM dbo.SessionTables WHERE TableId=@t2 AND ReleasedAt IS NULL);
            DECLARE @r1 uniqueidentifier=NEWID(), @r2 uniqueidentifier=NEWID();
            EXEC dbo.usp_SubmitOrder @SessionId=@s1,@RequestId=@r1,@ItemsJson=N'{{mainItems}}',@ActorUserId={{waiterId}};
            EXEC dbo.usp_SubmitOrder @SessionId=@s2,@RequestId=@r2,@ItemsJson=N'{{otherItems}}',@ActorUserId={{waiterId}};
            """);
        var shiftId = await Scalar(connection, "SELECT Id FROM dbo.Shifts WHERE Status='Open'");
        var emptyShiftId = await Scalar(connection, "SELECT MAX(Id) FROM dbo.Shifts WHERE Name=N'S305R ca trống'");
        var line = new Dictionary<int, long>();
        for (var n = 1; n <= 3; n++)
            line[n] = await Scalar(connection, $"SELECT Id FROM dbo.OrderItems WHERE Notes=N'S305R dòng {n}'");
        var otherLine = await Scalar(connection, "SELECT Id FROM dbo.OrderItems WHERE Notes=N'S305R bàn khác'");
        var sessionId = await Scalar(connection, $"SELECT b.SessionId FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId WHERE i.Id={line[1]}");
        var tableCode = await Text(connection, $"SELECT t.Code FROM dbo.OrderItems i JOIN dbo.DiningTables t ON t.Id=i.OriginalTableId WHERE i.Id={line[1]}");
        await Check(connection, $"SELECT CASE WHEN ShiftId={shiftId} THEN 1 ELSE 0 END FROM dbo.DiningSessions WHERE Id={sessionId}",
            "S3-05 report: the test table is served in the open shift");
        var billUrl = $"/ProvisionalBill/{sessionId}";
        var ourLines = string.Join(",", line.Values.Append(otherLine));

        async Task<string> Cancel(HttpClient client, long itemId, string? reason, string? expected = "Pending", string? note = null,
            bool confirmCharged = false)
        {
            var values = new List<(string, string)>();
            if (reason is not null) values.Add(("reason", reason));
            if (confirmCharged) values.Add(("confirmCharged", "true"));
            if (expected is not null) values.Add(("expectedStatus", expected));
            if (note is not null) values.Add(("note", note));
            values.Add(("__RequestVerificationToken", Token(await Html(client, billUrl))));
            using var response = await client.PostAsync($"{billUrl}/CancelItem/{itemId}", Form(values.ToArray()));
            Assert(response.StatusCode == HttpStatusCode.Redirect, $"S3-05 report: the cancel request for line {itemId} is answered");
            return await Html(client, billUrl);
        }
        Task<long> Rejections(string where) => Scalar(connection, $"SELECT COUNT(*) FROM dbo.OrderCancelRejections WHERE {where}");
        Task<long> Events(long id) => Scalar(connection, $"SELECT COUNT(*) FROM dbo.OrderItemEvents WHERE OrderItemId={id} AND ToStatus='Cancelled'");

        // 1. Huỷ thành công không tính tiền (Phục vụ, món còn chờ bếp), rồi xác nhận lặp hai lần: không trùng.
        Assert((await Cancel(waiter, line[1], "ChangedMind", note: "Khách gọi món khác")).Contains("alert alert-success"),
            "S3-05 report: the waiter cancels line 1 before cooking");
        Assert((await Cancel(waiter, line[1], "ChangedMind")).Contains("đã được hủy trước đó")
               && (await Cancel(manager, line[1], "Mistake")).Contains("đã được hủy trước đó"),
            "S3-05 report: repeated confirmations are told the line was already cancelled");
        Assert(await Events(line[1]) == 1 && await Rejections($"OrderItemId={line[1]}") == 0,
            "S3-05 report: repeated confirmations add nothing to the cancellation log");

        // 2. Bếp bắt đầu dòng 2: Phục vụ bị từ chối (ghi nhật ký), Quản lý huỷ — vẫn tính tiền.
        var kitchenPage = await Html(kitchen, "/Kitchen");
        using (var cook = await kitchen.PostAsync($"/Kitchen/Advance/{line[2]}", Form(("toStatus", "Preparing"), ("__RequestVerificationToken", Token(kitchenPage)))))
            Assert(cook.StatusCode == HttpStatusCode.Redirect, "S3-05 report: the kitchen starts line 2");
        Assert((await Cancel(waiter, line[2], "ChangedMind", expected: null)).Contains("alert alert-danger"),
            "S3-05 report: the waiter cannot cancel line 2 once cooking started");
        Assert((await Cancel(manager, line[2], "Mistake", expected: "Preparing", note: "Khách phàn nàn", confirmCharged: true)).Contains("vẫn tính tiền"),
            "S3-05 report: the manager cancels line 2 after cooking started and it is still charged");

        // 3. Yêu cầu bị từ chối khác: thiếu lý do, lý do chỉ dành cho Quản lý, trạng thái giả, dòng của hoá đơn khác.
        Assert((await Cancel(waiter, line[3], null)).Contains("alert alert-danger"), "S3-05 report: a request without a reason is refused");
        Assert((await Cancel(waiter, line[3], "ManagerOverride")).Contains("alert alert-danger"), "S3-05 report: the manager-only reason is refused for the waiter");
        Assert((await Cancel(waiter, line[3], "Mistake", expected: "Hack")).Contains("alert alert-danger"), "S3-05 report: a forged status is refused");
        Assert((await Cancel(waiter, otherLine, "Mistake")).Contains("alert alert-danger"), "S3-05 report: a line of another bill is refused");
        await Check(connection, $"SELECT CASE WHEN Status='Pending' AND NOT EXISTS(SELECT 1 FROM dbo.OrderItemEvents WHERE OrderItemId={line[3]} AND ToStatus='Cancelled') THEN 1 ELSE 0 END FROM dbo.OrderItems WHERE Id={line[3]}",
            "S3-05 report: the refused requests did not cancel line 3");

        // Mỗi yêu cầu bị từ chối được ghi đúng một lần, đúng ca, đúng người, đúng mã lỗi.
        foreach (var (where, label) in new[]
                 {
                     ($"OrderItemId={line[2]} AND ErrorNumber=51035 AND ItemStatus='Preparing' AND Reason=N'ChangedMind'", "waiter on a line being cooked (51035)"),
                     ($"OrderItemId={line[3]} AND ErrorNumber=51032 AND Reason IS NULL", "missing reason (51032)"),
                     ($"OrderItemId={line[3]} AND ErrorNumber=51036 AND Reason=N'ManagerOverride'", "manager-only reason (51036)"),
                     ($"OrderItemId={line[3]} AND ErrorNumber=51037 AND Reason=N'Mistake'", "forged status (51037)"),
                     ($"OrderItemId={otherLine} AND ErrorNumber=51033 AND SessionId={sessionId}", "line of another bill (51033)")
                 })
            Assert(await Rejections($"{where} AND ShiftId={shiftId} AND ActorUserId={waiterId} AND LEN(Message)>0") == 1,
                $"S3-05 report: the refused request is logged once in the right shift — {label}");
        Assert(await Rejections($"OrderItemId IN ({ourLines})") == 5, "S3-05 report: exactly five refused requests were logged for the test lines");

        // 4. Báo cáo (API dữ liệu) của ca: đúng ca, đủ thông tin, không trùng.
        using (var response = await manager.GetAsync($"{DataUrl}?shiftId={shiftId}"))
        {
            Assert(response.StatusCode == HttpStatusCode.OK, "S3-05 report: the manager gets the shift's cancellation data");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = json.RootElement;
            var entries = root.GetProperty("entries").EnumerateArray().ToArray();
            var expectedCount = await Scalar(connection, $"""
                SELECT (SELECT COUNT(*) FROM dbo.OrderItemEvents e JOIN dbo.OrderItems i ON i.Id=e.OrderItemId JOIN dbo.OrderBatches b ON b.Id=i.BatchId
                        JOIN dbo.DiningSessions s ON s.Id=b.SessionId WHERE e.ToStatus='Cancelled' AND s.ShiftId={shiftId})
                     + (SELECT COUNT(*) FROM dbo.OrderCancelRejections WHERE ShiftId={shiftId})
                """);
            Assert(root.GetProperty("shift").GetProperty("id").GetInt64() == shiftId && entries.Length == expectedCount,
                $"S3-05 report: the report lists every cancellation of the shift and nothing else ({entries.Length} entries)");
            JsonElement[] Of(long id) => entries.Where(e => e.GetProperty("orderItemId").ValueKind == JsonValueKind.Number && e.GetProperty("orderItemId").GetInt64() == id).ToArray();
            static string S(JsonElement e, string name) => e.GetProperty(name).ValueKind == JsonValueKind.Null ? "" : e.GetProperty(name).GetString() ?? "";

            var one = Of(line[1]);
            Assert(one.Length == 1 && S(one[0], "kind") == "Cancelled" && S(one[0], "chargeKind") == "free" && S(one[0], "reason") == "ChangedMind"
                   && S(one[0], "note") == "Khách gọi món khác" && S(one[0], "actorName") == waiterName && S(one[0], "actorRole") == "Waiter"
                   && one[0].GetProperty("billSessionId").GetInt64() == sessionId && S(one[0], "tableCode") == tableCode
                   && one[0].GetProperty("batchNumber").ValueKind == JsonValueKind.Number && S(one[0], "itemName").Length > 0
                   && S(one[0], "occurredAtLabel").Length > 0 && S(one[0], "chargeLabel").StartsWith("Không tính tiền"),
                "S3-05 report: line 1 appears once, free of charge, with the order, line, waiter, time, reason and note");
            var two = Of(line[2]);
            Assert(two.Length == 2 && S(two[0], "kind") == "Rejected" && S(two[0], "actorName") == waiterName && S(two[0], "chargeKind") == "none"
                   && S(two[0], "message").Length > 0
                   && S(two[1], "kind") == "Cancelled" && S(two[1], "chargeKind") == "charged" && S(two[1], "reason") == "Mistake"
                   && S(two[1], "actorName") == managerName && S(two[1], "actorRole") == "Manager" && S(two[1], "chargeLabel").StartsWith("Vẫn tính tiền"),
                "S3-05 report: line 2 shows the waiter's refused request, then the manager's charged cancellation, in time order");
            Assert(Of(line[3]).Length == 3 && Of(line[3]).All(e => S(e, "kind") == "Rejected") && Of(otherLine).Length == 1,
                "S3-05 report: every refused request is listed as refused");
            var times = entries.Select(e => e.GetProperty("occurredAtUtc").GetDateTime()).ToArray();
            Assert(times.Zip(times.Skip(1)).All(p => p.First <= p.Second), "S3-05 report: entries are in time order");
            Assert(root.GetProperty("rejectedCount").GetInt32() == entries.Count(e => S(e, "kind") == "Rejected")
                   && root.GetProperty("cancelledCount").GetInt32() == entries.Count(e => S(e, "kind") == "Cancelled"),
                "S3-05 report: the totals match the entries");
        }
        using (var none = await manager.GetAsync($"{DataUrl}?shiftId={emptyShiftId}"))
        {
            using var json = JsonDocument.Parse(await none.Content.ReadAsStringAsync());
            Assert(none.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("entries").GetArrayLength() == 0
                   && json.RootElement.GetProperty("cancelledCount").GetInt32() == 0,
                "S3-05 report: the other shift has no cancellations (entries belong to their own shift only)");
        }
        using (var missing = await manager.GetAsync($"{DataUrl}?shiftId=999999999"))
            Assert(missing.StatusCode == HttpStatusCode.NotFound, "S3-05 report: an unknown shift answers 404");

        // 5. Trang báo cáo của Quản lý.
        var page = await Html(manager, $"{PageUrl}?shiftId={shiftId}");
        string Row(long id) => string.Join("\n", Regex.Matches(page, $"<tr data-kind=\"\\w+\" data-order-item-id=\"{id}\"[\\s\\S]*?</tr>").Select(m => m.Value));
        Assert(page.Contains("id=\"shift-cancellations-table\"") && !page.Contains("id=\"shift-cancellations-empty\"") && !page.Contains("id=\"shift-cancellations-error\""),
            "S3-05 report: the manager opens the end-of-shift report and sees the cancellation table");
        var row1 = Row(line[1]);
        Assert(row1.Contains("data-charge=\"free\"") && row1.Contains("Đã hủy") && row1.Contains($"Hoá đơn #{sessionId}") && row1.Contains($"Bàn {tableCode}")
               && row1.Contains(waiterName) && row1.Contains("Phục vụ") && row1.Contains("Khách đổi ý") && row1.Contains("Ghi chú: Khách gọi món khác")
               && row1.Contains("Không tính tiền") && Regex.IsMatch(row1, "<time datetime=\"[^\"]+\">\\d{2}:\\d{2}:\\d{2} \\d{2}/\\d{2}/\\d{4}</time>"),
            "S3-05 report: the row shows order, line, actor, time, reason and that the line is not charged");
        var row2 = Row(line[2]);
        Assert(row2.Contains("data-charge=\"charged\"") && row2.Contains("Vẫn tính tiền") && row2.Contains("Gọi nhầm món") && row2.Contains("Đã huỷ có tính tiền") && row2.Contains(managerName)
               && row2.Contains("data-kind=\"Rejected\"") && row2.Contains("Bị từ chối"),
            "S3-05 report: the charged cancellation and the refused request are told apart");
        Assert(Regex.Matches(page, $"data-order-item-id=\"{line[1]}\"").Count == 1, "S3-05 report: the repeated confirmations are not listed twice");
        var emptyPage = await Html(manager, $"{PageUrl}?shiftId={emptyShiftId}");
        Assert(emptyPage.Contains("id=\"shift-cancellations-empty\"") && emptyPage.Contains("Ca này không có lần huỷ món nào.") && !emptyPage.Contains("id=\"shift-cancellations-table\""),
            "S3-05 report: a shift without cancellations shows a clear message");
        Assert((await Html(manager, $"{PageUrl}?shiftId=999999999")).Contains("Không tìm thấy ca này."),
            "S3-05 report: an unknown shift shows a clear message");
        Assert((await Html(manager, PageUrl)).Contains($"<option value=\"{shiftId}\" selected=\"selected\""),
            "S3-05 report: without a choice the page opens on the open shift");
        Assert((await Html(manager, "/Cashier/Shift")).Contains("id=\"shift-cancellations-link\"") && !(await Html(cashier, "/Cashier/Shift")).Contains("id=\"shift-cancellations-link\""),
            "S3-05 report: the shift close screen links the report for the manager only");

        // 6. Không tải được báo cáo (thủ tục tạm thời không dùng được): trang hiện lỗi, API trả 503 — không giả vờ là ca trống.
        await DatabaseTool.Execute(connection, "EXEC sp_rename 'dbo.usp_ShiftCancellationReport', 'usp_ShiftCancellationReport_S305off';");
        try
        {
            var broken = await Html(manager, $"{PageUrl}?shiftId={shiftId}");
            Assert(broken.Contains("id=\"shift-cancellations-error\"") && broken.Contains("Không tải được báo cáo huỷ món")
                   && !broken.Contains("id=\"shift-cancellations-empty\""),
                "S3-05 report: when the report cannot be loaded the page says so");
            using var failed = await manager.GetAsync($"{DataUrl}?shiftId={shiftId}");
            Assert(failed.StatusCode == HttpStatusCode.ServiceUnavailable, "S3-05 report: the data API answers 503 when the report cannot be loaded");
        }
        finally
        {
            await DatabaseTool.Execute(connection, "EXEC sp_rename 'dbo.usp_ShiftCancellationReport_S305off', 'usp_ShiftCancellationReport';");
        }

        // 7. Chỉ Quản lý: nhân viên khác bị chặn cả trên giao diện và khi gọi thẳng dữ liệu / thủ tục.
        foreach (var (client, who) in new[] { (waiter, "the waiter"), (kitchen, "the kitchen"), (cashier, "the cashier"), (guest, "a visitor") })
        {
            using var blockedPage = await client.GetAsync($"{PageUrl}?shiftId={shiftId}");
            using var blockedData = await client.GetAsync($"{DataUrl}?shiftId={shiftId}");
            Assert(Blocked(blockedPage) && Blocked(blockedData), $"S3-05 report: {who} cannot open the report page or request its data");
        }
        Assert(!(await Html(waiter, "/")).Contains("data-nav-item=\"shift-cancellations\""), "S3-05 report: the waiter does not see the report on the navigation bar");
        await Refused(connection, $"EXEC dbo.usp_ShiftCancellationReport @ShiftId={shiftId}, @ActorUserId={waiterId};", 51001,
            "S3-05 report SQL: the waiter cannot read the report even by calling the procedure");
        await Refused(connection, $"EXEC dbo.usp_LogOrderCancelRejection @OrderItemId={line[3]}, @ActorUserId={kitchenId}, @ErrorNumber=51032, @Message=N'x';", 51001,
            "S3-05 report SQL: an account without the ordering right cannot write to the log");
        await Refused(connection, $"DELETE dbo.OrderCancelRejections WHERE OrderItemId={line[3]};", 51140,
            "S3-05 report SQL: the refusal log cannot be deleted");
        Assert(await Rejections($"OrderItemId IN ({ourLines})") == 5, "S3-05 report: the refusal log is unchanged");
        Console.WriteLine("PASS: S3-05 Task 2 end-of-shift cancellation report checks.");
    }

    private static async Task Refused(string connection, string sql, int number, string name)
    {
        try { await DatabaseTool.Execute(connection, sql); }
        catch (SqlException ex)
        {
            Assert(ex.Number == number, name + (ex.Number == number ? "" : $" (got {ex.Number}: {ex.Message})"));
            return;
        }
        Assert(false, name + $" (expected error {number}, but the call succeeded)");
    }

    private static bool Blocked(HttpResponseMessage response)
    {
        var location = Location(response);
        return response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
            || (response.StatusCode == HttpStatusCode.Redirect && (location.Contains("/Account/AccessDenied") || location.Contains("/Account/Login")));
    }

    private static async Task<string> Text(string connection, string sql)
    {
        await using var cn = new SqlConnection(connection);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        return Convert.ToString(await cmd.ExecuteScalarAsync()) ?? "";
    }

    private static HttpClient NewClient(Web web)
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() };
        return new HttpClient(handler, disposeHandler: true) { BaseAddress = web.Client.BaseAddress, Timeout = TimeSpan.FromSeconds(30) };
    }

    private static async Task<HttpClient> SignIn(Web web, string user, string password)
    {
        var client = NewClient(web);
        await Login(client, user, password);
        return client;
    }
}
