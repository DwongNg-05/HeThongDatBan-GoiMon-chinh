using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using static RestaurantManagement.DbTool.BookingConfirmationVerification;

namespace RestaurantManagement.DbTool;

/// <summary>
/// S3-09 Task 1 (SQL Server thật + web thật): cảnh báo đặt bàn trễ 15 phút và "Đánh dấu khách không tới".
/// - Quy tắc mốc (dbo.fn_LateReservationAlerts với thời điểm cho trước): trước mốc 1 ms chưa cảnh báo, đúng mốc và sau mốc có.
/// - Khách đã tới, đã huỷ: không cảnh báo.
/// - Phục vụ đánh dấu qua API: lượt chuyển NoShow, bàn "Đã đặt trước" chuyển "Trống" (kèm sự kiện đồng bộ sơ đồ), lịch sử số điện thoại thêm đúng một lần.
/// - Gửi lặp và nhiều nhân viên cùng bấm: chỉ một lần được ghi nhận, số lần không tới không tăng thêm.
/// - Khách được đánh dấu đã tới trước khi xác nhận: bị từ chối, bàn không bị giải phóng.
/// - Hai màn hình nhận cảnh báo qua API (sơ đồ bàn) và danh sách đặt bàn; Bếp bị chặn.
/// Cần các tài khoản manager/waiter/kitchen do ApiAuthorizationVerification tạo (chạy trong "verify" sau bước đó).
/// </summary>
internal static class LateReservationVerification
{
    internal static async Task Run(string connection)
    {
        var password = ApiAuthorizationVerification.Password;
        await using var web = await Web.Start(connection, new() { ["Email__RetryPollSeconds"] = "0" });
        var waiter = await SignIn(web, "waiter", password);
        var manager = await SignIn(web, "manager", password);
        var kitchen = await SignIn(web, "kitchen", password);

        // Bàn trống, chưa có phiên và không có lượt đã xác nhận/đã tới quanh thời điểm hiện tại.
        var tables = await Ids(connection, """
            SELECT TOP(5) t.Id FROM dbo.DiningTables t JOIN dbo.Areas a ON a.Id=t.AreaId
            WHERE t.IsActive=1 AND a.IsActive=1 AND t.Status='Available'
              AND NOT EXISTS(SELECT 1 FROM dbo.SessionTables st WHERE st.TableId=t.Id AND st.ReleasedAt IS NULL)
              AND NOT EXISTS(SELECT 1 FROM dbo.Reservations r WHERE r.TableId=t.Id AND r.Status IN ('Confirmed','Arrived')
                             AND r.StartsAt<DATEADD(hour,4,SYSUTCDATETIME()) AND r.EndsAt>DATEADD(hour,-4,SYSUTCDATETIME()))
            ORDER BY t.Id
            """);
        Assert(tables.Count == 5, "S3-09: five free tables for the late-reservation scenarios");

        // Các lượt đặt (giờ hẹn tính theo đồng hồ database). Mốc cảnh báo = giờ hẹn + 15 phút (RestaurantSettings.NoShowGraceMinutes).
        await DatabaseTool.Execute(connection, $"""
            DECLARE @n datetime2(3)=SYSUTCDATETIME();
            INSERT dbo.Reservations(Code,CustomerName,Phone,GuestCount,TableId,StartsAt,EndsAt,Status) VALUES
             ('S309A1',N'S309 Trễ có bàn','0985309001',4,{tables[0]},DATEADD(minute,-20,@n),DATEADD(minute,70,@n),'Confirmed'),
             ('S309A2',N'S309 Chưa tới mốc','0985309002',2,{tables[1]},DATEADD(minute,-10,@n),DATEADD(minute,80,@n),'Confirmed'),
             ('S309A3',N'S309 Chờ chưa xếp bàn','0985309003',2,NULL,DATEADD(minute,-30,@n),DATEADD(minute,60,@n),'Pending'),
             ('S309A4',N'S309 Nhiều người bấm','0985309004',3,{tables[2]},DATEADD(minute,-25,@n),DATEADD(minute,65,@n),'Confirmed'),
             ('S309A5',N'S309 Vừa tới','0985309005',2,{tables[3]},DATEADD(minute,-18,@n),DATEADD(minute,72,@n),'Confirmed'),
             ('S309A6',N'S309 Đã tới','0985309006',2,{tables[4]},DATEADD(minute,-40,@n),DATEADD(minute,50,@n),'Arrived'),
             ('S309A7',N'S309 Đã huỷ','0985309007',2,NULL,DATEADD(minute,-40,@n),DATEADD(minute,50,@n),'Cancelled'),
             ('S309A8',N'S309 Cùng số lần trước','0985309001',4,NULL,DATEADD(minute,-50,@n),DATEADD(minute,40,@n),'Pending');
            UPDATE dbo.Reservations SET ArrivedAt=DATEADD(minute,-35,@n) WHERE Code='S309A6';
            UPDATE dbo.DiningTables SET Status='Reserved' WHERE Id IN ({tables[0]},{tables[1]},{tables[2]},{tables[3]});
            UPDATE dbo.DiningTables SET Status='Serving' WHERE Id={tables[4]};
            """);
        Task<long> IdOf(string code) => Scalar(connection, $"SELECT Id FROM dbo.Reservations WHERE Code='{code}'");
        var (late, early, pending, crowd) = (await IdOf("S309A1"), await IdOf("S309A2"), await IdOf("S309A3"), await IdOf("S309A4"));
        var (arriving, arrived, cancelled, samePhone) = (await IdOf("S309A5"), await IdOf("S309A6"), await IdOf("S309A7"), await IdOf("S309A8"));

        // 1. Quy tắc mốc 15 phút (trước / tại / sau), khách đã tới và đã huỷ không cảnh báo.
        string At(long id, string offset) => $"SELECT COUNT(*) FROM dbo.fn_LateReservationAlerts((SELECT DATEADD(millisecond,{offset},DATEADD(minute,15,StartsAt)) FROM dbo.Reservations WHERE Id={id})) WHERE Id={id}";
        Assert(await Scalar(connection, At(late, "-1")) == 0, "S3-09: 1 ms before the 15-minute mark there is no warning");
        Assert(await Scalar(connection, At(late, "0")) == 1, "S3-09: exactly at the 15-minute mark the warning starts");
        Assert(await Scalar(connection, At(late, "1500000")) == 1, "S3-09: 25 minutes after the mark the warning is still shown");
        await Check(connection, $"SELECT CASE WHEN LateMinutes=40 THEN 1 ELSE 0 END FROM dbo.fn_LateReservationAlerts((SELECT DATEADD(minute,40,StartsAt) FROM dbo.Reservations WHERE Id={late})) WHERE Id={late}",
            "S3-09: late minutes are counted from the appointment time");
        await Check(connection, $"SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.fn_LateReservationAlerts(SYSUTCDATETIME()) WHERE Id IN ({arrived},{cancelled})) THEN 0 ELSE 1 END",
            "S3-09: a guest who has arrived (or a cancelled booking) is never warned");
        await Check(connection, $"SELECT CASE WHEN (SELECT COUNT(*) FROM dbo.fn_LateReservationAlerts(SYSUTCDATETIME()) WHERE Id IN ({late},{pending},{crowd},{arriving},{samePhone}))=5 AND NOT EXISTS(SELECT 1 FROM dbo.fn_LateReservationAlerts(SYSUTCDATETIME()) WHERE Id={early}) THEN 1 ELSE 0 END",
            "S3-09: now, the four bookings past the mark are warned and the one 10 minutes late is not");

        // 2. Cả hai màn hình: API cảnh báo (sơ đồ bàn) và danh sách đặt bàn trong ngày; Bếp bị chặn.
        using (var json = JsonDocument.Parse(await waiter.GetStringAsync("/api/reservation-alerts")))
        {
            var alerts = json.RootElement.GetProperty("alerts").EnumerateArray().ToList();
            var ids = alerts.Select(a => a.GetProperty("id").GetInt64()).ToHashSet();
            Assert(ids.IsSupersetOf([late, pending, crowd, arriving]) && !ids.Contains(early) && !ids.Contains(arrived),
                "S3-09: the table map's warning API lists the late bookings only");
            var first = alerts.Single(a => a.GetProperty("id").GetInt64() == late);
            Assert(first.GetProperty("phone").GetString() == "098****001" && first.GetProperty("lateMinutes").GetInt32() >= 15
                   && first.GetProperty("tableCode").GetString()!.Length > 0 && first.GetProperty("lateLabel").GetString()!.StartsWith("Trễ "),
                "S3-09: a warning carries the table, masked phone and how late the guest is");
            var nextWarn = json.RootElement.GetProperty("nextWarnAtUtc").GetDateTime();
            var evaluated = json.RootElement.GetProperty("evaluatedAtUtc").GetDateTime();
            Assert(nextWarn > evaluated && nextWarn <= evaluated.AddMinutes(6),
                "S3-09: the API tells the screen when the next booking reaches the mark (refresh without reloading)");
        }
        var daily = await waiter.GetStringAsync("/ReservationManagement/Daily");
        using (var json = JsonDocument.Parse(daily))
        {
            var rows = json.RootElement.GetProperty("reservations").EnumerateArray().ToList();
            var lateIds = rows.Where(r => r.GetProperty("isLate").GetBoolean()).Select(r => r.GetProperty("id").GetInt64()).ToHashSet();
            Assert(lateIds.IsSupersetOf([late, pending, crowd, arriving]) && !lateIds.Contains(early) && !lateIds.Contains(arrived)
                   && rows.TakeWhile(r => r.GetProperty("isLate").GetBoolean()).Count() == lateIds.Count,
                "S3-09: the reservation list flags the same late bookings and shows them first");
            Assert(!daily.Contains("0985309001"), "S3-09: the reservation list never sends raw phone numbers");
        }
        using (var blocked = await kitchen.GetAsync("/api/reservation-alerts"))
            Assert(blocked.StatusCode == HttpStatusCode.Forbidden, "S3-09: the kitchen account cannot read reservation warnings");

        // 3. Chưa tới mốc: máy chủ từ chối đánh dấu.
        using (var tooEarly = await MarkNoShow(waiter, early))
            Assert(tooEarly.StatusCode == HttpStatusCode.Conflict && (await tooEarly.Content.ReadAsStringAsync()).Contains("chưa trễ"),
                "S3-09: a booking that is not yet 15 minutes late cannot be marked as no-show");
        await Check(connection, $"SELECT CASE WHEN Status='Confirmed' THEN 1 ELSE 0 END FROM dbo.Reservations WHERE Id={early}", "S3-09: the refused booking is unchanged");

        // 4. Phục vụ đánh dấu khách không tới: trạng thái + giải phóng bàn + lịch sử số điện thoại trong một thao tác.
        var eventsBefore = await Scalar(connection, $"SELECT COUNT(*) FROM dbo.TableStatusChangeEvents WHERE TableId={tables[0]}");
        using (var marked = await MarkNoShow(waiter, late))
        {
            Assert(marked.StatusCode == HttpStatusCode.OK, "S3-09: the waiter marks the late guest as no-show");
            using var json = JsonDocument.Parse(await marked.Content.ReadAsStringAsync());
            Assert(!json.RootElement.GetProperty("alreadyMarked").GetBoolean() && json.RootElement.GetProperty("tableReleased").GetBoolean()
                   && json.RootElement.GetProperty("tableStatus").GetString() == "Available" && json.RootElement.GetProperty("phoneNoShowCount").GetInt32() == 1,
                "S3-09: the answer reports the freed table and one no-show for the phone number");
        }
        await Check(connection, $"""
            SELECT CASE WHEN r.Status='NoShow' AND r.NoShowAt IS NOT NULL AND t.Status='Available'
              AND (SELECT COUNT(*) FROM dbo.ReservationNoShowHistory h WHERE h.ReservationId=r.Id AND h.Phone='0985309001' AND h.NoShowAt=r.NoShowAt)=1
              AND (SELECT COUNT(*) FROM dbo.ReservationEvents e WHERE e.ReservationId=r.Id AND e.ToStatus='NoShow'
                   AND e.ActorUserId=(SELECT Id FROM dbo.Users WHERE UserName=N'waiter'))=1
             THEN 1 ELSE 0 END
            FROM dbo.Reservations r JOIN dbo.DiningTables t ON t.Id=r.TableId WHERE r.Id={late}
            """, "S3-09: booking is NoShow, its table is free and the phone history has the no-show time");
        Assert(await Scalar(connection, $"SELECT COUNT(*) FROM dbo.TableStatusChangeEvents WHERE TableId={tables[0]} AND Status='Available'") > 0
               && await Scalar(connection, $"SELECT COUNT(*) FROM dbo.TableStatusChangeEvents WHERE TableId={tables[0]}") > eventsBefore,
            "S3-09: the freed table is pushed to every open table map (status change event)");
        using (var json = JsonDocument.Parse(await waiter.GetStringAsync("/api/reservation-alerts")))
            Assert(json.RootElement.GetProperty("alerts").EnumerateArray().All(a => a.GetProperty("id").GetInt64() != late),
                "S3-09: the warning disappears from the table map after marking");
        using (var json = JsonDocument.Parse(await waiter.GetStringAsync("/ReservationManagement/Daily")))
            Assert(json.RootElement.GetProperty("reservations").EnumerateArray().Single(r => r.GetProperty("id").GetInt64() == late) is var row
                   && !row.GetProperty("isLate").GetBoolean() && row.GetProperty("status").GetString() == "Khách không tới",
                "S3-09: the reservation list shows the booking as no-show without a warning");

        // 5. Gửi lặp: không ghi thêm, số lần không tới không tăng.
        using (var again = await MarkNoShow(manager, late))
        {
            using var json = JsonDocument.Parse(await again.Content.ReadAsStringAsync());
            Assert(again.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("alreadyMarked").GetBoolean()
                   && json.RootElement.GetProperty("phoneNoShowCount").GetInt32() == 1,
                "S3-09: sending the action again is accepted as already done");
        }
        await Check(connection, $"SELECT CASE WHEN (SELECT COUNT(*) FROM dbo.ReservationNoShowHistory WHERE Phone='0985309001')=1 AND (SELECT COUNT(*) FROM dbo.ReservationEvents WHERE ReservationId={late} AND ToStatus='NoShow')=1 THEN 1 ELSE 0 END",
            "S3-09: a repeated action does not add a second no-show");

        // 6. Nhiều nhân viên cùng bấm (song song): đúng một lần ghi nhận.
        var crowdResults = await Task.WhenAll(Enumerable.Range(0, 6).Select(async i =>
        {
            using var response = await MarkNoShow(i % 2 == 0 ? waiter : manager, crowd);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return (response.StatusCode, Already: json.RootElement.GetProperty("alreadyMarked").GetBoolean());
        }));
        Assert(crowdResults.All(r => r.StatusCode == HttpStatusCode.OK) && crowdResults.Count(r => !r.Already) == 1,
            "S3-09: six simultaneous clicks by two staff members record the no-show exactly once");
        await Check(connection, $"SELECT CASE WHEN (SELECT COUNT(*) FROM dbo.ReservationNoShowHistory WHERE ReservationId={crowd})=1 AND (SELECT COUNT(*) FROM dbo.ReservationEvents WHERE ReservationId={crowd} AND ToStatus='NoShow')=1 AND (SELECT Status FROM dbo.DiningTables WHERE Id={tables[2]})='Available' THEN 1 ELSE 0 END",
            "S3-09: one history row and one event after simultaneous clicks; the table is free");

        // 7. Khách tới (được đánh dấu đã tới) trước khi thao tác không tới được xác nhận: từ chối, bàn không bị giải phóng.
        await DatabaseTool.Execute(connection, $"UPDATE dbo.Reservations SET Status='Arrived', ArrivedAt=SYSUTCDATETIME() WHERE Id={arriving}; UPDATE dbo.DiningTables SET Status='Serving' WHERE Id={tables[3]};");
        using (var refused = await MarkNoShow(waiter, arriving))
            Assert(refused.StatusCode == HttpStatusCode.Conflict && (await refused.Content.ReadAsStringAsync()).Contains("Khách đã tới"),
                "S3-09: a guest marked as arrived just before cannot be marked as no-show");
        await Check(connection, $"SELECT CASE WHEN r.Status='Arrived' AND r.NoShowAt IS NULL AND t.Status='Serving' AND NOT EXISTS(SELECT 1 FROM dbo.ReservationNoShowHistory h WHERE h.ReservationId=r.Id) THEN 1 ELSE 0 END FROM dbo.Reservations r JOIN dbo.DiningTables t ON t.Id=r.TableId WHERE r.Id={arriving}",
            "S3-09: the arrived guest keeps the table and gets no no-show record");

        // 8. Lượt chưa xếp bàn: đánh dấu được, không đụng tới bàn nào. Cùng số điện thoại → lịch sử cộng dồn (2 lần).
        await Check(connection, $"SELECT CASE WHEN Status='Reserved' THEN 1 ELSE 0 END FROM dbo.DiningTables WHERE Id={tables[1]}", "S3-09: other reserved tables are untouched");
        using (var second = await MarkNoShow(waiter, samePhone))
        {
            using var json = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
            Assert(second.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("phoneNoShowCount").GetInt32() == 2
                   && json.RootElement.GetProperty("tableCode").ValueKind == JsonValueKind.Null,
                "S3-09: a second no-show of the same phone number adds up to two in its history");
        }

        // 9. Thủ tục cũ (lịch bàn của Quản lý) cũng ghi lịch sử số điện thoại, đúng một lần.
        var managerId = await Scalar(connection, "SELECT Id FROM dbo.Users WHERE UserName=N'manager'");
        await DatabaseTool.Execute(connection, $"EXEC dbo.usp_MarkManagedReservationNoShow @ReservationId={pending},@ActorUserId={managerId};");
        await Check(connection, $"SELECT CASE WHEN (SELECT COUNT(*) FROM dbo.ReservationNoShowHistory WHERE ReservationId={pending} AND Phone='0985309003')=1 THEN 1 ELSE 0 END",
            "S3-09: the manager's table schedule no-show also writes the phone history");
        Console.WriteLine("PASS: S3-09 Task 1 late reservation warning and no-show checks.");
    }

    private static async Task<HttpResponseMessage> MarkNoShow(HttpClient client, long reservationId)
    {
        var page = await Html(client, "/ReservationManagement");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/reservation-alerts/{reservationId}/no-show");
        request.Headers.Add("RequestVerificationToken", Token(page));
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
