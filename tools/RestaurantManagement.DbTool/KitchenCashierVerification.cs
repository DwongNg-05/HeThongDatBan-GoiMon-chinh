using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using static RestaurantManagement.DbTool.BookingConfirmationVerification;

namespace RestaurantManagement.DbTool;

/// <summary>
/// S1-04 Task 2 (web thật, SQL Server thật): Bếp và Thu ngân làm đúng phần việc của mình qua giao diện,
/// và bị chặn ở máy chủ khi làm việc của vai trò khác.
/// Luồng: Thu ngân mở ca → (Phục vụ đón khách, gọi món) → Bếp nấu xong món → Phục vụ mang ra ở Món chờ mang ra (cảnh báo quá 5 phút) → Thu ngân thanh toán,
/// xem hoá đơn → (S2-08 Task 1: Bếp/Quản lý bật/tắt "Tạm hết" ở Món trong ngày; Quản lý báo hết ở Quản lý món; vai trò khác bị chặn) → Thu ngân chốt ca. Chạy trên database mới (lệnh verify-api-permissions).
/// </summary>
internal static class KitchenCashierVerification
{
    internal static async Task Run(string connection)
    {
        var password = ApiAuthorizationVerification.Password;
        await using var web = await Web.Start(connection, new() { ["Email__RetryPollSeconds"] = "0" });
        var cashier = await SignIn(web, "cashier", password);
        var kitchen = await SignIn(web, "kitchen", password);
        var waiter = await SignIn(web, "waiter", password);
        var waiterId = await Scalar(connection, "SELECT Id FROM dbo.Users WHERE UserName=N'waiter'");

        // 1. Thu ngân mở ca.
        var shiftPage = await Html(cashier, "/Cashier/Shift");
        Assert(shiftPage.Contains("data-shift-status=\"None\""), "Cashier: shift screen offers to open a shift");
        using (var open = await cashier.PostAsync("/Cashier/OpenShift", Form(("name", "Ca kiểm thử S1-04"), ("openingCash", "100000"), ("__RequestVerificationToken", Token(shiftPage)))))
            Assert(open.StatusCode == HttpStatusCode.Redirect, "Cashier opens a shift");
        var shiftId = await Scalar(connection, "SELECT Id FROM dbo.Shifts WHERE Status='Open' AND OpenedBy=(SELECT Id FROM dbo.Users WHERE UserName=N'cashier')");
        Assert(shiftId > 0, "Shift is open and recorded under the cashier's account");

        // 2. Phục vụ đón khách và gọi món (thủ tục SQL như màn hình phục vụ).
        var dishId = await Scalar(connection, "SELECT TOP(1) m.Id FROM dbo.MenuItems m JOIN dbo.MenuCategories c ON c.Id=m.CategoryId WHERE m.IsActive=1 AND c.IsActive=1 AND m.IsSoldOut=0 AND m.IsTemporarilyOut=0 ORDER BY m.Id");
        // $$""": dấu ngoặc nhọn đơn là chữ thường (JSON), {{...}} là giá trị chèn vào.
        await DatabaseTool.Execute(connection, $$"""
            DECLARE @table int=(SELECT TOP(1) t.Id FROM dbo.DiningTables t JOIN dbo.Areas a ON a.Id=t.AreaId
              WHERE t.IsActive=1 AND a.IsActive=1 AND t.Status='Available' AND t.MaxCapacity>=2
               AND NOT EXISTS(SELECT 1 FROM dbo.SessionTables st WHERE st.TableId=t.Id AND st.ReleasedAt IS NULL) ORDER BY t.Id);
            EXEC dbo.usp_OpenSession @TableId=@table,@GuestCount=2,@ActorUserId={{waiterId}};
            DECLARE @session bigint=(SELECT MAX(Id) FROM dbo.DiningSessions);
            DECLARE @request uniqueidentifier=NEWID();
            DECLARE @items nvarchar(max)=CONCAT(N'[{"MenuItemId":',{{dishId}},N',"Quantity":2,"Notes":"S104 ít cay"}]');
            EXEC dbo.usp_SubmitOrder @SessionId=@session,@RequestId=@request,@ItemsJson=@items,@ActorUserId={{waiterId}};
            """);
        var sessionId = await Scalar(connection, "SELECT MAX(Id) FROM dbo.DiningSessions");
        var itemId = await Scalar(connection, $"SELECT i.Id FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId WHERE b.SessionId={sessionId}");

        // 3. Bếp: thấy món trên màn hình bếp, chuyển Đang nấu rồi Xong.
        var kitchenPage = await Html(kitchen, "/Kitchen");
        Assert(kitchenPage.Contains($"data-order-item=\"{itemId}\" data-status=\"Pending\"") && kitchenPage.Contains("S104 ít cay") && kitchenPage.Contains("Bắt đầu nấu"),
            "Kitchen: the new dish appears on the kitchen screen");
        foreach (var next in new[] { "Preparing", "Ready" })
        {
            using var advance = await kitchen.PostAsync($"/Kitchen/Advance/{itemId}", Form(("toStatus", next), ("__RequestVerificationToken", Token(kitchenPage))));
            Assert(advance.StatusCode == HttpStatusCode.Redirect && !Blocked(advance), $"Kitchen moves the dish to {next}");
            await Check(connection, $"SELECT CASE WHEN Status='{next}' THEN 1 ELSE 0 END FROM dbo.OrderItems WHERE Id={itemId}", $"Kitchen: dish status is {next}");
            kitchenPage = await Html(kitchen, "/Kitchen");
        }

        // Ngoài phạm vi: Phục vụ (S1-04 Task 1: không còn xem màn hình bếp) và Thu ngân không chuyển được trạng thái chế biến.
        using (var waiterKitchenScreen = await waiter.GetAsync("/Kitchen"))
            Assert(Blocked(waiterKitchenScreen), "Waiter cannot open the kitchen screen (blocked by the server)");
        var waiterPage = await Html(waiter, "/Ordering");
        foreach (var (who, client, page) in new[] { ("Waiter", waiter, waiterPage), ("Cashier", cashier, await Html(cashier, "/Cashier")) })
        {
            using var denied = await client.PostAsync($"/Kitchen/Advance/{itemId}", Form(("toStatus", "Ready"), ("__RequestVerificationToken", Token(page))));
            Assert(Blocked(denied), $"{who} cannot change the cooking status (blocked by the server)");
        }

        // 4. Món chờ mang ra (/Serving): Phục vụ thấy món vừa xong; chờ dưới 5 phút không cảnh báo, quá 5 phút được đánh dấu nổi bật;
        //    bấm "Đã mang ra" thì món rời danh sách chờ và không còn bị cảnh báo. Bếp, Thu ngân bị chặn.
        static bool Row(string html, long id, bool overdue) =>
            Regex.IsMatch(html, $"data-serving-item=\"{id}\"[^>]*data-overdue=\"{(overdue ? "true" : "false")}\"");
        async Task<JsonElement?> QueueItem(long id)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/Serving/Queue");
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            using var response = await waiter.SendAsync(request);
            Assert(response.StatusCode == HttpStatusCode.OK, "Serving queue: the refresh API answers the waiter");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert(json.RootElement.GetProperty("overdueAfterMs").GetInt64() == 300_000, "Serving queue: the refresh API reports the 5-minute threshold");
            foreach (var entry in json.RootElement.GetProperty("items").EnumerateArray())
                if (entry.GetProperty("id").GetInt64() == id) return entry.Clone();
            return null;
        }
        // S3-08 Task 1: món vừa xong có ngay trong danh sách chờ (API trang tự gọi lại), đủ mã bàn và tên món;
        // trang gọi lại mỗi data-refresh-ms nên món + thông báo hiện trong ≤ 5 giây kể từ lúc ghi nhận hoàn thành.
        var tableCode = await Text(connection, $"SELECT t.Code FROM dbo.OrderItems i JOIN dbo.DiningTables t ON t.Id=i.OriginalTableId WHERE i.Id={itemId}");
        var itemName = await Text(connection, $"SELECT ItemName FROM dbo.OrderItems WHERE Id={itemId}");
        var justReady = await QueueItem(itemId);
        var servingPage = await Html(waiter, "/Serving");
        var refreshMs = int.Parse(Regex.Match(servingPage, "data-refresh-ms=\"(\\d+)\"").Groups[1].Value);
        Assert(justReady is { } r0 && r0.GetProperty("tableCode").GetString() == tableCode && r0.GetProperty("itemName").GetString() == itemName
            && r0.GetProperty("waitMs").GetInt64() + refreshMs <= 5000,
            $"S3-08: the finished dish reaches the waiter's list with table {tableCode} and \"{itemName}\" within 5 seconds (refresh every {refreshMs} ms)");
        Assert(Row(servingPage, itemId, overdue: false) && servingPage.Contains("Đã mang ra") && servingPage.Contains(tableCode) && servingPage.Contains(itemName),
            "Serving queue: the waiter sees the finished dish, not highlighted right after it is ready");
        await DatabaseTool.Execute(connection, $"UPDATE dbo.OrderItems SET ReadyAt=DATEADD(second,-290,SYSUTCDATETIME()) WHERE Id={itemId};");
        var waiting = await QueueItem(itemId);
        Assert(waiting is { } w1 && !w1.GetProperty("overdue").GetBoolean() && w1.GetProperty("waitMs").GetInt64() is >= 290_000 and < 300_000,
            "Serving queue: a dish waiting 4:50 is counted from its completion time and not highlighted");
        await DatabaseTool.Execute(connection, $"UPDATE dbo.OrderItems SET ReadyAt=DATEADD(second,-301,SYSUTCDATETIME()) WHERE Id={itemId};");
        var late = await QueueItem(itemId);
        Assert(late is { } w2 && w2.GetProperty("overdue").GetBoolean() && w2.GetProperty("waitMs").GetInt64() >= 301_000,
            "Serving queue: a dish waiting over 5 minutes is flagged by the refresh API");
        servingPage = await Html(waiter, "/Serving");
        Assert(Row(servingPage, itemId, overdue: true) && servingPage.Contains("table-danger serving-overdue") && servingPage.Contains("Chờ quá 5 phút"),
            "Serving queue: the overdue dish is highlighted on the waiting list");
        foreach (var (who, client) in new[] { ("Kitchen", kitchen), ("Cashier", cashier) })
        {
            using var screen = await client.GetAsync("/Serving");
            using var serve = await client.PostAsync($"/Serving/Serve/{itemId}", Form(("__RequestVerificationToken", Token(servingPage))));
            Assert(Blocked(screen) && Blocked(serve), $"{who} cannot open the serving queue or mark dishes as served (blocked by the server)");
        }
        // S3-08 Task 1: Phục vụ và Quản lý bấm "Đã mang ra" cùng lúc → đúng một người thành công (200),
        // người còn lại nhận 409 kèm tên người đã mang ra; món chỉ ghi nhận phục vụ một lần.
        var servingManager = await SignIn(web, "manager", password);
        var managerServingPage = await Html(servingManager, "/Serving");
        async Task<(string Who, HttpStatusCode Status, JsonElement Body)> Confirm(string who, HttpClient client, string page)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/Serving/Serve/{itemId}")
                { Content = Form(("__RequestVerificationToken", Token(page))) };
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            using var response = await client.SendAsync(request);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return (who, response.StatusCode, json.RootElement.Clone());
        }
        var both = await Task.WhenAll(Confirm("waiter", waiter, servingPage), Confirm("manager", servingManager, managerServingPage));
        var winners = both.Where(b => b.Status == HttpStatusCode.OK).ToArray();
        var losers = both.Where(b => b.Status == HttpStatusCode.Conflict).ToArray();
        Assert(winners.Length == 1 && losers.Length == 1 && winners[0].Body.GetProperty("served").GetBoolean()
            && winners[0].Body.GetProperty("message").GetString() == $"Đã mang ra: Bàn {tableCode} · {itemName}.",
            $"S3-08: two staff confirm at the same time — only {winners.FirstOrDefault().Who ?? "one"} succeeds");
        var winnerId = await Scalar(connection, $"SELECT Id FROM dbo.Users WHERE UserName=N'{winners[0].Who}'");
        var winnerName = await Text(connection, $"SELECT FullName FROM dbo.Users WHERE Id={winnerId}");
        Assert(losers[0].Body.GetProperty("alreadyServed").GetBoolean() && !losers[0].Body.GetProperty("served").GetBoolean()
            && losers[0].Body.GetProperty("message").GetString()!.StartsWith($"Bàn {tableCode} · {itemName} đã được {winnerName} mang ra lúc ", StringComparison.Ordinal),
            $"S3-08: the other staff member ({losers[0].Who}) is told the dish was already served by {winnerName}");
        var repeat = await Confirm(winners[0].Who, winners[0].Who == "waiter" ? waiter : servingManager, winners[0].Who == "waiter" ? servingPage : managerServingPage);
        Assert(repeat.Status == HttpStatusCode.Conflict && repeat.Body.GetProperty("message").GetString()!.StartsWith("Bạn đã xác nhận mang ra", StringComparison.Ordinal),
            "S3-08: confirming the same dish again tells the staff member they already did");
        servingManager.Dispose();
        await Check(connection, $"SELECT CASE WHEN Status='Served' AND ServedAt IS NOT NULL AND ServedBy={winnerId} THEN 1 ELSE 0 END FROM dbo.OrderItems WHERE Id={itemId}",
            "S3-08: the dish moves to Served, recorded under the staff member who succeeded");
        await Check(connection, $"SELECT CASE WHEN COUNT(*)=1 THEN 1 ELSE 0 END FROM dbo.OrderItemEvents WHERE OrderItemId={itemId} AND ToStatus='Served'",
            "S3-08: concurrent and repeated confirmations record exactly one served event");
        Assert(await QueueItem(itemId) is null && !(await Html(waiter, "/Serving")).Contains($"data-serving-item=\"{itemId}\""),
            "Serving queue: a served dish leaves the waiting list and is no longer highlighted");

        // 5. Thu ngân thanh toán và xem hoá đơn; Bếp không thanh toán được.
        var payPage = await Html(cashier, "/Cashier");
        Assert(payPage.Contains($"data-session-id=\"{sessionId}\"") && payPage.Contains(">Thanh toán</button>"), "Cashier: the served table is ready to pay");
        using (var kitchenPay = await kitchen.PostAsync("/Cashier/Checkout", Form(("sessionId", sessionId.ToString()), ("requestId", Guid.NewGuid().ToString()),
                   ("method", "Cash"), ("cashReceived", "1000000"), ("__RequestVerificationToken", Token(kitchenPage)))))
            Assert(Blocked(kitchenPay), "Kitchen cannot take payments (blocked by the server)");
        using (var pay = await cashier.PostAsync("/Cashier/Checkout", Form(("sessionId", sessionId.ToString()), ("requestId", Guid.NewGuid().ToString()),
                   ("method", "Cash"), ("cashReceived", "1000000"), ("__RequestVerificationToken", Token(payPage)))))
            Assert(pay.StatusCode == HttpStatusCode.Redirect && !Blocked(pay), "Cashier takes the payment");
        await Check(connection, $"""
            SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.Invoices i JOIN dbo.Payments p ON p.InvoiceId=i.Id
              WHERE i.SessionId={sessionId} AND i.Status='Paid' AND i.IssuedBy=(SELECT Id FROM dbo.Users WHERE UserName=N'cashier') AND p.Method='Cash')
             AND (SELECT Status FROM dbo.DiningSessions WHERE Id={sessionId})='Closed' THEN 1 ELSE 0 END
            """, "Cashier: invoice issued under the cashier's account and the table is closed");
        var invoiceNumber = await InvoiceNumber(connection, sessionId);
        Assert((await Html(cashier, "/Cashier/Invoices")).Contains($"data-invoice=\"{invoiceNumber}\""), "Cashier: the invoice is listed on the invoice screen");

        // 6. S2-08 Task 1 — "Tạm hết" trên danh sách món trong ngày (/Kitchen/Dishes). Các bài kiểm thử theo đúng tiêu chí của task:
        //    T1 bật tạm hết, T2 tắt tạm hết, T3 Bếp thao tác được, T4 Quản lý thao tác được,
        //    T5 món tạm hết không nhận order mới, T6 nhãn "Tạm hết" ở thực đơn công khai và màn hình gọi món trong tối đa 5 giây.
        // Khớp TemporaryOutRules (web): trang tự cập nhật mỗi 3 giây, nhãn phải hiện trong tối đa 5 giây.
        const int PollIntervalMs = 3000, MaxDisplayDelayMs = 5000;
        var manager = await SignIn(web, "manager", password);
        using var guest = new HttpClient { BaseAddress = web.Client.BaseAddress };

        // S2-08 Task 2: nhật ký của món thử trước khi bắt đầu (database mới nên thường là 0).
        var logStart = await Scalar(connection, $"SELECT COALESCE(MAX(Id),0) FROM dbo.MenuTemporaryOutEvents WHERE MenuItemId={dishId}");
        const string DbNowMs = "SELECT DATEDIFF_BIG(millisecond,'2000-01-01',SYSUTCDATETIME())";
        async Task<long> LogCount() => await Scalar(connection, $"SELECT COUNT(*) FROM dbo.MenuTemporaryOutEvents WHERE MenuItemId={dishId}");

        // Một chạm bật/tắt (giống nút trên trang: fetch + X-Requested-With). Trả về thời điểm máy chủ xác nhận.
        // S2-08 Task 2: mỗi lần bấm tạo đúng một dòng nhật ký với đúng người thao tác, trạng thái trước/sau và thời điểm.
        async Task<System.Diagnostics.Stopwatch> Toggle(HttpClient client, string userName, string who, bool on)
        {
            var actorId = await Scalar(connection, $"SELECT Id FROM dbo.Users WHERE UserName=N'{userName}'");
            var countBefore = await LogCount();
            var startedMs = await Scalar(connection, DbNowMs);
            var page = await Html(client, "/Kitchen/Dishes");
            Assert(page.Contains($"data-availability-id=\"{dishId}\"") && page.Contains(on ? ">Tạm hết</button>" : ">Bán lại</button>"),
                $"{who}: today's dish list shows the one-tap \"{(on ? "Tạm hết" : "Bán lại")}\" button");
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/Kitchen/Dishes/{dishId}/TemporarilyOut")
                { Content = Form(("isTemporarilyOut", on ? "true" : "false"), ("__RequestVerificationToken", Token(page))) };
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            using var response = await client.SendAsync(request);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var body = await response.Content.ReadAsStringAsync();
            Assert(response.StatusCode == HttpStatusCode.OK && body.Contains($"\"isTemporarilyOut\":{(on ? "true" : "false")}"),
                $"{who} turns \"Tạm hết\" {(on ? "on" : "off")} with one tap");
            await Check(connection, $"SELECT CASE WHEN IsTemporarilyOut={(on ? 1 : 0)} THEN 1 ELSE 0 END FROM dbo.MenuItems WHERE Id={dishId}",
                $"{who}: \"Tạm hết\" {(on ? "on" : "off")} saved");
            var finishedMs = await Scalar(connection, DbNowMs);
            Assert(await LogCount() == countBefore + 1, $"Log: turning \"Tạm hết\" {(on ? "on" : "off")} ({who}) creates exactly one log entry");
            await Check(connection, $"""
                SELECT CASE WHEN l.ChangedBy={actorId} AND l.OldIsTemporarilyOut={(on ? 0 : 1)} AND l.IsTemporarilyOut={(on ? 1 : 0)}
                  AND DATEDIFF_BIG(millisecond,'2000-01-01',l.ChangedAt) BETWEEN {startedMs} AND {finishedMs}
                  AND l.ChangedAt=(SELECT UpdatedAt FROM dbo.MenuItems WHERE Id={dishId}) THEN 1 ELSE 0 END
                FROM (SELECT TOP(1) * FROM dbo.MenuTemporaryOutEvents WHERE MenuItemId={dishId} ORDER BY Id DESC) l
                """, $"Log ({who}): right actor ({userName}), dish, before → after state and the moment of the change are recorded");
            return timer;
        }

        // T6: trang tự hỏi lại trạng thái mỗi PollIntervalMs; API phải phản ánh thao tác ngay, nên độ trễ tối đa = chu kỳ + thời gian gọi API ≤ 5 giây.
        async Task AssertVisibleWithin5Seconds(System.Diagnostics.Stopwatch sinceToggle, bool on, string when)
        {
            using var api = await guest.GetAsync("/api/menu/availability");
            var json = System.Text.Json.JsonDocument.Parse(await api.Content.ReadAsStringAsync()).RootElement;
            var listed = json.GetProperty("unavailable").EnumerateArray().Any(e => e.GetInt64() == dishId);
            var worstCaseMs = sinceToggle.ElapsedMilliseconds + PollIntervalMs;
            Assert(api.StatusCode == HttpStatusCode.OK && listed == on && worstCaseMs <= MaxDisplayDelayMs,
                $"T6 {when}: public menu and ordering screen see the change within 5 s (worst case {worstCaseMs} ms)");
            var menu = await Html(guest, "/Menu");
            Assert(menu.Contains($"data-availability-id=\"{dishId}\" data-sold-out=\"{(on ? "true" : "false")}\"") && menu.Contains($"data-availability-interval=\"{PollIntervalMs}\""),
                $"T6 {when}: public menu {(on ? "shows" : "no longer shows")} the \"Tạm hết\" label and refreshes itself");
            var ordering = await Html(waiter, "/Ordering");
            Assert(ordering.Contains($"data-availability-id=\"{dishId}\" data-unavailable=\"{(on ? "true" : "false")}\"") && ordering.Contains($"data-availability-interval=\"{PollIntervalMs}\""),
                $"T6 {when}: ordering screen {(on ? "shows" : "no longer shows")} the \"Tạm hết\" label and refreshes itself");
        }

        async Task<HttpStatusCode> WaiterOrders()
        {
            var page = await Html(waiter, "/Ordering");
            using var order = await waiter.PostAsync("/Ordering/Checkout", Form(("CartJson", $"[{{\"dishId\":{dishId},\"quantity\":1,\"price\":1,\"name\":\"x\"}}]"), ("__RequestVerificationToken", Token(page))));
            return order.StatusCode;
        }

        // T1 + T3: Bếp bật tạm hết.
        var sinceOn = await Toggle(kitchen, "kitchen", "T1/T3 Kitchen", on: true);
        Assert((await Html(kitchen, "/Kitchen/Dishes")).Contains($"data-availability-id=\"{dishId}\" data-unavailable=\"true\" data-temporarily-out=\"true\""),
            "T1: the dish shows \"Tạm hết\" on today's dish list right away");
        await AssertVisibleWithin5Seconds(sinceOn, on: true, "after Kitchen turns it on");
        // S2-08 Task 2: bấm lại khi món đã tạm hết (bấm đúp, hai thiết bị) không đổi trạng thái nên không ghi thêm nhật ký.
        var countAfterOn = await LogCount();
        var samePage = await Html(kitchen, "/Kitchen/Dishes");
        using (var again = await kitchen.PostAsync($"/Kitchen/Dishes/{dishId}/TemporarilyOut", Form(("isTemporarilyOut", "true"), ("__RequestVerificationToken", Token(samePage)))))
            Assert(again.StatusCode == HttpStatusCode.Redirect && await LogCount() == countAfterOn, "Log: repeating \"Tạm hết\" on an already temporarily-out dish adds no log entry");

        // T5: món tạm hết không nhận order mới (màn hình gọi món và database).
        Assert(await WaiterOrders() == HttpStatusCode.BadRequest, "T5: ordering screen refuses a new order for the temporarily-out dish");
        var orderRefused = false;
        try
        {
            // Chạy trong giao dịch rồi huỷ: không để lại phiên bàn thử (lỗi 51028 cũng huỷ giao dịch khi đóng kết nối).
            await DatabaseTool.Execute(connection, $$"""
                BEGIN TRANSACTION;
                DECLARE @table int=(SELECT TOP(1) t.Id FROM dbo.DiningTables t JOIN dbo.Areas a ON a.Id=t.AreaId
                  WHERE t.IsActive=1 AND a.IsActive=1 AND t.Status='Available' AND t.MaxCapacity>=2
                   AND NOT EXISTS(SELECT 1 FROM dbo.SessionTables st WHERE st.TableId=t.Id AND st.ReleasedAt IS NULL) ORDER BY t.Id);
                EXEC dbo.usp_OpenSession @TableId=@table,@GuestCount=2,@ActorUserId={{waiterId}};
                DECLARE @session bigint=(SELECT MAX(Id) FROM dbo.DiningSessions);
                DECLARE @request uniqueidentifier=NEWID();
                DECLARE @items nvarchar(max)=CONCAT(N'[{"MenuItemId":',{{dishId}},N',"Quantity":1}]');
                EXEC dbo.usp_SubmitOrder @SessionId=@session,@RequestId=@request,@ItemsJson=@items,@ActorUserId={{waiterId}};
                ROLLBACK;
                """);
        }
        catch (SqlException ex) when (ex.Number == 51028) { orderRefused = true; }
        Assert(orderRefused, "T5: database also refuses new orders for the temporarily-out dish (usp_SubmitOrder 51028)");

        // T2 + T3: Bếp tắt tạm hết → món nhận order lại.
        var sinceOff = await Toggle(kitchen, "kitchen", "T2/T3 Kitchen", on: false);
        await AssertVisibleWithin5Seconds(sinceOff, on: false, "after Kitchen turns it off");
        Assert(await WaiterOrders() == HttpStatusCode.Redirect, "T2: the dish accepts orders again after \"Tạm hết\" is turned off");

        // T4: Quản lý bật rồi tắt trên cùng danh sách.
        await AssertVisibleWithin5Seconds(await Toggle(manager, "manager", "T4 Manager", on: true), on: true, "after Manager turns it on");
        Assert(await WaiterOrders() == HttpStatusCode.BadRequest, "T4/T5: Manager's \"Tạm hết\" also blocks new orders");
        await AssertVisibleWithin5Seconds(await Toggle(manager, "manager", "T4 Manager", on: false), on: false, "after Manager turns it off");

        // S2-08 Task 2: nhiều lần bật/tắt → nhật ký đúng thứ tự (Bếp bật, Bếp tắt, Quản lý bật, Quản lý tắt), thời điểm không giảm.
        await Check(connection, $"""
            WITH l AS (SELECT e.Id,e.OldIsTemporarilyOut,e.IsTemporarilyOut,e.ChangedAt,u.UserName,
                              LAG(e.ChangedAt) OVER (ORDER BY e.Id) AS PrevAt
                       FROM dbo.MenuTemporaryOutEvents e JOIN dbo.Users u ON u.Id=e.ChangedBy
                       WHERE e.MenuItemId={dishId} AND e.Id>{logStart})
            SELECT CASE WHEN STRING_AGG(CONCAT(UserName,':',CONVERT(int,OldIsTemporarilyOut),'>',CONVERT(int,IsTemporarilyOut)),',') WITHIN GROUP (ORDER BY Id)
                             ='kitchen:0>1,kitchen:1>0,manager:0>1,manager:1>0'
                         AND SUM(CASE WHEN PrevAt>ChangedAt THEN 1 ELSE 0 END)=0 THEN 1 ELSE 0 END
            FROM l
            """, "Log: several on/off toggles are recorded in the right order with non-decreasing times");

        // S2-08 Task 2: Quản lý xem lịch sử (mới nhất trước) — từng lần thay đổi, người thao tác và thời điểm.
        var history = await Html(manager, $"/Kitchen/Dishes/History?dishId={dishId}");
        var shownStates = System.Text.RegularExpressions.Regex.Matches(history, $"data-dish-id=\"{dishId}\" data-new-state=\"(true|false)\"").Select(m => m.Groups[1].Value).ToArray();
        Assert(shownStates.Take(4).SequenceEqual(new[] { "false", "true", "false", "true" })
            && history.Contains("Bật tạm hết") && history.Contains("Tắt tạm hết") && history.Contains("(kitchen)") && history.Contains("(manager)")
            && history.Contains("<time datetime="),
            "History: Manager sees each change (newest first) with who did it and when");
        using (var kitchenHistory = await kitchen.GetAsync($"/Kitchen/Dishes/History?dishId={dishId}"))
            Assert(Blocked(kitchenHistory), "History: only Manager can open the temporarily-out history (Kitchen blocked)");
        var logBeforeBlocked = await LogCount();

        // Quyền đã chốt với PO: chỉ Bếp và Quản lý; Phục vụ, Thu ngân bị chặn ở máy chủ.
        foreach (var (who, client, page) in new[] { ("Waiter", waiter, await Html(waiter, "/Ordering")), ("Cashier", cashier, payPage) })
        {
            using var dishesScreen = await client.GetAsync("/Kitchen/Dishes");
            using var denied = await client.PostAsync($"/Kitchen/Dishes/{dishId}/TemporarilyOut", Form(("isTemporarilyOut", "true"), ("__RequestVerificationToken", Token(page))));
            Assert(Blocked(dishesScreen) && Blocked(denied), $"{who} cannot open today's dish list or toggle \"Tạm hết\" (blocked by the server)");
        }
        await Check(connection, $"SELECT CASE WHEN IsTemporarilyOut=0 THEN 1 ELSE 0 END FROM dbo.MenuItems WHERE Id={dishId}", "Blocked attempts change nothing");
        Assert(await LogCount() == logBeforeBlocked, "Log: blocked attempts write no log entry");
        waiterPage = await Html(waiter, "/Ordering");

        // Báo hết trong ngày (S2-01 Task 3) vẫn chỉ ở Quản lý món (/Dishes, Quản lý).
        var dishesPage = await Html(manager, "/Dishes");
        using (var soldOut = await manager.PostAsync("/Management/Availability", Form(("id", dishId.ToString()), ("soldOut", "true"), ("__RequestVerificationToken", Token(dishesPage)))))
            Assert(soldOut.StatusCode == HttpStatusCode.Redirect && !Blocked(soldOut), "Manager marks the dish sold out in dish management");
        await Check(connection, $"SELECT CASE WHEN IsSoldOut=1 AND SoldOutBusinessDate=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME())) THEN 1 ELSE 0 END FROM dbo.MenuItems WHERE Id={dishId}",
            "Manager: sold-out flag saved for today");
        foreach (var (who, client, page) in new[] { ("Kitchen", kitchen, kitchenPage), ("Cashier", cashier, payPage), ("Waiter", waiter, waiterPage) })
        {
            using var denied = await client.PostAsync("/Management/Availability", Form(("id", dishId.ToString()), ("soldOut", "false"), ("__RequestVerificationToken", Token(page))));
            Assert(Blocked(denied), $"{who} cannot change dish availability (blocked by the server)");
        }
        // Database cũng từ chối (migration 031, 032): chỉ Quản lý có quyền Menu.Availability (Bếp chỉ có Menu.TemporarilyOut, migration 038).
        var kitchenId = await Scalar(connection, "SELECT Id FROM dbo.Users WHERE UserName=N'kitchen'");
        foreach (var (who, actorId) in new[] { ("Waiter", waiterId), ("Kitchen", kitchenId) })
        {
            var refused = false;
            try { await DatabaseTool.Execute(connection, $"EXEC dbo.usp_SetMenuAvailability @MenuItemId={dishId},@IsSoldOut=0,@ActorUserId={actorId};"); }
            catch (SqlException ex) when (ex.Number == 51001) { refused = true; }
            Assert(refused, $"{who} cannot change dish availability in the database either");
        }
        await Check(connection, $"SELECT CASE WHEN IsSoldOut=1 THEN 1 ELSE 0 END FROM dbo.MenuItems WHERE Id={dishId}",
            "Dish stays sold out after the blocked attempts");
        dishesPage = await Html(manager, "/Dishes");
        using (var back = await manager.PostAsync("/Management/Availability", Form(("id", dishId.ToString()), ("soldOut", "false"), ("__RequestVerificationToken", Token(dishesPage)))))
            Assert(back.StatusCode == HttpStatusCode.Redirect, "Manager puts the dish back on sale");
        await Check(connection, $"SELECT CASE WHEN IsSoldOut=0 THEN 1 ELSE 0 END FROM dbo.MenuItems WHERE Id={dishId}", "Manager: dish is on sale again");
        manager.Dispose();

        // 7. Thu ngân chốt ca, tiền mặt khớp số phải có.
        shiftPage = await Html(cashier, "/Cashier/Shift");
        var expected = await Scalar(connection, $"""
            SELECT CONVERT(bigint,s.OpeningCash+COALESCE((SELECT SUM(p.Amount) FROM dbo.Invoices i JOIN dbo.Payments p ON p.InvoiceId=i.Id
              WHERE i.ShiftId=s.Id AND i.Status='Paid' AND p.Method='Cash'),0)) FROM dbo.Shifts s WHERE s.Id={shiftId}
            """);
        Assert(shiftPage.Contains($"data-expected-cash=\"{expected}\""), $"Cashier: the shift screen shows the expected cash {expected}");
        using (var waiterClose = await waiter.PostAsync("/Cashier/CloseShift", Form(("shiftId", shiftId.ToString()), ("countedCash", expected.ToString()), ("__RequestVerificationToken", Token(waiterPage)))))
            Assert(Blocked(waiterClose), "Waiter cannot close the shift (blocked by the server)");
        using (var close = await cashier.PostAsync("/Cashier/CloseShift", Form(("shiftId", shiftId.ToString()), ("countedCash", expected.ToString()), ("__RequestVerificationToken", Token(shiftPage)))))
            Assert(close.StatusCode == HttpStatusCode.Redirect && !Blocked(close), "Cashier closes the shift");
        await Check(connection, $"SELECT CASE WHEN Status='Closed' AND CashDifference=0 AND InvoiceCount=1 AND ClosedBy=(SELECT Id FROM dbo.Users WHERE UserName=N'cashier') THEN 1 ELSE 0 END FROM dbo.Shifts WHERE Id={shiftId}",
            "Cashier: shift closed with no cash difference");
        Console.WriteLine("PASS: S1-04 Task 2 kitchen and cashier work flow checks.");
    }

    private static async Task<HttpClient> SignIn(Web web, string user, string password)
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() };
        var client = new HttpClient(handler, disposeHandler: true) { BaseAddress = web.Client.BaseAddress, Timeout = TimeSpan.FromSeconds(30) };
        await Login(client, user, password);
        return client;
    }

    private static async Task<string> Text(string connection, string sql)
    {
        await using var cn = new SqlConnection(connection);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        return Convert.ToString(await cmd.ExecuteScalarAsync()) ?? "";
    }

    private static bool Blocked(HttpResponseMessage response)
    {
        var location = Location(response);
        return response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
            || (response.StatusCode == HttpStatusCode.Redirect && (location.Contains("/Account/AccessDenied") || location.Contains("/Account/Login")));
    }

    private static async Task<string> InvoiceNumber(string connection, long sessionId)
    {
        await using var cn = new Microsoft.Data.SqlClient.SqlConnection(connection);
        await cn.OpenAsync();
        await using var cmd = new Microsoft.Data.SqlClient.SqlCommand($"SELECT InvoiceNumber FROM dbo.Invoices WHERE SessionId={sessionId}", cn);
        return Convert.ToString(await cmd.ExecuteScalarAsync())!;
    }
}
