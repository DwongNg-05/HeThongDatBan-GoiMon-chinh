using System.Net;
using Microsoft.Data.SqlClient;
using static RestaurantManagement.DbTool.BookingConfirmationVerification;

namespace RestaurantManagement.DbTool;

/// <summary>
/// S2-08 Task 3 (web thật, SQL Server thật): tự đặt lại "Tạm hết" lúc 00:00 Asia/Ho_Chi_Minh.
/// Chuẩn bị món tạm hết TRƯỚC mốc 00:00 gần nhất, món còn hàng, món tạm hết SAU mốc; xoá lần chạy của mốc đó rồi khởi động web:
/// tác vụ nền (TemporaryOutResetWorker) chạy bù cho mốc hôm nay — đúng như khi tới 00:00.
/// Kiểm tra: món tạm hết trước 00:00 thành còn hàng; món còn hàng không bị đổi; mốc tính theo giờ Việt Nam;
/// sau khi đặt lại món nhận order (màn hình gọi món, database) và hiện "còn hàng" trên các màn hình; nhật ký "Hệ thống".
/// Chạy trong lệnh verify-api-permissions.
/// </summary>
internal static class TemporaryOutResetVerification
{
    internal static async Task Run(string connection)
    {
        var password = ApiAuthorizationVerification.Password;
        var zone = FindZone();
        var vietnamNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
        var midnightUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(vietnamNow.Date, DateTimeKind.Unspecified), zone);
        string Sql(DateTime utc) => $"'{utc:yyyy-MM-ddTHH:mm:ss.fff}'";

        // Mốc 00:00 giờ Việt Nam = 17:00 UTC ngày hôm trước.
        Assert(zone.BaseUtcOffset == TimeSpan.FromHours(7) && midnightUtc.Hour == 17 && midnightUtc.Minute == 0
            && TimeZoneInfo.ConvertTimeFromUtc(midnightUtc, zone).TimeOfDay == TimeSpan.Zero,
            $"Reset: the cut-off is 00:00 Asia/Ho_Chi_Minh = {midnightUtc:yyyy-MM-dd HH:mm} UTC");

        var ids = new List<long>();
        await using (var cn = new SqlConnection(connection))
        {
            await cn.OpenAsync();
            await using var cmd = new SqlCommand("""
                SELECT TOP(3) m.Id FROM dbo.MenuItems m JOIN dbo.MenuCategories c ON c.Id=m.CategoryId
                WHERE m.IsActive=1 AND c.IsActive=1 AND (m.IsSoldOut=0 OR m.SoldOutBusinessDate IS NULL OR m.SoldOutBusinessDate<>CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME())))
                ORDER BY m.Id DESC
                """, cn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) ids.Add(r.GetInt32(0));
        }
        Assert(ids.Count == 3, "Reset: three dishes on sale for the test");
        long before = ids[0], available = ids[1], after = ids[2];
        var kitchenId = await Scalar(connection, "SELECT Id FROM dbo.Users WHERE UserName=N'kitchen'");

        // Món "before": Bếp báo tạm hết lúc 23:59:59 (giờ Việt Nam) hôm qua. Món "after": báo tạm hết 00:00:01 hôm nay.
        // Món "available": còn hàng. Xoá lần chạy của mốc hôm nay (các bộ kiểm thử trước đã khởi động web) để tác vụ chạy lại.
        await DatabaseTool.Execute(connection, $"""
            UPDATE dbo.MenuItems SET IsSoldOut=0,IsTemporarilyOut=0 WHERE Id IN ({before},{available},{after});
            UPDATE dbo.MenuItems SET IsTemporarilyOut=1,UpdatedAt=DATEADD(second,-1,{Sql(midnightUtc)}) WHERE Id={before};
            INSERT dbo.MenuTemporaryOutEvents(MenuItemId,OldIsTemporarilyOut,IsTemporarilyOut,ChangedBy,ChangedAt)
              VALUES({before},0,1,{kitchenId},DATEADD(second,-1,{Sql(midnightUtc)}));
            UPDATE dbo.MenuItems SET IsTemporarilyOut=1,UpdatedAt=DATEADD(second,1,{Sql(midnightUtc)}) WHERE Id={after};
            INSERT dbo.MenuTemporaryOutEvents(MenuItemId,OldIsTemporarilyOut,IsTemporarilyOut,ChangedBy,ChangedAt)
              VALUES({after},0,1,{kitchenId},DATEADD(second,1,{Sql(midnightUtc)}));
            UPDATE dbo.MenuItems SET UpdatedAt=DATEADD(day,-1,{Sql(midnightUtc)}) WHERE Id={available};
            DELETE dbo.ScheduledJobRuns WHERE JobName='TemporaryOutReset' AND ScheduledFor={Sql(midnightUtc)};
            """);
        var availableEvents = await Scalar(connection, $"SELECT COUNT(*) FROM dbo.MenuTemporaryOutEvents WHERE MenuItemId={available}");
        Assert(await Scalar(connection, $"SELECT COUNT(*) FROM dbo.MenuItems WHERE Id IN ({before},{after}) AND IsTemporarilyOut=1") == 2,
            "Reset: a dish is temporarily out before the day changes");

        // Thủ tục từ chối mốc không phải 00:00 giờ Việt Nam và mốc chưa tới.
        foreach (var (label, at, code) in new[] { ("00:00 UTC (07:00 Vietnam)", midnightUtc.AddHours(7), 51090), ("tomorrow's 00:00", midnightUtc.AddDays(1), 51091) })
        {
            var refused = false;
            try { await DatabaseTool.Execute(connection, $"EXEC dbo.usp_ResetTemporarilyOutMenuItems @ScheduledFor={Sql(at)};"); }
            catch (SqlException ex) when (ex.Number == code) { refused = true; }
            Assert(refused, $"Reset: a cut-off at {label} is refused ({code})");
        }

        // Khởi động web: tác vụ nền chạy bù cho mốc 00:00 hôm nay.
        await using var web = await Web.Start(connection, new() { ["Email__RetryPollSeconds"] = "0" });
        await WaitFor(connection, $"SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.ScheduledJobRuns WHERE JobName='TemporaryOutReset' AND ScheduledFor={Sql(midnightUtc)} AND Status='Succeeded') THEN 1 ELSE 0 END",
            "Reset: the background task runs for today's 00:00 (Asia/Ho_Chi_Minh)");

        await Check(connection, $"SELECT CASE WHEN IsTemporarilyOut=0 THEN 1 ELSE 0 END FROM dbo.MenuItems WHERE Id={before}",
            "Reset: the dish marked before 00:00 is back in stock after the day changes");
        await Check(connection, $"""
            SELECT CASE WHEN COUNT(*)=1 AND MIN(CONVERT(int,OldIsTemporarilyOut))=1 AND MAX(CONVERT(int,IsTemporarilyOut))=0 AND MAX(ChangedBy) IS NULL
              AND MIN(ChangedAt)>={Sql(midnightUtc)} THEN 1 ELSE 0 END
            FROM dbo.MenuTemporaryOutEvents WHERE MenuItemId={before} AND ChangedAt>={Sql(midnightUtc)}
            """, "Reset log (PO decision): one entry by the system (no user), Tạm hết → Còn món, after 00:00");
        await Check(connection, $"""
            SELECT CASE WHEN IsTemporarilyOut=0 AND UpdatedAt=DATEADD(day,-1,{Sql(midnightUtc)})
              AND (SELECT COUNT(*) FROM dbo.MenuTemporaryOutEvents WHERE MenuItemId={available})={availableEvents} THEN 1 ELSE 0 END
            FROM dbo.MenuItems WHERE Id={available}
            """, "Reset: a dish already in stock is not touched (no update, no log entry)");
        await Check(connection, $"SELECT CASE WHEN IsTemporarilyOut=1 THEN 1 ELSE 0 END FROM dbo.MenuItems WHERE Id={after}",
            "Reset: a dish marked temporarily out after 00:00 (Vietnam) today keeps its status when the task catches up late");

        // Chạy lại cùng mốc: không đổi gì.
        var eventsBefore = await Scalar(connection, "SELECT COUNT(*) FROM dbo.MenuTemporaryOutEvents");
        Assert(await Scalar(connection, $"EXEC dbo.usp_ResetTemporarilyOutMenuItems @ScheduledFor={Sql(midnightUtc)};") == 0
            && await Scalar(connection, "SELECT COUNT(*) FROM dbo.MenuTemporaryOutEvents") == eventsBefore,
            "Reset: each day's reset runs only once");

        // Sau khi đặt lại: các màn hình hiện còn hàng và món nhận order trở lại.
        using (var guest = new HttpClient { BaseAddress = web.Client.BaseAddress })
        {
            using var api = await guest.GetAsync("/api/menu/availability");
            var json = System.Text.Json.JsonDocument.Parse(await api.Content.ReadAsStringAsync()).RootElement;
            var unavailable = json.GetProperty("unavailable").EnumerateArray().Select(e => e.GetInt64()).ToHashSet();
            Assert(!unavailable.Contains(before) && unavailable.Contains(after), "Reset: the status API (polled every 3 s by every screen) shows the dish back in stock");
            Assert((await Html(guest, "/Menu")).Contains($"data-availability-id=\"{before}\" data-sold-out=\"false\""), "Reset: public menu shows the dish without the \"Tạm hết\" label");
        }
        using var kitchen = await SignIn(web, "kitchen", password);
        Assert((await Html(kitchen, "/Kitchen/Dishes")).Contains($"data-availability-id=\"{before}\" data-unavailable=\"false\" data-temporarily-out=\"false\""),
            "Reset: today's dish list shows the dish back in stock");
        using var waiter = await SignIn(web, "waiter", password);
        var ordering = await Html(waiter, "/Ordering");
        Assert(ordering.Contains($"data-availability-id=\"{before}\" data-unavailable=\"false\""), "Reset: ordering screen shows the dish back in stock");
        using (var order = await waiter.PostAsync("/Ordering/Checkout", Form(("CartJson", $"[{{\"dishId\":{before},\"quantity\":1,\"price\":1,\"name\":\"x\"}}]"), ("__RequestVerificationToken", Token(ordering)))))
            Assert(order.StatusCode == HttpStatusCode.Redirect, "Reset: the ordering screen accepts an order for the dish again");
        var waiterId = await Scalar(connection, "SELECT Id FROM dbo.Users WHERE UserName=N'waiter'");
        // Gọi món thật trong database (rồi huỷ giao dịch để không để lại phiên bàn thử).
        await DatabaseTool.Execute(connection, $$"""
            BEGIN TRANSACTION;
            DECLARE @table int=(SELECT TOP(1) t.Id FROM dbo.DiningTables t JOIN dbo.Areas a ON a.Id=t.AreaId
              WHERE t.IsActive=1 AND a.IsActive=1 AND t.Status='Available' AND t.MaxCapacity>=2
               AND NOT EXISTS(SELECT 1 FROM dbo.SessionTables st WHERE st.TableId=t.Id AND st.ReleasedAt IS NULL) ORDER BY t.Id);
            EXEC dbo.usp_OpenSession @TableId=@table,@GuestCount=2,@ActorUserId={{waiterId}};
            DECLARE @session bigint=(SELECT MAX(Id) FROM dbo.DiningSessions);
            DECLARE @request uniqueidentifier=NEWID();
            DECLARE @items nvarchar(max)=CONCAT(N'[{"MenuItemId":',{{before}},N',"Quantity":1}]');
            EXEC dbo.usp_SubmitOrder @SessionId=@session,@RequestId=@request,@ItemsJson=@items,@ActorUserId={{waiterId}};
            IF NOT EXISTS(SELECT 1 FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId WHERE b.SessionId=@session AND i.MenuItemId={{before}})
              THROW 51099,N'Order was not created.',1;
            ROLLBACK;
            """);
        Assert(true, "Reset: the database accepts a new order for the dish again (usp_SubmitOrder)");

        // Dọn dẹp.
        await DatabaseTool.Execute(connection, $"UPDATE dbo.MenuItems SET IsTemporarilyOut=0 WHERE Id={after};");
        Console.WriteLine("PASS: S2-08 Task 3 daily temporarily-out reset checks.");
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
