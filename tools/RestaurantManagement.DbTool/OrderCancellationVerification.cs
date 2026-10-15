using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using static RestaurantManagement.DbTool.BookingConfirmationVerification;

namespace RestaurantManagement.DbTool;

/// <summary>
/// S3-05 Task 1 (SQL Server thật + web thật): Phục vụ huỷ dòng món đang chờ bếp với lý do bắt buộc.
/// - Hoá đơn: mỗi dòng chờ bếp có nút huỷ; hộp xác nhận có đúng 3 lý do (Quản lý thêm "Quản lý yêu cầu"), không chọn sẵn; Thu ngân không huỷ được.
/// - Huỷ với từng lý do: dòng thành "Đã hủy", không tính tiền, tạm tính giảm đúng số tiền dòng, nhật ký đủ (dòng món, người, thời điểm, lý do).
/// - Món rời màn hình bếp ngay ở lần tải lại kế tiếp (trong 5 giây); bếp bấm nấu món đã huỷ thì bị từ chối kèm lý do rõ ràng.
/// - Thiếu lý do / lý do sai / "Quản lý yêu cầu" của Phục vụ / dòng của hoá đơn khác / tài khoản không có quyền: bị từ chối,
///   kể cả khi gửi thẳng tới máy chủ hoặc gọi thẳng thủ tục SQL.
/// - Món bếp đã bắt đầu: nút của Phục vụ bị vô hiệu kèm lý do, gửi thẳng cũng bị từ chối.
/// - Bếp bắt đầu nấu trong lúc nhân viên chọn lý do: xác nhận bị từ chối, giao diện hiện trạng thái mới; 8 cặp "bếp nấu" / "phục vụ huỷ"
///   gửi đồng thời: mỗi món chỉ một trong hai thành công.
/// - Gửi lặp và 6 yêu cầu song song: chỉ huỷ và trừ tiền một lần.
/// Cần các tài khoản manager/waiter/kitchen/cashier do ApiAuthorizationVerification tạo.
/// </summary>
internal static class OrderCancellationVerification
{
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

        // Ca làm (nếu chưa có) + hai bàn trống, mỗi bàn một phiên. Bàn chính gọi 17 dòng cùng món, phân biệt bằng ghi chú.
        var dishId = await Scalar(connection, """
            SELECT TOP(1) m.Id FROM dbo.MenuItems m JOIN dbo.MenuCategories c ON c.Id=m.CategoryId
            WHERE m.IsActive=1 AND c.IsActive=1 AND m.IsSoldOut=0 AND m.IsTemporarilyOut=0 ORDER BY m.Id
            """);
        var mainItems = JsonSerializer.Serialize(Enumerable.Range(1, 17)
            .Select(n => new { MenuItemId = dishId, Quantity = n % 3 + 1, Notes = $"S305 dòng {n:00}" })).Replace("'", "''");
        var otherItems = JsonSerializer.Serialize(new[] { new { MenuItemId = dishId, Quantity = 1, Notes = "S305 bàn khác" } }).Replace("'", "''");
        await DatabaseTool.Execute(connection, $$"""
            IF NOT EXISTS(SELECT 1 FROM dbo.Shifts WHERE Status='Open')
                EXEC dbo.usp_OpenShift @Name=N'Ca kiểm thử S3-05', @OpeningCash=0, @ActorUserId={{managerId}};
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
        var line = new Dictionary<int, long>();
        for (var n = 1; n <= 17; n++)
            line[n] = await Scalar(connection, $"SELECT Id FROM dbo.OrderItems WHERE Notes=N'S305 dòng {n:00}'");
        var otherLine = await Scalar(connection, "SELECT Id FROM dbo.OrderItems WHERE Notes=N'S305 bàn khác'");
        var sessionId = await Scalar(connection, $"SELECT b.SessionId FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId WHERE i.Id={line[1]}");
        var billUrl = $"/ProvisionalBill/{sessionId}";
        Assert(line.Values.Distinct().Count() == 17 && otherLine > 0 && sessionId > 0, "S3-05: a table with 17 lines waiting for the kitchen and another table");

        Task<long> Subtotal() => Scalar(connection, $"""
            SELECT COALESCE(SUM(CASE WHEN i.Status<>'Cancelled' OR i.ChargeWhenCancelled=1 THEN i.LineTotal ELSE 0 END),0)
            FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId JOIN dbo.DiningSessions s ON s.Id=b.SessionId
            WHERE COALESCE(s.BillingSessionId,s.Id)={sessionId}
            """);
        Task<long> LineTotal(long id) => Scalar(connection, $"SELECT LineTotal FROM dbo.OrderItems WHERE Id={id}");
        static long PageSubtotal(string page) => long.Parse(Regex.Match(page, "data-bill-subtotal=\"(\\d+)\"").Groups[1].Value);
        static string BillRow(string page, long id) => Regex.Match(page, $"<tr data-bill-line=\"{id}\"[\\s\\S]*?</tr>").Value;
        string Unchanged(long id) => $"""
            SELECT CASE WHEN i.Status='Pending' AND i.CancelledAt IS NULL
              AND NOT EXISTS(SELECT 1 FROM dbo.OrderItemEvents e WHERE e.OrderItemId=i.Id AND e.ToStatus='Cancelled') THEN 1 ELSE 0 END
            FROM dbo.OrderItems i WHERE i.Id={id}
            """;

        async Task<HttpResponseMessage> Cancel(HttpClient client, long itemId, string? reason, string? expected = "Pending",
            string? note = null, long? viaSession = null, bool withToken = true, bool confirmCharged = false)
        {
            var values = new List<(string, string)>();
            if (reason is not null) values.Add(("reason", reason));
            if (confirmCharged) values.Add(("confirmCharged", "true"));
            if (expected is not null) values.Add(("expectedStatus", expected));
            if (note is not null) values.Add(("note", note));
            if (withToken) values.Add(("__RequestVerificationToken", Token(await Html(client, billUrl))));
            return await client.PostAsync($"/ProvisionalBill/{viaSession ?? sessionId}/CancelItem/{itemId}", Form(values.ToArray()));
        }

        // Gửi rồi đọc lại hoá đơn (kết quả hiện ở đầu hoá đơn).
        async Task<string> CancelAndRead(HttpClient client, long itemId, string? reason, string? expected = "Pending", string? note = null,
            bool confirmCharged = false)
        {
            using var response = await Cancel(client, itemId, reason, expected, note, confirmCharged: confirmCharged);
            Assert(response.StatusCode == HttpStatusCode.Redirect && Location(response).EndsWith(billUrl, StringComparison.Ordinal),
                $"S3-05: the cancel request for line {itemId} returns to the bill");
            return await Html(client, billUrl);
        }

        async Task Advance(long id, string to)
        {
            var page = await Html(kitchen, "/Kitchen");
            using var response = await kitchen.PostAsync($"/Kitchen/Advance/{id}", Form(("toStatus", to), ("__RequestVerificationToken", Token(page))));
            Assert(response.StatusCode == HttpStatusCode.Redirect, $"S3-05: the kitchen moves line {id} to {to}");
            await Check(connection, $"SELECT CASE WHEN Status='{to}' THEN 1 ELSE 0 END FROM dbo.OrderItems WHERE Id={id}", $"S3-05: line {id} is {to}");
        }

        // 1. Hoá đơn: nút huỷ cho mọi dòng chờ bếp; hộp xác nhận không chọn sẵn lý do; Thu ngân chỉ xem.
        var bill = await Html(waiter, billUrl);
        Assert(line.Values.All(id => bill.Contains($"data-cancel-item=\"{id}\"")) && !bill.Contains($"data-cancel-item=\"{otherLine}\""),
            "S3-05: every line of this bill waiting for the kitchen has a cancel button");
        Assert(bill.Contains("id=\"cancel-item-dialog\"") && Regex.Matches(bill, "<input type=\"radio\" name=\"reason\"").Count == 3
               && bill.Contains("value=\"ChangedMind\"") && bill.Contains("value=\"Mistake\"") && bill.Contains("value=\"SoldOut\"")
               && !bill.Contains("value=\"ManagerOverride\"") && !Regex.IsMatch(bill, "name=\"reason\"[^>]*checked")
               && bill.Contains("data-cancel-confirm disabled"),
            "S3-05: the waiter's dialog offers exactly the three reasons, none pre-selected, and the confirm button starts disabled");
        var managerBill = await Html(manager, billUrl);
        Assert(Regex.Matches(managerBill, "<input type=\"radio\" name=\"reason\"").Count == 4 && managerBill.Contains("value=\"ManagerOverride\""),
            "S3-05: the manager's dialog also offers \"Quản lý yêu cầu\"");
        var cashierBill = await Html(cashier, billUrl);
        Assert(!cashierBill.Contains("data-cancel-item=") && !cashierBill.Contains("cancel-item-dialog"),
            "S3-05: the cashier sees the bill without any cancel action");
        var kitchenPage = await Html(kitchen, "/Kitchen");
        Assert(line.Values.All(id => kitchenPage.Contains($"data-order-item=\"{id}\"")), "S3-05: the kitchen queue shows the 17 new lines");

        // 2. Huỷ với từng lý do: không tính tiền, tạm tính giảm đúng, nhật ký đủ; món rời màn hình bếp trong 5 giây.
        foreach (var (n, reason, label) in new[] { (1, "ChangedMind", "Khách đổi ý"), (2, "Mistake", "Gọi nhầm món"), (3, "SoldOut", "Hết nguyên liệu") })
        {
            var id = line[n];
            var expectedSubtotal = await Subtotal() - await LineTotal(id);
            var page = await CancelAndRead(waiter, id, reason, note: n == 1 ? "  Khách gọi lại món khác  " : null);
            var watch = Stopwatch.StartNew();
            Assert(page.Contains("alert alert-success") && page.Contains("Đã hủy") && page.Contains($"({label})"),
                $"S3-05: the waiter cancels line {n} with the reason \"{label}\" and sees the result");
            Assert(await Subtotal() == expectedSubtotal && PageSubtotal(page) == expectedSubtotal && page.Contains($"data-bill-line=\"{id}\" data-status=\"Cancelled\""),
                $"S3-05: the subtotal drops by exactly the amount of line {n} (database and bill agree)");
            await Check(connection, $"""
                SELECT CASE WHEN i.Status='Cancelled' AND i.ChargeWhenCancelled=0 AND i.CancelReason='{reason}' AND i.CancelledBy={waiterId}
                  AND i.CancelledAt IS NOT NULL
                  AND (SELECT COUNT(*) FROM dbo.OrderItemEvents e WHERE e.OrderItemId=i.Id AND e.ToStatus='Cancelled')=1
                  AND EXISTS(SELECT 1 FROM dbo.OrderItemEvents e WHERE e.OrderItemId=i.Id AND e.ToStatus='Cancelled' AND e.FromStatus='Pending'
                             AND e.ActorUserId={waiterId} AND e.Reason LIKE '{reason}%' AND e.OccurredAt IS NOT NULL)
                 THEN 1 ELSE 0 END FROM dbo.OrderItems i WHERE i.Id={id}
                """, $"S3-05: line {n} is cancelled free of charge and logged once with the line, the waiter, the time and the reason");
            var gone = false;
            while (!gone && watch.Elapsed < TimeSpan.FromSeconds(5))
            {
                gone = !(await Html(kitchen, "/Kitchen")).Contains($"data-order-item=\"{id}\"");
                if (!gone) await Task.Delay(250);
            }
            Assert(gone && watch.Elapsed < TimeSpan.FromSeconds(5), $"S3-05: line {n} leaves the kitchen queue within 5 seconds ({watch.ElapsedMilliseconds} ms)");
        }
        await Check(connection, $"SELECT CASE WHEN CancellationNote=N'Khách gọi lại món khác' AND (SELECT Reason FROM dbo.OrderItemEvents WHERE OrderItemId={line[1]} AND ToStatus='Cancelled')=N'ChangedMind: Khách gọi lại món khác' THEN 1 ELSE 0 END FROM dbo.OrderItems WHERE Id={line[1]}",
            "S3-05: the optional note is kept (trimmed) on the line and in the log");
        var kitchenAfter = await Html(kitchen, "/Kitchen");
        Assert(kitchenAfter.Contains($"data-order-item=\"{line[4]}\""), "S3-05: lines that were not cancelled stay on the kitchen queue");

        // Bếp bấm "Bắt đầu nấu" trên màn hình chưa kịp tải lại: bị từ chối với lý do rõ ràng.
        using (var stale = await kitchen.PostAsync($"/Kitchen/Advance/{line[1]}", Form(("toStatus", "Preparing"), ("__RequestVerificationToken", Token(kitchenAfter)))))
            Assert(stale.StatusCode == HttpStatusCode.Redirect, "S3-05: the kitchen's late click on a cancelled line is answered");
        Assert((await Html(kitchen, "/Kitchen")).Contains("vừa bị hủy nên không cần chế biến nữa"),
            "S3-05: the kitchen is told the line was cancelled");
        await Check(connection, $"SELECT CASE WHEN Status='Cancelled' AND NOT EXISTS(SELECT 1 FROM dbo.OrderItemEvents WHERE OrderItemId={line[1]} AND ToStatus='Preparing') THEN 1 ELSE 0 END FROM dbo.OrderItems WHERE Id={line[1]}",
            "S3-05: a cancelled line can never be cooked");

        // 3. Yêu cầu không hợp lệ bị từ chối, kể cả khi gửi thẳng tới máy chủ.
        foreach (var (reason, text, label) in new (string?, string, string)[]
                 {
                     (null, "Hãy chọn lý do hủy món", "a request without a reason"),
                     ("", "Hãy chọn lý do hủy món", "an empty reason"),
                     ("Hack", "Hãy chọn lý do hủy món", "an unknown reason"),
                     ("ManagerOverride", "chỉ dành cho Quản lý", "the manager-only reason sent by the waiter")
                 })
        {
            var page = await CancelAndRead(waiter, line[9], reason);
            Assert(page.Contains("alert alert-danger") && page.Contains(text), $"S3-05: {label} is refused");
        }
        Assert((await CancelAndRead(waiter, line[9], "Mistake", expected: "Hack")).Contains("Yêu cầu hủy món không hợp lệ"),
            "S3-05: a forged line status is refused");
        Assert((await CancelAndRead(waiter, otherLine, "Mistake")).Contains("không thuộc hoá đơn đang mở"),
            "S3-05: a line of another table cannot be cancelled through this bill");
        await Check(connection, Unchanged(line[9]), "S3-05: the refused requests changed nothing on the line");
        await Check(connection, Unchanged(otherLine), "S3-05: the other table's line is untouched");
        foreach (var (client, who) in new[] { (kitchen, "the kitchen"), (cashier, "the cashier"), (guest, "a visitor who is not signed in") })
            using (var blocked = await Cancel(client, line[9], "Mistake", withToken: false))
                Assert(Blocked(blocked), $"S3-05: {who} cannot cancel a line (blocked by the server)");
        await Refused(connection, $"EXEC dbo.usp_CancelOrderItem @OrderItemId={line[9]},@Reason='ManagerOverride',@ActorUserId={waiterId};", 51036,
            "S3-05 SQL: the waiter cannot use the manager-only reason even by calling the procedure");
        await Refused(connection, $"EXEC dbo.usp_CancelOrderItem @OrderItemId={line[9]},@Reason=NULL,@ActorUserId={waiterId};", 51032,
            "S3-05 SQL: a missing reason is refused by the procedure");
        await Refused(connection, $"EXEC dbo.usp_CancelOrderItem @OrderItemId={line[9]},@Reason='Hack',@ActorUserId={waiterId};", 51032,
            "S3-05 SQL: an unknown reason is refused by the procedure");
        await Refused(connection, $"EXEC dbo.usp_CancelOrderItem @OrderItemId={otherLine},@Reason='Mistake',@ActorUserId={waiterId},@SessionId={sessionId};", 51033,
            "S3-05 SQL: the procedure refuses a line that is not on the given bill");
        await Refused(connection, $"EXEC dbo.usp_CancelOrderItem @OrderItemId={line[9]},@Reason='Mistake',@ActorUserId={kitchenId};", 51001,
            "S3-05 SQL: the kitchen account has no right to cancel");
        await Check(connection, Unchanged(line[9]), "S3-05: line 9 is still waiting for the kitchen after every refused request");

        // 4. Món bếp đã bắt đầu: nút của Phục vụ bị vô hiệu kèm lý do; gửi thẳng cũng bị từ chối.
        await Advance(line[4], "Preparing");
        var row4 = BillRow(await Html(waiter, billUrl), line[4]);
        Assert(row4.Contains($"data-cancel-locked=\"{line[4]}\"") && Regex.IsMatch(row4, "<button[^>]*provisional-cancel-disabled[^>]*disabled")
               && row4.Contains("Bếp đang chế biến món này") && !row4.Contains("data-cancel-item="),
            "S3-05: the waiter's cancel button is disabled for a line being cooked, with the reason shown");
        Assert(BillRow(await Html(manager, billUrl), line[4]).Contains($"data-cancel-item=\"{line[4]}\""),
            "S3-05: the manager still has the cancel action for that line");
        Assert((await CancelAndRead(waiter, line[4], "ChangedMind", expected: null)).Contains("Bếp đã bắt đầu chế biến món này nên Phục vụ không huỷ được"),
            "S3-05: a direct request by the waiter to cancel a line being cooked is refused");
        await Check(connection, $"SELECT CASE WHEN Status='Preparing' AND ChargeWhenCancelled=0 AND NOT EXISTS(SELECT 1 FROM dbo.OrderItemEvents WHERE OrderItemId={line[4]} AND ToStatus='Cancelled') THEN 1 ELSE 0 END FROM dbo.OrderItems WHERE Id={line[4]}",
            "S3-05: the line being cooked is unchanged");

        // 5. Bếp bắt đầu nấu trong lúc Phục vụ chọn lý do (hộp xác nhận đã mở với trạng thái "Chờ bếp").
        var opened = await Html(waiter, billUrl);
        Assert(Regex.Match(opened, $"data-cancel-item=\"{line[5]}\"[^>]*?data-cancel-status=\"([A-Za-z]+)\"").Groups[1].Value == "Pending",
            "S3-05: the dialog for line 5 is opened while it waits for the kitchen");
        await Advance(line[5], "Preparing");
        var raced = await CancelAndRead(waiter, line[5], "ChangedMind", expected: "Pending");
        Assert(raced.Contains("alert alert-danger") && raced.Contains("Bếp đã bắt đầu chế biến món này trong lúc bạn chọn lý do")
               && raced.Contains("Phục vụ không huỷ được nữa"),
            "S3-05: the confirmation is refused because the kitchen started cooking meanwhile");
        var row5 = BillRow(raced, line[5]);
        Assert(row5.Contains("data-status=\"Preparing\"") && row5.Contains("Đang nấu") && row5.Contains($"data-cancel-locked=\"{line[5]}\""),
            "S3-05: the bill shows the new status of line 5 and disables the waiter's button");
        await Check(connection, $"SELECT CASE WHEN Status='Preparing' AND NOT EXISTS(SELECT 1 FROM dbo.OrderItemEvents WHERE OrderItemId={line[5]} AND ToStatus='Cancelled') THEN 1 ELSE 0 END FROM dbo.OrderItems WHERE Id={line[5]}",
            "S3-05: line 5 is still being cooked and not cancelled");

        // Quản lý cũng không bị tính tiền ngoài ý muốn: mở hộp lúc "Chờ bếp", bếp bắt đầu → phải xác nhận lại.
        await Advance(line[6], "Preparing");
        Assert((await CancelAndRead(manager, line[6], "ChangedMind", expected: "Pending")).Contains("Nếu huỷ bây giờ món vẫn tính tiền"),
            "S3-05: the manager is asked again when the kitchen started meanwhile");
        var subtotalBefore6 = await Subtotal();
        // S3-05 Task 3: huỷ có tính tiền cần một trong ba lý do và ô xác nhận "có tính tiền".
        Assert((await CancelAndRead(manager, line[6], "Mistake", expected: "Preparing", confirmCharged: true)).Contains("vẫn tính tiền"),
            "S3-05: after confirming again, the manager's cancellation of a line being cooked is recorded and still charged");
        Assert(await Subtotal() == subtotalBefore6, "S3-05: a line cancelled after cooking started still counts in the subtotal");
        await Check(connection, $"SELECT CASE WHEN Status='Cancelled' AND ChargeWhenCancelled=1 AND CancelledBy={managerId} AND CancelReason='Mistake' THEN 1 ELSE 0 END FROM dbo.OrderItems WHERE Id={line[6]}",
            "S3-05: the manager's late cancellation keeps the charge");

        // 6. Gửi lặp: không huỷ hay trừ tiền thêm.
        var total7 = await LineTotal(line[7]);
        var before7 = await Subtotal();
        Assert((await CancelAndRead(waiter, line[7], "Mistake")).Contains("alert alert-success"), "S3-05: line 7 is cancelled");
        var cancelledAt7 = await Text(connection, $"SELECT CONVERT(varchar(30), CancelledAt, 126) FROM dbo.OrderItems WHERE Id={line[7]}");
        var again = await CancelAndRead(waiter, line[7], "Mistake");
        Assert(again.Contains("alert alert-info") && again.Contains("đã được hủy trước đó") && again.Contains("không trừ tiền thêm"),
            "S3-05: confirming the same cancellation again says it was already done");
        Assert((await CancelAndRead(manager, line[7], "ChangedMind")).Contains("đã được hủy trước đó"),
            "S3-05: another staff member confirming from an old page is told it was already done");
        Assert(await Subtotal() == before7 - total7, "S3-05: the amount of line 7 is deducted only once");
        await Check(connection, $"""
            SELECT CASE WHEN CONVERT(varchar(30), CancelledAt, 126)='{cancelledAt7}' AND CancelReason='Mistake' AND CancelledBy={waiterId}
              AND (SELECT COUNT(*) FROM dbo.OrderItemEvents WHERE OrderItemId={line[7]} AND ToStatus='Cancelled')=1 THEN 1 ELSE 0 END
            FROM dbo.OrderItems WHERE Id={line[7]}
            """, "S3-05: the repeats changed neither the line nor the log");

        // 6 yêu cầu song song (Phục vụ và Quản lý cùng bấm): chỉ một lần huỷ, một lần trừ tiền.
        var total8 = await LineTotal(line[8]);
        var before8 = await Subtotal();
        var waiterToken = Token(await Html(waiter, billUrl));
        var managerToken = Token(await Html(manager, billUrl));
        var parallel = await Task.WhenAll(Enumerable.Range(0, 6).Select(async i =>
        {
            var (client, token) = i % 2 == 0 ? (waiter, waiterToken) : (manager, managerToken);
            using var response = await client.PostAsync($"{billUrl}/CancelItem/{line[8]}",
                Form(("reason", "ChangedMind"), ("expectedStatus", "Pending"), ("__RequestVerificationToken", token)));
            return response.StatusCode;
        }));
        Assert(parallel.All(status => status == HttpStatusCode.Redirect) && await Subtotal() == before8 - total8,
            "S3-05: six simultaneous confirmations deduct line 8 exactly once");
        await Check(connection, $"SELECT CASE WHEN (SELECT COUNT(*) FROM dbo.OrderItemEvents WHERE OrderItemId={line[8]} AND ToStatus='Cancelled')=1 THEN 1 ELSE 0 END",
            "S3-05: six simultaneous confirmations log one cancellation");

        // 7. Bếp bắt đầu nấu và Phục vụ huỷ cùng lúc (8 món): mỗi món chỉ một trong hai thao tác thành công.
        var raceIds = Enumerable.Range(10, 8).Select(n => line[n]).ToArray();
        var kitchenToken = Token(await Html(kitchen, "/Kitchen"));
        waiterToken = Token(await Html(waiter, billUrl));
        await Task.WhenAll(raceIds.SelectMany(id => new[]
        {
            Task.Run(async () =>
            {
                using var cook = await kitchen.PostAsync($"/Kitchen/Advance/{id}", Form(("toStatus", "Preparing"), ("__RequestVerificationToken", kitchenToken)));
            }),
            Task.Run(async () =>
            {
                using var cancel = await waiter.PostAsync($"{billUrl}/CancelItem/{id}",
                    Form(("reason", "SoldOut"), ("expectedStatus", "Pending"), ("__RequestVerificationToken", waiterToken)));
            })
        }));
        foreach (var id in raceIds)
            await Check(connection, $"""
                SELECT CASE WHEN (i.Status='Cancelled' AND i.ChargeWhenCancelled=0
                                  AND NOT EXISTS(SELECT 1 FROM dbo.OrderItemEvents e WHERE e.OrderItemId=i.Id AND e.ToStatus='Preparing'))
                              OR (i.Status='Preparing'
                                  AND NOT EXISTS(SELECT 1 FROM dbo.OrderItemEvents e WHERE e.OrderItemId=i.Id AND e.ToStatus='Cancelled'))
                            THEN 1 ELSE 0 END
                FROM dbo.OrderItems i WHERE i.Id={id}
                """, $"S3-05: line {id} was either cancelled before cooking or cooked without being cancelled, never both");
        var raceList = string.Join(",", raceIds);
        var raceCancelled = await Scalar(connection, $"SELECT COUNT(*) FROM dbo.OrderItems WHERE Id IN ({raceList}) AND Status='Cancelled'");
        var raceCooking = await Scalar(connection, $"SELECT COUNT(*) FROM dbo.OrderItems WHERE Id IN ({raceList}) AND Status='Preparing'");
        Assert(raceCancelled + raceCooking == 8, $"S3-05: 8 simultaneous pairs settled ({raceCancelled} cancelled first, {raceCooking} cooked first)");

        // 8. Kết thúc: tạm tính trên hoá đơn khớp database; nhật ký huỷ đủ cho mọi dòng đã huỷ.
        var finalBill = await Html(waiter, billUrl);
        Assert(PageSubtotal(finalBill) == await Subtotal(), "S3-05: the bill's subtotal matches the database after all cancellations");
        var history = Regex.Match(finalBill, "<section class=\"provisional-cancel-history\"[\\s\\S]*?</section>").Value;
        var cancelledLines = await Scalar(connection, $"SELECT COUNT(*) FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId WHERE b.SessionId={sessionId} AND i.Status='Cancelled'");
        Assert(Regex.Matches(history, "<li>").Count == cancelledLines && history.Contains(waiterName)
               && history.Contains("Khách đổi ý") && history.Contains("Gọi nhầm món") && history.Contains("Hết nguyên liệu")
               && history.Contains("Ghi chú: Khách gọi lại món khác"),
            "S3-05: the bill's cancellation log lists every cancelled line with its reason, who cancelled it and when");
        await Check(connection, $"""
            SELECT CASE WHEN NOT EXISTS(
                SELECT 1 FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId
                WHERE b.SessionId={sessionId} AND i.Status='Cancelled'
                  AND (i.CancelledBy IS NULL OR i.CancelledAt IS NULL OR i.CancelReason IS NULL
                       OR (SELECT COUNT(*) FROM dbo.OrderItemEvents e WHERE e.OrderItemId=i.Id AND e.ToStatus='Cancelled'
                           AND e.ActorUserId=i.CancelledBy AND e.Reason LIKE i.CancelReason+'%')<>1))
             THEN 1 ELSE 0 END
            """, "S3-05: every cancelled line has exactly one log entry with the line, the actor, the time and the reason");
        Console.WriteLine("PASS: S3-05 Task 1 cancel order line checks.");
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
