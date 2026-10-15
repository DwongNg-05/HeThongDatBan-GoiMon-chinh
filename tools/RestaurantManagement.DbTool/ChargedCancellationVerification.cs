using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using static RestaurantManagement.DbTool.BookingConfirmationVerification;

namespace RestaurantManagement.DbTool;

/// <summary>
/// S3-05 Task 3 (SQL Server thật + web thật): Quản lý huỷ món đã bắt đầu chế biến, món vẫn tính tiền.
/// - Quản lý huỷ được món Đang nấu và Đã xong (chưa mang ra); món Đã mang ra không huỷ được (51143).
/// - Bắt buộc một trong ba lý do ("Quản lý yêu cầu" bị từ chối — 51142) và ô xác nhận huỷ có tính tiền (51144).
/// - Phục vụ, Bếp, Thu ngân, khách không làm được cùng thao tác (giao diện và gửi thẳng); thủ tục SQL cũng từ chối.
/// - Tạm tính không giảm; dòng món vẫn trên hoá đơn với nhãn "Đã huỷ có tính tiền"; hoá đơn khi thanh toán vẫn tính dòng đó.
/// - Bếp: món rời hàng đợi, thẻ "DỪNG MÓN" hiện trong 5 giây; Bếp bấm "Đã dừng món" một lần (bấm lặp không ghi thêm).
/// - Nhật ký đủ (dòng, người, thời điểm, lý do, trạng thái lúc huỷ) và báo cáo cuối ca phân biệt huỷ có / không tính tiền.
/// - Xác nhận lặp không ghi thêm; bếp đổi trạng thái trong lúc Quản lý chọn lý do → từ chối, phải xác nhận lại.
/// Cần các tài khoản manager/waiter/kitchen/cashier do ApiAuthorizationVerification tạo.
/// </summary>
internal static class ChargedCancellationVerification
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
        var cashierId = await Scalar(connection, "SELECT Id FROM dbo.Users WHERE UserName=N'cashier'");
        var managerName = await Text(connection, "SELECT FullName FROM dbo.Users WHERE UserName=N'manager'");

        // Ca đang mở + một bàn gọi 4 dòng (phân biệt bằng ghi chú).
        var dishId = await Scalar(connection, """
            SELECT TOP(1) m.Id FROM dbo.MenuItems m JOIN dbo.MenuCategories c ON c.Id=m.CategoryId
            WHERE m.IsActive=1 AND c.IsActive=1 AND m.IsSoldOut=0 AND m.IsTemporarilyOut=0 ORDER BY m.Id
            """);
        var items = JsonSerializer.Serialize(Enumerable.Range(1, 4)
            .Select(n => new { MenuItemId = dishId, Quantity = n, Notes = $"S305C dòng {n}" })).Replace("'", "''");
        await DatabaseTool.Execute(connection, $$"""
            IF NOT EXISTS(SELECT 1 FROM dbo.Shifts WHERE Status='Open')
                EXEC dbo.usp_OpenShift @Name=N'Ca kiểm thử S3-05 huỷ có tính tiền', @OpeningCash=0, @ActorUserId={{managerId}};
            DECLARE @t int=(SELECT TOP(1) t.Id FROM dbo.DiningTables t JOIN dbo.Areas a ON a.Id=t.AreaId
              WHERE t.IsActive=1 AND a.IsActive=1 AND t.Status='Available' AND t.MaxCapacity>=2
                AND NOT EXISTS(SELECT 1 FROM dbo.SessionTables st WHERE st.TableId=t.Id AND st.ReleasedAt IS NULL)
                AND NOT EXISTS(SELECT 1 FROM dbo.Reservations r WHERE r.TableId=t.Id AND r.Status='Confirmed'
                               AND r.StartsAt<DATEADD(minute,(SELECT DefaultBookingMinutes FROM dbo.RestaurantSettings WHERE Id=1),SYSUTCDATETIME())
                               AND r.EndsAt>SYSUTCDATETIME())
              ORDER BY t.Id);
            EXEC dbo.usp_OpenSession @TableId=@t,@GuestCount=2,@ActorUserId={{waiterId}};
            DECLARE @s bigint=(SELECT SessionId FROM dbo.SessionTables WHERE TableId=@t AND ReleasedAt IS NULL);
            DECLARE @r uniqueidentifier=NEWID();
            EXEC dbo.usp_SubmitOrder @SessionId=@s,@RequestId=@r,@ItemsJson=N'{{items}}',@ActorUserId={{waiterId}};
            """);
        var line = new Dictionary<int, long>();
        for (var n = 1; n <= 4; n++)
            line[n] = await Scalar(connection, $"SELECT Id FROM dbo.OrderItems WHERE Notes=N'S305C dòng {n}'");
        var sessionId = await Scalar(connection, $"SELECT b.SessionId FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId WHERE i.Id={line[1]}");
        var shiftId = await Scalar(connection, $"SELECT ShiftId FROM dbo.DiningSessions WHERE Id={sessionId}");
        var billUrl = $"/ProvisionalBill/{sessionId}";

        Task<long> Subtotal() => Scalar(connection, $"""
            SELECT COALESCE(SUM(CASE WHEN i.Status<>'Cancelled' OR i.ChargeWhenCancelled=1 THEN i.LineTotal ELSE 0 END),0)
            FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId JOIN dbo.DiningSessions s ON s.Id=b.SessionId
            WHERE COALESCE(s.BillingSessionId,s.Id)={sessionId}
            """);
        static long PageSubtotal(string page) => long.Parse(Regex.Match(page, "data-bill-subtotal=\"(\\d+)\"").Groups[1].Value);
        static string BillRow(string page, long id) => Regex.Match(page, $"<tr data-bill-line=\"{id}\"[\\s\\S]*?</tr>").Value;
        Task<long> Events(long id) => Scalar(connection, $"SELECT COUNT(*) FROM dbo.OrderItemEvents WHERE OrderItemId={id} AND ToStatus='Cancelled'");
        Task<string> Status(long id) => Text(connection, $"SELECT Status FROM dbo.OrderItems WHERE Id={id}");

        async Task<HttpResponseMessage> Post(HttpClient client, long itemId, string? reason, string? expected, bool confirmCharged, bool withToken = true)
        {
            var values = new List<(string, string)>();
            if (reason is not null) values.Add(("reason", reason));
            if (expected is not null) values.Add(("expectedStatus", expected));
            if (confirmCharged) values.Add(("confirmCharged", "true"));
            if (withToken) values.Add(("__RequestVerificationToken", Token(await Html(client, billUrl))));
            return await client.PostAsync($"{billUrl}/CancelItem/{itemId}", Form(values.ToArray()));
        }
        async Task<string> Cancel(HttpClient client, long itemId, string? reason, string? expected, bool confirmCharged)
        {
            using var response = await Post(client, itemId, reason, expected, confirmCharged);
            Assert(response.StatusCode == HttpStatusCode.Redirect && Location(response).EndsWith(billUrl, StringComparison.Ordinal),
                $"S3-05 charged: the cancel request for line {itemId} returns to the bill");
            return await Html(client, billUrl);
        }
        async Task Advance(long id, string to)
        {
            var page = await Html(kitchen, "/Kitchen");
            using var response = await kitchen.PostAsync($"/Kitchen/Advance/{id}", Form(("toStatus", to), ("__RequestVerificationToken", Token(page))));
            Assert(response.StatusCode == HttpStatusCode.Redirect && await Status(id) == to, $"S3-05 charged: the kitchen moves line {id} to {to}");
        }

        await Advance(line[1], "Preparing");
        await Advance(line[2], "Preparing");
        await Advance(line[2], "Ready");
        await Advance(line[3], "Preparing");
        await Advance(line[4], "Preparing");
        await Advance(line[4], "Ready");
        await DatabaseTool.Execute(connection, $"EXEC dbo.usp_TransitionOrderItem @OrderItemId={line[4]},@ToStatus='Served',@ActorUserId={waiterId};");

        // 1. Giao diện: Quản lý có "Hủy có tính tiền" ở món đang nấu / đã xong; món đã mang ra bị vô hiệu kèm lý do.
        var managerBill = await Html(manager, billUrl);
        foreach (var n in new[] { 1, 2, 3 })
        {
            var row = BillRow(managerBill, line[n]);
            Assert(row.Contains($"data-cancel-item=\"{line[n]}\"") && row.Contains("data-cancel-charged=\"true\"") && row.Contains("Hủy có tính tiền"),
                $"S3-05 charged: the manager has the charged cancel action on line {n} ({await Status(line[n])})");
        }
        var served = BillRow(managerBill, line[4]);
        Assert(!served.Contains("data-cancel-item=") && served.Contains($"data-cancel-locked=\"{line[4]}\"") && served.Contains("Món đã mang ra cho khách"),
            "S3-05 charged: a served line cannot be cancelled and the manager sees why");
        Assert(managerBill.Contains("id=\"cancel-confirm-charged\"") && managerBill.Contains("name=\"confirmCharged\"")
               && Regex.IsMatch(managerBill, "data-reason-charged=\"false\"><input type=\"radio\" name=\"reason\" value=\"ManagerOverride\"")
               && Regex.Matches(managerBill, "data-reason-charged=\"true\"><input type=\"radio\" name=\"reason\"").Count == 3
               && !Regex.IsMatch(managerBill, "name=\"reason\"[^>]*checked") && !Regex.IsMatch(managerBill, "id=\"cancel-confirm-charged\"[^>]*checked"),
            "S3-05 charged: the dialog offers exactly three reasons for a charged cancellation and an unticked charged confirmation");
        var waiterBill = await Html(waiter, billUrl);
        Assert(new[] { 1, 2, 3, 4 }.All(n => !BillRow(waiterBill, line[n]).Contains("data-cancel-item=")) && !waiterBill.Contains("id=\"cancel-confirm-charged\""),
            "S3-05 charged: the waiter has no charged cancel action");
        var cashierBill = await Html(cashier, billUrl);
        Assert(!cashierBill.Contains("data-cancel-item=") && !cashierBill.Contains("cancel-item-dialog"), "S3-05 charged: the cashier only reads the bill");

        // 2. Yêu cầu không hợp lệ: thiếu lý do, "Quản lý yêu cầu", thiếu xác nhận (cả khi không gửi trạng thái); món không đổi.
        foreach (var (reason, expected, confirm, number, label) in new (string?, string?, bool, int, string)[]
                 {
                     (null, "Preparing", true, 51032, "without a reason"),
                     ("ManagerOverride", "Preparing", true, 51142, "with the reason \"Quản lý yêu cầu\""),
                     ("ChangedMind", "Preparing", false, 51144, "without ticking the charged confirmation"),
                     ("ChangedMind", null, false, 51144, "without the confirmation, sent directly without a status")
                 })
        {
            var before = await Scalar(connection, $"SELECT COUNT(*) FROM dbo.OrderCancelRejections WHERE OrderItemId={line[3]} AND ErrorNumber={number}");
            var page = await Cancel(manager, line[3], reason, expected, confirm);
            Assert(page.Contains("alert alert-danger") && await Status(line[3]) == "Preparing" && await Events(line[3]) == 0,
                $"S3-05 charged: the manager's request {label} is refused and changes nothing");
            Assert(await Scalar(connection, $"SELECT COUNT(*) FROM dbo.OrderCancelRejections WHERE OrderItemId={line[3]} AND ErrorNumber={number} AND ActorUserId={managerId} AND ShiftId={shiftId}") == before + 1,
                $"S3-05 charged: the refused request {label} is logged for the end-of-shift report ({number})");
        }

        // 3. Nhân viên khác làm cùng thao tác: bị từ chối.
        Assert((await Cancel(waiter, line[3], "ChangedMind", "Preparing", true)).Contains("Phục vụ không huỷ được"),
            "S3-05 charged: the waiter cannot make a charged cancellation even with the confirmation sent directly");
        foreach (var (client, who) in new[] { (kitchen, "the kitchen"), (cashier, "the cashier"), (guest, "a visitor") })
            using (var blocked = await Post(client, line[3], "ChangedMind", "Preparing", true, withToken: false))
                Assert(Blocked(blocked), $"S3-05 charged: {who} cannot cancel a line");
        await Refused(connection, $"EXEC dbo.usp_CancelOrderItem @OrderItemId={line[3]},@Reason='ChangedMind',@ActorUserId={waiterId},@ConfirmCharged=1;", 51035,
            "S3-05 charged SQL: the waiter cannot make a charged cancellation through the procedure");
        await Refused(connection, $"EXEC dbo.usp_CancelOrderItem @OrderItemId={line[3]},@Reason='ChangedMind',@ActorUserId={cashierId},@ConfirmCharged=1;", 51001,
            "S3-05 charged SQL: the cashier has no right to cancel");
        await Refused(connection, $"EXEC dbo.usp_CancelOrderItem @OrderItemId={line[3]},@Reason='ManagerOverride',@ActorUserId={managerId},@ConfirmCharged=1;", 51142,
            "S3-05 charged SQL: a charged cancellation needs one of the three reasons");
        await Refused(connection, $"EXEC dbo.usp_CancelOrderItem @OrderItemId={line[3]},@Reason='ChangedMind',@ActorUserId={managerId};", 51144,
            "S3-05 charged SQL: a charged cancellation needs the explicit confirmation");
        Assert(await Status(line[3]) == "Preparing" && await Events(line[3]) == 0, "S3-05 charged: line 3 is still being cooked");

        // Món đã mang ra: Quản lý gửi thẳng cũng bị từ chối.
        Assert((await Cancel(manager, line[4], "ChangedMind", "Served", true)).Contains("Món đã mang ra cho khách nên không huỷ được nữa"),
            "S3-05 charged: the manager cannot cancel a served line");
        Assert(await Status(line[4]) == "Served" && await Events(line[4]) == 0, "S3-05 charged: the served line is unchanged");

        // 4. Quản lý huỷ món đang nấu (dòng 1): tạm tính không giảm, dòng giữ trên hoá đơn với nhãn đúng, bếp dừng món trong 5 giây.
        var subtotalBefore = await Subtotal();
        var page1 = await Cancel(manager, line[1], "ChangedMind", "Preparing", true);
        var watch = Stopwatch.StartNew();
        Assert(page1.Contains("alert alert-success") && page1.Contains("Đã huỷ có tính tiền") && page1.Contains("vẫn tính tiền"),
            "S3-05 charged: the manager cancels the line being cooked and is told it stays charged");
        var row1 = BillRow(page1, line[1]);
        var lineTotal1 = await Scalar(connection, $"SELECT LineTotal FROM dbo.OrderItems WHERE Id={line[1]}");
        Assert(await Subtotal() == subtotalBefore && PageSubtotal(page1) == subtotalBefore,
            "S3-05 charged: the subtotal does not drop (database and bill agree)");
        Assert(row1.Contains("data-status=\"Cancelled\"") && row1.Contains($"data-charged-cancelled=\"{line[1]}\"") && row1.Contains("Đã huỷ có tính tiền")
               && Regex.IsMatch(row1, "provisional-bill-amount\">[^<]*\\d") && !row1.Contains("data-cancel-item="),
            "S3-05 charged: the line stays on the bill with its amount and the label \"Đã huỷ có tính tiền\"");
        await Check(connection, $"""
            SELECT CASE WHEN i.Status='Cancelled' AND i.ChargeWhenCancelled=1 AND i.CancelReason='ChangedMind' AND i.CancelledBy={managerId}
              AND i.CancelledAt IS NOT NULL
              AND (SELECT COUNT(*) FROM dbo.OrderItemEvents e WHERE e.OrderItemId=i.Id AND e.ToStatus='Cancelled' AND e.FromStatus='Preparing'
                   AND e.ActorUserId={managerId} AND e.Reason='ChangedMind' AND e.OccurredAt IS NOT NULL)=1
             THEN 1 ELSE 0 END FROM dbo.OrderItems i WHERE i.Id={line[1]}
            """, "S3-05 charged: the cancellation is logged once with the line, the manager, the time, the reason and the cooking status");
        var shown = false;
        string kitchenPage = "";
        while (!shown && watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            kitchenPage = await Html(kitchen, "/Kitchen");
            shown = kitchenPage.Contains($"data-stop-item=\"{line[1]}\"") && !kitchenPage.Contains($"data-order-item=\"{line[1]}\"");
            if (!shown) await Task.Delay(250);
        }
        Assert(shown && watch.Elapsed < TimeSpan.FromSeconds(5),
            $"S3-05 charged: within 5 seconds the line leaves the kitchen queue and a stop notice appears ({watch.ElapsedMilliseconds} ms)");
        var stop1 = Regex.Match(kitchenPage, $"<li class=\"kitchen-stop\" data-stop-item=\"{line[1]}\"[\\s\\S]*?</li>").Value;
        Assert(kitchenPage.Contains("id=\"kitchen-stop-notices\"") && stop1.Contains("data-stop-from=\"Preparing\"") && stop1.Contains("Dừng chế biến món này.")
               && stop1.Contains("Khách đổi ý") && stop1.Contains(managerName) && stop1.Contains("Đã dừng món"),
            "S3-05 charged: the stop notice tells the kitchen what to stop, why and who cancelled");

        // Xác nhận lặp (Quản lý bấm lại, Phục vụ từ trang cũ): không ghi thêm, tạm tính không đổi.
        Assert((await Cancel(manager, line[1], "ChangedMind", "Preparing", true)).Contains("đã được hủy trước đó")
               && (await Cancel(waiter, line[1], "Mistake", "Pending", false)).Contains("đã được hủy trước đó"),
            "S3-05 charged: repeated confirmations are told the line was already cancelled");
        Assert(await Events(line[1]) == 1 && await Subtotal() == subtotalBefore
               && await Scalar(connection, $"SELECT COUNT(*) FROM dbo.OrderCancelRejections WHERE OrderItemId={line[1]}") == 0,
            "S3-05 charged: repeated confirmations add nothing to the log and do not change the subtotal");

        // Bếp bấm "Đã xong" trên màn hình cũ: bị từ chối với lý do rõ ràng.
        using (var stale = await kitchen.PostAsync($"/Kitchen/Advance/{line[1]}", Form(("toStatus", "Ready"), ("__RequestVerificationToken", Token(kitchenPage)))))
            Assert(stale.StatusCode == HttpStatusCode.Redirect, "S3-05 charged: the kitchen's late click is answered");
        Assert((await Html(kitchen, "/Kitchen")).Contains("vừa bị hủy nên không cần chế biến nữa") && await Status(line[1]) == "Cancelled",
            "S3-05 charged: a cancelled line cannot be moved on by the kitchen");

        // 5. Món đã xong chờ mang ra (dòng 2): huỷ có tính tiền, bếp được báo không mang ra.
        Assert((await Html(waiter, "/Serving")).Contains($"data-serving-item=\"{line[2]}\""), "S3-05 charged: line 2 waits to be served");
        Assert((await Cancel(manager, line[2], "SoldOut", "Ready", true)).Contains("alert alert-success"),
            "S3-05 charged: the manager cancels a line that is ready but not served");
        var kitchenAfter2 = await Html(kitchen, "/Kitchen");
        Assert(Regex.Match(kitchenAfter2, $"<li class=\"kitchen-stop\" data-stop-item=\"{line[2]}\"[\\s\\S]*?</li>").Value is var stop2
               && stop2.Contains("data-stop-from=\"Ready\"") && stop2.Contains("Không mang món này ra bàn."),
            "S3-05 charged: the stop notice for a ready line says not to bring it out");
        Assert(!(await Html(waiter, "/Serving")).Contains($"data-serving-item=\"{line[2]}\""),
            "S3-05 charged: the cancelled ready line leaves the waiter's serving queue");

        // 6. Bếp đổi trạng thái trong lúc Quản lý chọn lý do (dòng 3: mở hộp lúc Đang nấu, bếp bấm Đã xong).
        await Advance(line[3], "Ready");
        var raced = await Cancel(manager, line[3], "Mistake", "Preparing", true);
        Assert(raced.Contains("alert alert-danger") && raced.Contains("Bếp đã làm xong món này trong lúc bạn chọn lý do")
               && BillRow(raced, line[3]).Contains("data-status=\"Ready\"") && await Status(line[3]) == "Ready" && await Events(line[3]) == 0,
            "S3-05 charged: a status change before confirming is refused and the bill shows the new status");
        Assert((await Cancel(manager, line[3], "Mistake", "Ready", true)).Contains("alert alert-success") && await Status(line[3]) == "Cancelled",
            "S3-05 charged: after confirming again with the new status the cancellation goes through");

        // 7. Bếp xác nhận đã dừng món: một lần, bấm lặp không ghi thêm; chỉ tài khoản Bếp.
        foreach (var (client, who) in new[] { (manager, "the manager"), (waiter, "the waiter"), (cashier, "the cashier") })
            using (var blocked = await client.PostAsync($"/Kitchen/StopAck/{line[1]}", Form()))
                Assert(Blocked(blocked), $"S3-05 charged: {who} cannot acknowledge a kitchen stop");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var current = await Html(kitchen, "/Kitchen");
            using var ack = await kitchen.PostAsync($"/Kitchen/StopAck/{line[1]}", Form(("__RequestVerificationToken", Token(current))));
            Assert(ack.StatusCode == HttpStatusCode.Redirect, "S3-05 charged: the kitchen acknowledges the stop");
        }
        var kitchenAcked = await Html(kitchen, "/Kitchen");
        Assert(!kitchenAcked.Contains($"data-stop-item=\"{line[1]}\"") && kitchenAcked.Contains($"data-stop-item=\"{line[2]}\"")
               && kitchenAcked.Contains("đã được xác nhận dừng trước đó"),
            "S3-05 charged: the acknowledged notice disappears, the others stay, and the repeat is answered");
        await Check(connection, $"SELECT CASE WHEN COUNT(*)=1 AND MAX(AcknowledgedBy)={kitchenId} THEN 1 ELSE 0 END FROM dbo.KitchenStopAcknowledgements WHERE OrderItemId={line[1]}",
            "S3-05 charged: the kitchen's acknowledgement is recorded once");
        await Refused(connection, $"EXEC dbo.usp_AcknowledgeKitchenStop @OrderItemId={line[2]},@ActorUserId={managerId};", 51001,
            "S3-05 charged SQL: only the kitchen acknowledges a stop");
        await Refused(connection, $"EXEC dbo.usp_AcknowledgeKitchenStop @OrderItemId={line[4]},@ActorUserId={kitchenId};", 51145,
            "S3-05 charged SQL: there is nothing to acknowledge for a line that was not cancelled");

        // 8. Báo cáo cuối ca phân biệt huỷ có / không tính tiền.
        using (var response = await manager.GetAsync($"/api/reports/shift-cancellations?shiftId={shiftId}"))
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var entries = json.RootElement.GetProperty("entries").EnumerateArray()
                .Where(e => e.GetProperty("kind").GetString() == "Cancelled" && e.GetProperty("orderItemId").ValueKind == JsonValueKind.Number)
                .ToDictionary(e => e.GetProperty("orderItemId").GetInt64());
            Assert(response.StatusCode == HttpStatusCode.OK && new[] { 1, 2, 3 }.All(n => entries.ContainsKey(line[n])
                   && entries[line[n]].GetProperty("chargeKind").GetString() == "charged"
                   && entries[line[n]].GetProperty("kindLabel").GetString() == "Đã huỷ có tính tiền"
                   && entries[line[n]].GetProperty("actorName").GetString() == managerName
                   && entries[line[n]].GetProperty("chargeLabel").GetString()!.StartsWith("Vẫn tính tiền")),
                "S3-05 charged: the end-of-shift report lists the three charged cancellations with the manager and the charge");
            Assert(entries[line[1]].GetProperty("itemStatus").GetString() == "Preparing" && entries[line[2]].GetProperty("itemStatus").GetString() == "Ready"
                   && entries[line[2]].GetProperty("reason").GetString() == "SoldOut",
                "S3-05 charged: the report keeps the cooking status and reason of each cancellation");
        }
        var report = await Html(manager, $"/Reports/Shift/Cancellations?shiftId={shiftId}");
        Assert(Regex.Match(report, $"<tr data-kind=\"Cancelled\" data-order-item-id=\"{line[1]}\" data-charge=\"charged\"[\\s\\S]*?</tr>").Value is var reportRow
               && reportRow.Contains("Đã huỷ có tính tiền") && reportRow.Contains("Vẫn tính tiền") && reportRow.Contains("Khách đổi ý"),
            "S3-05 charged: the report page shows the charged cancellation apart from free ones");

        // 9. Thanh toán: hoá đơn vẫn tính các dòng huỷ có tính tiền.
        var expectedTotal = await Subtotal();
        Assert(expectedTotal == await Scalar(connection, $"SELECT SUM(LineTotal) FROM dbo.OrderItems WHERE Id IN ({line[1]},{line[2]},{line[3]},{line[4]})"),
            "S3-05 charged: the subtotal still includes every line of the table");
        await DatabaseTool.Execute(connection, $"EXEC dbo.usp_Checkout @SessionId={sessionId},@RequestId='{Guid.NewGuid()}',@Method='Cash',@ActorUserId={cashierId},@CashReceived={expectedTotal};");
        await Check(connection, $"""
            SELECT CASE WHEN v.Subtotal={expectedTotal}
              AND (SELECT COUNT(*) FROM dbo.InvoiceLines l WHERE l.InvoiceId=v.Id AND l.IsChargedCancellation=1 AND l.OrderItemId IN ({line[1]},{line[2]},{line[3]}))=3
              AND (SELECT COUNT(*) FROM dbo.InvoiceLines l WHERE l.InvoiceId=v.Id)=4 THEN 1 ELSE 0 END
            FROM dbo.Invoices v WHERE v.SessionId={sessionId}
            """, "S3-05 charged: the paid invoice keeps the charged cancelled lines and their amount");
        Console.WriteLine($"PASS: S3-05 Task 3 charged cancellation checks (line total {lineTotal1}).");
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
