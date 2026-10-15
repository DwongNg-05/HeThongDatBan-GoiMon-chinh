using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using static RestaurantManagement.DbTool.BookingConfirmationVerification;

namespace RestaurantManagement.DbTool;

/// <summary>
/// S3-09 Task 2 (SQL Server thật + web thật): "Gia hạn 15 phút" giữ bàn, tối đa một lần.
/// - Thời hạn mới = thời điểm bấm + 15 phút (quy tắc đề xuất, docs/S3-09-Task2.md); lưu ExtensionCount = 1 và HoldExtendedUntil.
/// - Sau gia hạn: cảnh báo được gỡ trên API sơ đồ bàn và danh sách đặt bàn, hai màn hình nhận thời hạn mới; bàn vẫn "Đã đặt trước".
/// - Hết hạn mà khách chưa tới: cảnh báo hiện lại (đúng tại thời hạn, trước 1 ms thì chưa), không gia hạn lần hai được,
///   nhưng vẫn đánh dấu khách không tới được (giải phóng bàn, lịch sử số điện thoại đúng một lần).
/// - Khách đã tới trong thời gian gia hạn: không cảnh báo lại, không gia hạn / đánh dấu không tới được, bàn giữ nguyên.
/// - Gửi lặp và 6 yêu cầu song song của hai nhân viên: đúng một lần gia hạn.
/// - Chưa tới mốc cảnh báo thì không gia hạn; Bếp bị chặn.
/// "Hết hạn" được mô phỏng bằng cách lùi HoldExtendedUntil trong database (không chờ 15 phút thật).
/// Cần các tài khoản manager/waiter/kitchen do ApiAuthorizationVerification tạo (chạy trong "verify" sau bước đó).
/// </summary>
internal static class HoldExtensionVerification
{
    private const string EpochMs = "DATEDIFF_BIG(millisecond, '1970-01-01', {0})";

    internal static async Task Run(string connection)
    {
        var password = ApiAuthorizationVerification.Password;
        await using var web = await Web.Start(connection, new() { ["Email__RetryPollSeconds"] = "0" });
        var waiter = await SignIn(web, "waiter", password);
        var manager = await SignIn(web, "manager", password);
        var kitchen = await SignIn(web, "kitchen", password);

        // Bàn trống, chưa có phiên và không có lượt đã xác nhận/đã tới quanh thời điểm hiện tại.
        var tables = await Ids(connection, """
            SELECT TOP(4) t.Id FROM dbo.DiningTables t JOIN dbo.Areas a ON a.Id=t.AreaId
            WHERE t.IsActive=1 AND a.IsActive=1 AND t.Status='Available'
              AND NOT EXISTS(SELECT 1 FROM dbo.SessionTables st WHERE st.TableId=t.Id AND st.ReleasedAt IS NULL)
              AND NOT EXISTS(SELECT 1 FROM dbo.Reservations r WHERE r.TableId=t.Id AND r.Status IN ('Confirmed','Arrived')
                             AND r.StartsAt<DATEADD(hour,4,SYSUTCDATETIME()) AND r.EndsAt>DATEADD(hour,-4,SYSUTCDATETIME()))
            ORDER BY t.Id
            """);
        Assert(tables.Count == 4, "S3-09 T2: four free tables for the hold-extension scenarios");

        await DatabaseTool.Execute(connection, $"""
            DECLARE @n datetime2(3)=SYSUTCDATETIME();
            INSERT dbo.Reservations(Code,CustomerName,Phone,GuestCount,TableId,StartsAt,EndsAt,Status) VALUES
             ('S309B1',N'S309 Gia hạn rồi không tới','0985309101',4,{tables[0]},DATEADD(minute,-20,@n),DATEADD(minute,70,@n),'Confirmed'),
             ('S309B2',N'S309 Chưa trễ','0985309102',2,{tables[1]},DATEADD(minute,-10,@n),DATEADD(minute,80,@n),'Confirmed'),
             ('S309B3',N'S309 Nhiều người gia hạn','0985309103',3,{tables[2]},DATEADD(minute,-25,@n),DATEADD(minute,65,@n),'Confirmed'),
             ('S309B4',N'S309 Tới trong gia hạn','0985309104',2,{tables[3]},DATEADD(minute,-30,@n),DATEADD(minute,60,@n),'Confirmed'),
             ('S309B5',N'S309 Chờ chưa xếp bàn','0985309105',2,NULL,DATEADD(minute,-16,@n),DATEADD(minute,74,@n),'Pending');
            UPDATE dbo.DiningTables SET Status='Reserved' WHERE Id IN ({tables[0]},{tables[1]},{tables[2]},{tables[3]});
            """);
        Task<long> IdOf(string code) => Scalar(connection, $"SELECT Id FROM dbo.Reservations WHERE Code='{code}'");
        var (later, early, crowd, arriving, pending) = (await IdOf("S309B1"), await IdOf("S309B2"), await IdOf("S309B3"), await IdOf("S309B4"), await IdOf("S309B5"));
        var waiterId = await Scalar(connection, "SELECT Id FROM dbo.Users WHERE UserName=N'waiter'");
        string Extensions(long id) => $"SELECT COUNT(*) FROM dbo.ReservationEvents WHERE ReservationId={id} AND Reason LIKE N'Gia hạn giữ bàn 15 phút%'";

        // 1. Đang cảnh báo, chưa gia hạn: API cho phép gia hạn.
        using (var json = JsonDocument.Parse(await waiter.GetStringAsync("/api/reservation-alerts")))
        {
            var alert = json.RootElement.GetProperty("alerts").EnumerateArray().Single(a => a.GetProperty("id").GetInt64() == later);
            Assert(alert.GetProperty("canExtend").GetBoolean() && alert.GetProperty("extensionCount").GetInt32() == 0
                   && alert.GetProperty("extensionLabel").ValueKind == JsonValueKind.Null,
                "S3-09 T2: a warned booking that was never extended offers \"Gia hạn 15 phút\"");
        }

        // 2. Gia hạn: thời hạn mới = lúc bấm + 15 phút; lưu số lần gia hạn; ghi sự kiện; bàn vẫn giữ.
        var before = await Scalar(connection, "SELECT " + string.Format(EpochMs, "SYSUTCDATETIME()"));
        using (var extended = await Post(waiter, later, "extend"))
        {
            Assert(extended.StatusCode == HttpStatusCode.OK, "S3-09 T2: the waiter extends the hold of a late booking");
            using var json = JsonDocument.Parse(await extended.Content.ReadAsStringAsync());
            Assert(json.RootElement.GetProperty("extensionCount").GetInt32() == 1
                   && json.RootElement.GetProperty("message").GetString()!.Contains("Đã gia hạn giữ bàn")
                   && json.RootElement.GetProperty("holdUntilTime").GetString()!.Length == 5,
                "S3-09 T2: the answer reports the new hold time");
        }
        var after = await Scalar(connection, "SELECT " + string.Format(EpochMs, "SYSUTCDATETIME()"));
        var holdUntil = await Scalar(connection, $"SELECT {string.Format(EpochMs, "HoldExtendedUntil")} FROM dbo.Reservations WHERE Id={later}");
        Assert(holdUntil >= before + 15 * 60_000 && holdUntil <= after + 15 * 60_000,
            "S3-09 T2: the new hold ends 15 minutes after the moment the waiter extended it");
        await Check(connection, $"""
            SELECT CASE WHEN r.ExtensionCount=1 AND r.Status='Confirmed' AND t.Status='Reserved'
              AND ({Extensions(later)})=1
              AND (SELECT COUNT(*) FROM dbo.ReservationEvents e WHERE e.ReservationId=r.Id AND e.ActorUserId={waiterId} AND e.Reason LIKE N'Gia hạn%')=1
             THEN 1 ELSE 0 END
            FROM dbo.Reservations r JOIN dbo.DiningTables t ON t.Id=r.TableId WHERE r.Id={later}
            """, "S3-09 T2: the booking stores one extension, the table stays reserved and the action is logged once");

        // 3. Trong thời gian gia hạn: gỡ cảnh báo, hai màn hình hiển thị thời hạn mới.
        await Check(connection, $"SELECT CASE WHEN NOT EXISTS(SELECT 1 FROM dbo.fn_LateReservationAlerts((SELECT DATEADD(millisecond,-1,HoldExtendedUntil) FROM dbo.Reservations WHERE Id={later})) WHERE Id={later}) THEN 1 ELSE 0 END",
            "S3-09 T2: 1 ms before the new hold ends there is still no warning");
        await Check(connection, $"SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.fn_LateReservationAlerts((SELECT HoldExtendedUntil FROM dbo.Reservations WHERE Id={later})) WHERE Id={later} AND ExtensionCount=1) THEN 1 ELSE 0 END",
            "S3-09 T2: exactly when the new hold ends the warning is back");
        await Check(connection, $"SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.fn_ExtendedReservationHolds((SELECT DATEADD(millisecond,-1,HoldExtendedUntil) FROM dbo.Reservations WHERE Id={later})) WHERE Id={later}) AND NOT EXISTS(SELECT 1 FROM dbo.fn_ExtendedReservationHolds((SELECT HoldExtendedUntil FROM dbo.Reservations WHERE Id={later})) WHERE Id={later}) THEN 1 ELSE 0 END",
            "S3-09 T2: the booking is listed as extended until the new hold ends, then no longer");
        using (var json = JsonDocument.Parse(await waiter.GetStringAsync("/api/reservation-alerts")))
        {
            Assert(json.RootElement.GetProperty("alerts").EnumerateArray().All(a => a.GetProperty("id").GetInt64() != later),
                "S3-09 T2: the table map no longer warns about the extended booking");
            var hold = json.RootElement.GetProperty("holds").EnumerateArray().Single(h => h.GetProperty("id").GetInt64() == later);
            Assert(hold.GetProperty("holdLabel").GetString()!.StartsWith("Gia hạn giữ bàn tới ") && hold.GetProperty("tableCode").GetString()!.Length > 0
                   && hold.GetProperty("phone").GetString() == "098****101",
                "S3-09 T2: the table map shows the new hold time for the table");
            var nextWarn = json.RootElement.GetProperty("nextWarnAtUtc").GetDateTime();
            Assert(nextWarn <= hold.GetProperty("holdUntilUtc").GetDateTime(),
                "S3-09 T2: the screen is told to refresh no later than when the extension ends");
        }
        using (var json = JsonDocument.Parse(await waiter.GetStringAsync("/ReservationManagement/Daily")))
        {
            var row = json.RootElement.GetProperty("reservations").EnumerateArray().Single(r => r.GetProperty("id").GetInt64() == later);
            Assert(row.GetProperty("isExtended").GetBoolean() && !row.GetProperty("isLate").GetBoolean()
                   && row.GetProperty("holdLabel").GetString()!.StartsWith("Gia hạn giữ bàn tới "),
                "S3-09 T2: the reservation list shows the new hold time instead of the warning");
        }

        // 4. Gia hạn lần hai (gửi lặp, nhân viên khác): bị từ chối, không đổi thời hạn.
        using (var again = await Post(manager, later, "extend"))
            Assert(again.StatusCode == HttpStatusCode.Conflict && (await again.Content.ReadAsStringAsync()).Contains("đã được gia hạn một lần"),
                "S3-09 T2: a second extension is refused");
        Assert(await Scalar(connection, $"SELECT {string.Format(EpochMs, "HoldExtendedUntil")} FROM dbo.Reservations WHERE Id={later}") == holdUntil
               && await Scalar(connection, Extensions(later)) == 1,
            "S3-09 T2: the refused second extension changes nothing");

        // 5. Nhiều nhân viên cùng bấm gia hạn (6 yêu cầu song song): đúng một lần thành công.
        var crowdResults = await Task.WhenAll(Enumerable.Range(0, 6).Select(async i =>
        {
            using var response = await Post(i % 2 == 0 ? waiter : manager, crowd, "extend");
            return response.StatusCode;
        }));
        Assert(crowdResults.Count(s => s == HttpStatusCode.OK) == 1 && crowdResults.Count(s => s == HttpStatusCode.Conflict) == 5,
            "S3-09 T2: six simultaneous extensions by two staff members succeed exactly once");
        await Check(connection, $"SELECT CASE WHEN (SELECT ExtensionCount FROM dbo.Reservations WHERE Id={crowd})=1 AND ({Extensions(crowd)})=1 THEN 1 ELSE 0 END",
            "S3-09 T2: one extension stored and logged after simultaneous clicks");

        // 6. Chưa tới mốc cảnh báo: không gia hạn được.
        using (var tooEarly = await Post(waiter, early, "extend"))
            Assert(tooEarly.StatusCode == HttpStatusCode.Conflict && (await tooEarly.Content.ReadAsStringAsync()).Contains("chưa trễ"),
                "S3-09 T2: a booking that is not yet warned cannot be extended");
        await Check(connection, $"SELECT CASE WHEN ExtensionCount=0 AND HoldExtendedUntil IS NULL THEN 1 ELSE 0 END FROM dbo.Reservations WHERE Id={early}",
            "S3-09 T2: the refused booking keeps no extension");
        using (var blocked = await Post(kitchen, crowd, "extend", withToken: false))
            Assert(blocked.StatusCode == HttpStatusCode.Forbidden, "S3-09 T2: the kitchen account cannot extend a hold");

        // 7. Khách tới trong thời gian gia hạn: không cảnh báo lại (kể cả khi đã qua thời hạn), không thao tác nào giải phóng bàn.
        using (var extended = await Post(waiter, arriving, "extend"))
            Assert(extended.StatusCode == HttpStatusCode.OK, "S3-09 T2: the hold of the fourth booking is extended");
        await DatabaseTool.Execute(connection, $"""
            UPDATE dbo.Reservations SET Status='Arrived', ArrivedAt=SYSUTCDATETIME() WHERE Id={arriving};
            UPDATE dbo.DiningTables SET Status='Serving' WHERE Id={tables[3]};
            UPDATE dbo.Reservations SET HoldExtendedUntil=DATEADD(second,-1,SYSUTCDATETIME()) WHERE Id={arriving};
            """);
        await Check(connection, $"SELECT CASE WHEN NOT EXISTS(SELECT 1 FROM dbo.fn_LateReservationAlerts(SYSUTCDATETIME()) WHERE Id={arriving}) AND NOT EXISTS(SELECT 1 FROM dbo.fn_ExtendedReservationHolds(DATEADD(minute,-5,SYSUTCDATETIME())) WHERE Id={arriving}) THEN 1 ELSE 0 END",
            "S3-09 T2: a guest who arrived during the extension is not warned when the extension ends");
        using (var refused = await Post(manager, arriving, "extend"))
            Assert(refused.StatusCode == HttpStatusCode.Conflict && (await refused.Content.ReadAsStringAsync()).Contains("Khách đã tới"),
                "S3-09 T2: an arrived guest cannot be extended again");
        using (var refused = await Post(waiter, arriving, "no-show"))
            Assert(refused.StatusCode == HttpStatusCode.Conflict, "S3-09 T2: an arrived guest cannot be marked as no-show after an extension");
        await Check(connection, $"SELECT CASE WHEN r.Status='Arrived' AND t.Status='Serving' AND NOT EXISTS(SELECT 1 FROM dbo.ReservationNoShowHistory h WHERE h.ReservationId=r.Id) THEN 1 ELSE 0 END FROM dbo.Reservations r JOIN dbo.DiningTables t ON t.Id=r.TableId WHERE r.Id={arriving}",
            "S3-09 T2: the arrived guest keeps the table");

        // 8. Hết thời hạn mà khách chưa tới (mô phỏng): cảnh báo hiện lại, chỉ còn "Đánh dấu khách không tới".
        await DatabaseTool.Execute(connection, $"UPDATE dbo.Reservations SET HoldExtendedUntil=DATEADD(second,-1,SYSUTCDATETIME()) WHERE Id={later};");
        using (var json = JsonDocument.Parse(await waiter.GetStringAsync("/api/reservation-alerts")))
        {
            var alert = json.RootElement.GetProperty("alerts").EnumerateArray().Single(a => a.GetProperty("id").GetInt64() == later);
            Assert(!alert.GetProperty("canExtend").GetBoolean() && alert.GetProperty("extensionLabel").GetString()!.StartsWith("Đã gia hạn 1 lần"),
                "S3-09 T2: when the extension ends the warning is back, without the extend action");
            Assert(json.RootElement.GetProperty("holds").EnumerateArray().All(h => h.GetProperty("id").GetInt64() != later),
                "S3-09 T2: the expired extension is no longer listed as a hold");
        }
        using (var json = JsonDocument.Parse(await waiter.GetStringAsync("/ReservationManagement/Daily")))
        {
            var row = json.RootElement.GetProperty("reservations").EnumerateArray().Single(r => r.GetProperty("id").GetInt64() == later);
            Assert(row.GetProperty("isLate").GetBoolean() && !row.GetProperty("isExtended").GetBoolean() && !row.GetProperty("canExtend").GetBoolean(),
                "S3-09 T2: the reservation list warns again and offers no second extension");
        }
        using (var third = await Post(waiter, later, "extend"))
            Assert(third.StatusCode == HttpStatusCode.Conflict, "S3-09 T2: an expired extension cannot be extended again");

        // 9. Đánh dấu khách không tới sau gia hạn: giải phóng bàn, lịch sử số điện thoại đúng một lần (gửi lặp không tăng).
        using (var marked = await Post(waiter, later, "no-show"))
        {
            Assert(marked.StatusCode == HttpStatusCode.OK, "S3-09 T2: the guest can still be marked as no-show after the extension");
            using var json = JsonDocument.Parse(await marked.Content.ReadAsStringAsync());
            Assert(json.RootElement.GetProperty("tableReleased").GetBoolean() && json.RootElement.GetProperty("phoneNoShowCount").GetInt32() == 1,
                "S3-09 T2: marking after the extension frees the table and records one no-show");
        }
        using (var again = await Post(manager, later, "no-show"))
        {
            using var json = JsonDocument.Parse(await again.Content.ReadAsStringAsync());
            Assert(again.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("alreadyMarked").GetBoolean(),
                "S3-09 T2: marking again is accepted as already done");
        }
        await Check(connection, $"SELECT CASE WHEN r.Status='NoShow' AND t.Status='Available' AND (SELECT COUNT(*) FROM dbo.ReservationNoShowHistory h WHERE h.Phone='0985309101')=1 THEN 1 ELSE 0 END FROM dbo.Reservations r JOIN dbo.DiningTables t ON t.Id=r.TableId WHERE r.Id={later}",
            "S3-09 T2: after the extension the table is free and the phone history has exactly one no-show");
        using (var afterNoShow = await Post(waiter, later, "extend"))
            Assert(afterNoShow.StatusCode == HttpStatusCode.Conflict && (await afterNoShow.Content.ReadAsStringAsync()).Contains("không tới"),
                "S3-09 T2: a booking marked as no-show cannot be extended");

        // 10. Lượt chưa xếp bàn: gia hạn được, hai màn hình hiển thị "Chưa xếp bàn" cùng thời hạn mới.
        using (var extended = await Post(waiter, pending, "extend"))
        {
            using var json = JsonDocument.Parse(await extended.Content.ReadAsStringAsync());
            Assert(extended.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("tableCode").ValueKind == JsonValueKind.Null,
                "S3-09 T2: a pending booking without a table can be extended");
        }
        using (var json = JsonDocument.Parse(await waiter.GetStringAsync("/api/reservation-alerts")))
            Assert(json.RootElement.GetProperty("holds").EnumerateArray().Any(h => h.GetProperty("id").GetInt64() == pending && h.GetProperty("tableCode").ValueKind == JsonValueKind.Null),
                "S3-09 T2: the extended booking without a table is listed with its new hold time");
        Console.WriteLine("PASS: S3-09 Task 2 hold extension checks.");
    }

    /// <param name="withToken">false: tài khoản không mở được trang đặt bàn (Bếp) — phân quyền chặn trước khi kiểm tra mã chống giả mạo.</param>
    private static async Task<HttpResponseMessage> Post(HttpClient client, long reservationId, string action, bool withToken = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/reservation-alerts/{reservationId}/{action}");
        if (withToken) request.Headers.Add("RequestVerificationToken", Token(await Html(client, "/ReservationManagement")));
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await client.SendAsync(request);
    }

    private static async Task<HttpClient> SignIn(Web web, string user, string password)
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() };
        var client = new HttpClient(handler, disposeHandler: true) { BaseAddress = web.Client.BaseAddress, Timeout = TimeSpan.FromSeconds(30) };
        await Login(client, user, password);
        return client;
    }

    private static async Task<List<int>> Ids(string connection, string sql)
    {
        await using var cn = new SqlConnection(connection);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var ids = new List<int>();
        while (await reader.ReadAsync()) ids.Add(reader.GetInt32(0));
        return ids;
    }
}
