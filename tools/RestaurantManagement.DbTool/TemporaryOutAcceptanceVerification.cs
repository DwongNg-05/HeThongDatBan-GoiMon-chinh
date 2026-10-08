using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using static RestaurantManagement.DbTool.BookingConfirmationVerification;

namespace RestaurantManagement.DbTool;

/// <summary>
/// S2-08 Task 4 – nghiệm thu toàn bộ luồng "Tạm hết" (web thật, SQL Server thật, database tạm riêng; lệnh verify-temporary-out).
/// Kịch bản: Bếp bật tạm hết → thử gọi món → kiểm tra thực đơn → kiểm tra nhật ký → tắt tạm hết → gọi lại món
/// → nhiều lần bật/tắt liên tiếp → quyền → nhiều món tạm hết qua 00:00 (Asia/Ho_Chi_Minh) → nhận order trở lại.
/// Sau mỗi bước so trạng thái của MỌI món giữa API trạng thái, Món trong ngày, thực đơn công khai và màn hình gọi món.
/// </summary>
internal static class TemporaryOutAcceptanceVerification
{
    // Khớp TemporaryOutRules (web).
    private const int PollIntervalMs = 3000, MaxDisplayDelayMs = 5000;

    internal static async Task Run(string connection, string password)
    {
        Console.WriteLine("== S2-08 Task 4: temporarily-out acceptance scenario ==");
        Web? web = await Web.Start(connection, new() { ["Email__RetryPollSeconds"] = "0" });
        try
        {
            var (kitchen, manager, waiter, cashier) = (await SignIn(web, "kitchen", password), await SignIn(web, "manager", password),
                await SignIn(web, "waiter", password), await SignIn(web, "cashier", password));
            using var guest = new HttpClient { BaseAddress = web.Client.BaseAddress };
            var dishes = await Ids(connection, """
                SELECT TOP(3) m.Id FROM dbo.MenuItems m JOIN dbo.MenuCategories c ON c.Id=m.CategoryId
                WHERE m.IsActive=1 AND c.IsActive=1 AND m.IsSoldOut=0 AND m.IsTemporarilyOut=0 ORDER BY m.Id
                """);
            Assert(dishes.Count == 3, "Setup: three dishes on sale");
            var (a, b, c) = (dishes[0], dishes[1], dishes[2]);
            var waiterId = await Scalar(connection, "SELECT Id FROM dbo.Users WHERE UserName=N'waiter'");
            await AssertConsistent(connection, guest, kitchen, waiter, "start");

            // 1. Bếp bật "Tạm hết" món A trên danh sách món trong ngày (một chạm).
            var shownIn = await ToggleAndMeasure(kitchen, guest, a, on: true);
            Assert(shownIn <= MaxDisplayDelayMs, $"1. Kitchen turns A temporarily out with one tap; public menu and ordering screen show it within 5 s (worst case {shownIn} ms)");
            await AssertConsistent(connection, guest, kitchen, waiter, "after Kitchen turns A on");

            // 2. Thử gọi món A: màn hình gọi món và database đều từ chối.
            Assert(await Order(waiter, a) == HttpStatusCode.BadRequest, "2. Ordering screen refuses a new order for A");
            Assert(await DbOrderRefused(connection, waiterId, a), "2. Database refuses a new order for A (51028)");

            // 3. Thực đơn công khai hiển thị "Tạm hết".
            var menu = await Html(guest, "/Menu");
            Assert(DishItem(menu, a).Contains("data-sold-out=\"true\"") && DishItem(menu, a).Contains("Tạm hết"), "3. Public menu shows A as \"Tạm hết\"");

            // 4. Nhật ký: Quản lý thấy lần bật của Bếp, có người thao tác và thời điểm.
            var history = await Html(manager, $"/Kitchen/Dishes/History?dishId={a}");
            Assert(Regex.IsMatch(history, $"data-dish-id=\"{a}\" data-new-state=\"true\"") && history.Contains("Bật tạm hết") && history.Contains("(kitchen)") && history.Contains("<time datetime="),
                "4. History shows Kitchen turning A on, with who and when");

            // 5. Bếp tắt "Tạm hết" món A; 6. gọi lại món A được.
            shownIn = await ToggleAndMeasure(kitchen, guest, a, on: false);
            Assert(shownIn <= MaxDisplayDelayMs, $"5. Kitchen turns A back on sale; screens show it within 5 s (worst case {shownIn} ms)");
            await AssertConsistent(connection, guest, kitchen, waiter, "after Kitchen turns A off");
            Assert(await Order(waiter, a) == HttpStatusCode.Redirect, "6. Ordering screen accepts A again");
            Assert(!await DbOrderRefused(connection, waiterId, a), "6. Database accepts A again");

            // 7. Nhiều lần bật/tắt liên tiếp (Bếp và Quản lý xen kẽ) → nhật ký đủ, đúng thứ tự, đúng người.
            var logStart = await Scalar(connection, $"SELECT COALESCE(MAX(Id),0) FROM dbo.MenuTemporaryOutEvents WHERE MenuItemId={b}");
            var sequence = new[] { (kitchen, "kitchen", true), (manager, "manager", false), (manager, "manager", true), (kitchen, "kitchen", false), (kitchen, "kitchen", true), (manager, "manager", false) };
            foreach (var (client, _, on) in sequence) await Toggle(client, b, on);
            var expected = string.Join(",", sequence.Select(s => $"{s.Item2}:{(s.Item3 ? "0>1" : "1>0")}"));
            await Check(connection, $"""
                WITH l AS (SELECT e.Id,e.OldIsTemporarilyOut,e.IsTemporarilyOut,e.ChangedAt,u.UserName,LAG(e.ChangedAt) OVER (ORDER BY e.Id) AS PrevAt
                           FROM dbo.MenuTemporaryOutEvents e JOIN dbo.Users u ON u.Id=e.ChangedBy WHERE e.MenuItemId={b} AND e.Id>{logStart})
                SELECT CASE WHEN STRING_AGG(CONCAT(UserName,':',CONVERT(int,OldIsTemporarilyOut),'>',CONVERT(int,IsTemporarilyOut)),',') WITHIN GROUP (ORDER BY Id)='{expected}'
                             AND SUM(CASE WHEN PrevAt>ChangedAt THEN 1 ELSE 0 END)=0 THEN 1 ELSE 0 END FROM l
                """, "7. Six toggles in a row → six log entries in order, right actor, before → after state and non-decreasing time");
            var historyB = await Html(manager, $"/Kitchen/Dishes/History?dishId={b}");
            Assert(Regex.Matches(historyB, $"data-dish-id=\"{b}\" data-new-state=\"(true|false)\"").Select(m => m.Groups[1].Value).Take(6)
                    .SequenceEqual(sequence.Reverse().Select(s => s.Item3 ? "true" : "false")),
                "7. History page lists the six changes newest first");
            await AssertConsistent(connection, guest, kitchen, waiter, "after six toggles");

            // 8. Quyền trên toàn luồng: Bếp, Quản lý thao tác được (bước 1–7); Phục vụ, Thu ngân bị chặn; chỉ Quản lý xem nhật ký.
            var logBefore = await Scalar(connection, "SELECT COUNT(*) FROM dbo.MenuTemporaryOutEvents");
            foreach (var (who, client) in new[] { ("Waiter", waiter), ("Cashier", cashier) })
            {
                using var screen = await client.GetAsync("/Kitchen/Dishes");
                var page = await Html(client, who == "Waiter" ? "/Ordering" : "/Cashier");
                using var toggle = await client.PostAsync($"/Kitchen/Dishes/{a}/TemporarilyOut", Form(("isTemporarilyOut", "true"), ("__RequestVerificationToken", Token(page))));
                using var log = await client.GetAsync("/Kitchen/Dishes/History");
                Assert(Blocked(screen) && Blocked(toggle) && Blocked(log), $"8. {who} cannot open today's dish list, toggle \"Tạm hết\" or read the history");
            }
            using (var kitchenLog = await kitchen.GetAsync("/Kitchen/Dishes/History"))
                Assert(Blocked(kitchenLog), "8. Kitchen toggles but cannot read the history (Manager only)");
            Assert(await Scalar(connection, "SELECT COUNT(*) FROM dbo.MenuTemporaryOutEvents") == logBefore
                && await Scalar(connection, $"SELECT CONVERT(int,IsTemporarilyOut) FROM dbo.MenuItems WHERE Id={a}") == 0,
                "8. Blocked attempts change nothing and write no log entry");

            // 9. Nhiều món tạm hết trước 00:00 → hệ thống chạy qua 00:00 (Asia/Ho_Chi_Minh) → tất cả về còn hàng.
            foreach (var dish in new[] { a, b, c }) await Toggle(kitchen, dish, on: true);
            var d = (await Ids(connection, $"SELECT TOP(1) m.Id FROM dbo.MenuItems m JOIN dbo.MenuCategories c ON c.Id=m.CategoryId WHERE m.IsActive=1 AND c.IsActive=1 AND m.IsSoldOut=0 AND m.Id NOT IN ({a},{b},{c}) ORDER BY m.Id")).Single();
            var zone = FindZone();
            var midnightUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone).Date, DateTimeKind.Unspecified), zone);
            var cutoff = $"'{midnightUtc:yyyy-MM-ddTHH:mm:ss.fff}'";
            // Giả lập: A, B, C được báo tạm hết lúc 22:00 tối qua (giờ Việt Nam); lần chạy 00:00 hôm nay chưa diễn ra (web tắt qua đêm).
            await DatabaseTool.Execute(connection, $"""
                UPDATE e SET ChangedAt=DATEADD(hour,-2,{cutoff}) FROM dbo.MenuTemporaryOutEvents e
                WHERE e.Id IN (SELECT MAX(Id) FROM dbo.MenuTemporaryOutEvents WHERE MenuItemId IN ({a},{b},{c}) GROUP BY MenuItemId);
                UPDATE dbo.MenuTemporaryOutEvents SET ChangedAt=DATEADD(hour,-3,{cutoff}) WHERE MenuItemId IN ({a},{b},{c}) AND ChangedAt>DATEADD(hour,-2,{cutoff});
                DELETE dbo.ScheduledJobRuns WHERE JobName='TemporaryOutReset' AND ScheduledFor={cutoff};
                """);
            Assert(await Scalar(connection, $"SELECT COUNT(*) FROM dbo.MenuItems WHERE Id IN ({a},{b},{c}) AND IsTemporarilyOut=1") == 3,
                "9. Three dishes are temporarily out before 00:00");
            await AssertConsistent(connection, guest, kitchen, waiter, "three dishes temporarily out before 00:00");
            var dUpdatedAt = await Scalar(connection, $"SELECT DATEDIFF_BIG(millisecond,'2000-01-01',UpdatedAt) FROM dbo.MenuItems WHERE Id={d}");

            // Web khởi động lại sau 00:00: tác vụ nền chạy cho mốc 00:00 giờ Việt Nam (như khi đồng hồ qua nửa đêm).
            foreach (var client in new[] { kitchen, manager, waiter, cashier }) client.Dispose();
            await web.DisposeAsync();
            web = null;
            web = await Web.Start(connection, new() { ["Email__RetryPollSeconds"] = "0" });
            (kitchen, manager, waiter, cashier) = (await SignIn(web, "kitchen", password), await SignIn(web, "manager", password),
                await SignIn(web, "waiter", password), await SignIn(web, "cashier", password));
            using var guest2 = new HttpClient { BaseAddress = web.Client.BaseAddress };
            await WaitFor(connection, $"SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.ScheduledJobRuns WHERE JobName='TemporaryOutReset' AND ScheduledFor={cutoff} AND Status='Succeeded') THEN 1 ELSE 0 END",
                $"9. The 00:00 Asia/Ho_Chi_Minh reset ({midnightUtc:yyyy-MM-dd HH:mm} UTC) runs");
            await Check(connection, $"SELECT CASE WHEN COUNT(*)=0 THEN 1 ELSE 0 END FROM dbo.MenuItems WHERE Id IN ({a},{b},{c}) AND IsTemporarilyOut=1",
                "9. All three dishes are back in stock after 00:00");
            await Check(connection, $"""
                SELECT CASE WHEN COUNT(*)=3 AND COUNT(DISTINCT MenuItemId)=3 AND MAX(ChangedBy) IS NULL AND MIN(CONVERT(int,OldIsTemporarilyOut))=1 AND MAX(CONVERT(int,IsTemporarilyOut))=0 THEN 1 ELSE 0 END
                FROM dbo.MenuTemporaryOutEvents WHERE MenuItemId IN ({a},{b},{c}) AND ChangedAt>={cutoff} AND ChangedBy IS NULL
                """, "9. One system log entry per reset dish (\"Hệ thống – tự đặt lại 00:00\")");
            Assert(await Scalar(connection, $"SELECT DATEDIFF_BIG(millisecond,'2000-01-01',UpdatedAt) FROM dbo.MenuItems WHERE Id={d}") == dUpdatedAt,
                "9. A dish that was already in stock is not touched by the reset");
            await AssertConsistent(connection, guest2, kitchen, waiter, "after 00:00");
            Assert((await Html(manager, $"/Kitchen/Dishes/History?dishId={a}")).Contains("Hệ thống – tự đặt lại 00:00"), "9. History shows the automatic 00:00 reset");

            // 10. Sau 00:00 món nhận order bình thường.
            foreach (var dish in new[] { a, b, c })
                Assert(await Order(waiter, dish) == HttpStatusCode.Redirect && !await DbOrderRefused(connection, waiterId, dish), $"10. Dish {dish} takes orders again after 00:00");
            foreach (var client in new[] { kitchen, manager, waiter, cashier }) client.Dispose();
            Console.WriteLine("PASS: S2-08 temporarily-out acceptance scenario.");
        }
        finally
        {
            if (web is not null) await web.DisposeAsync();
        }
    }

    // ---- Các bước dùng chung ----

    private static async Task Toggle(HttpClient client, long dishId, bool on)
    {
        var page = await Html(client, "/Kitchen/Dishes");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/Kitchen/Dishes/{dishId}/TemporarilyOut")
            { Content = Form(("isTemporarilyOut", on ? "true" : "false"), ("__RequestVerificationToken", Token(page))) };
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        using var response = await client.SendAsync(request);
        Assert(response.StatusCode == HttpStatusCode.OK && (await response.Content.ReadAsStringAsync()).Contains($"\"isTemporarilyOut\":{(on ? "true" : "false")}"),
            $"Toggle dish {dishId} {(on ? "on" : "off")} with one tap");
    }

    /// <summary>
    /// Bấm một chạm rồi đo: từ lúc máy chủ xác nhận tới khi API trạng thái (các màn hình hỏi mỗi 3 giây) thấy thay đổi.
    /// Độ trễ xấu nhất trên màn hình = thời gian đó + một chu kỳ hỏi lại.
    /// </summary>
    private static async Task<long> ToggleAndMeasure(HttpClient client, HttpClient guest, long dishId, bool on)
    {
        await Toggle(client, dishId, on);
        var timer = Stopwatch.StartNew();
        while (true)
        {
            if ((await Unavailable(guest)).Contains(dishId) == on) return timer.ElapsedMilliseconds + PollIntervalMs;
            if (timer.ElapsedMilliseconds > MaxDisplayDelayMs) return timer.ElapsedMilliseconds + PollIntervalMs;
            await Task.Delay(100);
        }
    }

    /// <summary>Trạng thái của MỌI món giống nhau ở database, API trạng thái, Món trong ngày, thực đơn công khai và màn hình gọi món.</summary>
    private static async Task AssertConsistent(string connection, HttpClient guest, HttpClient kitchen, HttpClient waiter, string when)
    {
        var db = (await Ids(connection, $"""
            SELECT m.Id FROM dbo.MenuItems m JOIN dbo.MenuCategories c ON c.Id=m.CategoryId
            WHERE m.IsActive=1 AND c.IsActive=1 AND (m.IsTemporarilyOut=1 OR (m.IsSoldOut=1 AND m.SoldOutBusinessDate=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME()))))
            """)).ToHashSet();
        var api = await Unavailable(guest);
        static Dictionary<long, bool> States(string html, string attribute) => Regex.Matches(html, $"data-availability-id=\"(\\d+)\"[^>]*?{attribute}=\"(true|false)\"")
            .GroupBy(m => long.Parse(m.Groups[1].Value)).ToDictionary(g => g.Key, g => g.First().Groups[2].Value == "true");
        var daily = States(await Html(kitchen, "/Kitchen/Dishes"), "data-unavailable");
        var publicMenu = States(await Html(guest, "/Menu"), "data-sold-out");
        var ordering = States(await Html(waiter, "/Ordering"), "data-unavailable");
        bool Same(Dictionary<long, bool> screen) => screen.Count > 0 && screen.All(s => s.Value == db.Contains(s.Key)) && db.All(screen.ContainsKey);
        Assert(api.SetEquals(db) && Same(daily) && Same(publicMenu) && Same(ordering),
            $"Consistent ({when}): status API, today's dish list, public menu and ordering screen agree for every dish ({db.Count} not taking orders)");
    }

    private static async Task<HashSet<long>> Unavailable(HttpClient guest)
    {
        using var response = await guest.GetAsync("/api/menu/availability");
        var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return json.GetProperty("unavailable").EnumerateArray().Select(e => e.GetInt64()).ToHashSet();
    }

    private static async Task<HttpStatusCode> Order(HttpClient waiter, long dishId)
    {
        var page = await Html(waiter, "/Ordering");
        using var response = await waiter.PostAsync("/Ordering/Checkout", Form(("CartJson", $"[{{\"dishId\":{dishId},\"quantity\":1,\"price\":1,\"name\":\"x\"}}]"), ("__RequestVerificationToken", Token(page))));
        return response.StatusCode;
    }

    /// <summary>Gọi món thật qua usp_SubmitOrder trong một giao dịch rồi huỷ (không để lại phiên bàn thử). true = bị từ chối vì món hết (51028).</summary>
    private static async Task<bool> DbOrderRefused(string connection, long waiterId, long dishId)
    {
        try
        {
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
            return false;
        }
        catch (SqlException ex) when (ex.Number == 51028) { return true; }
    }

    private static string DishItem(string html, long dishId)
    {
        var match = Regex.Match(html, $"<li class=\"public-dish\"[^>]*data-availability-id=\"{dishId}\".*?</li>", RegexOptions.Singleline);
        return match.Success ? match.Value : "";
    }

    private static async Task<List<long>> Ids(string connection, string sql)
    {
        await using var cn = new SqlConnection(connection);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var ids = new List<long>();
        while (await reader.ReadAsync()) ids.Add(Convert.ToInt64(reader.GetValue(0)));
        return ids;
    }

    private static bool Blocked(HttpResponseMessage response)
    {
        var location = Location(response);
        return response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
            || (response.StatusCode == HttpStatusCode.Redirect && (location.Contains("/Account/AccessDenied") || location.Contains("/Account/Login")));
    }

    private static async Task<HttpClient> SignIn(Web web, string user, string password)
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() };
        var client = new HttpClient(handler, disposeHandler: true) { BaseAddress = web.Client.BaseAddress, Timeout = TimeSpan.FromSeconds(30) };
        await Login(client, user, password);
        return client;
    }

    private static TimeZoneInfo FindZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time"); }
    }
}
