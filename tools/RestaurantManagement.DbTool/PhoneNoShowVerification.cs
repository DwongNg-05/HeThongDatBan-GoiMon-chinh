using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using static RestaurantManagement.DbTool.BookingConfirmationVerification;

namespace RestaurantManagement.DbTool;

/// <summary>
/// S3-09 Task 3 (SQL Server thật + web thật): cảnh báo khi tạo đặt bàn cho số điện thoại có ít nhất 3 lần không tới trong 90 ngày.
/// - dbo.fn_PhoneNoShowCount: 0 / 2 / 3 / hơn 3 lần; lần ngoài 90 ngày không tính; đúng mốc biên 90 ngày được tính, sớm hơn 1 ms thì không;
///   lần sau thời điểm kiểm tra không tính.
/// - API tra cứu (Phục vụ): cùng kết quả cho mọi cách nhập tương đương; Bếp và khách chưa đăng nhập bị chặn.
/// - Form đặt hộ khách (/Reservations/Create) và form tạo lượt của Quản lý (/TableReservations/Create): đạt ngưỡng thì hiện cảnh báo
///   và chỉ tạo khi nhân viên xác nhận đúng số lần hiện tại; lịch sử tăng trong lúc điền form vẫn bị phát hiện khi gửi.
/// - Khách tự đặt (không đăng nhập): không thấy lịch sử của số điện thoại, đặt bàn bình thường.
/// Cần các tài khoản manager/waiter/kitchen do ApiAuthorizationVerification tạo.
/// </summary>
internal static class PhoneNoShowVerification
{
    private const string P0 = "0985309300", P2 = "0985309302", P3 = "0985309303", P5 = "0985309305", Boundary = "0985309310";

    internal static async Task Run(string connection)
    {
        var password = ApiAuthorizationVerification.Password;

        // Lịch sử không tới (ghi thẳng vào bảng lịch sử, ReservationId giả không trùng lượt thật).
        await DatabaseTool.Execute(connection, $"""
            DECLARE @n datetime2(3)=SYSUTCDATETIME(), @t datetime2(3)='2026-06-01T00:00:00.000';
            INSERT dbo.ReservationNoShowHistory(Phone,ReservationId,ReservationCode,AppointmentAt,NoShowAt,TableId) VALUES
             ('{P2}',9300000001,'T3P201',DATEADD(day,-10,@n),DATEADD(day,-10,@n),NULL),
             ('{P2}',9300000002,'T3P202',DATEADD(day,-89,@n),DATEADD(day,-89,@n),NULL),
             ('{P2}',9300000003,'T3P203',DATEADD(day,-91,@n),DATEADD(day,-91,@n),NULL),
             ('{P2}',9300000004,'T3P204',DATEADD(day,-120,@n),DATEADD(day,-120,@n),NULL),
             ('{P3}',9300000011,'T3P301',DATEADD(day,-2,@n),DATEADD(day,-2,@n),NULL),
             ('{P3}',9300000012,'T3P302',DATEADD(day,-30,@n),DATEADD(day,-30,@n),NULL),
             ('{P3}',9300000013,'T3P303',DATEADD(day,-60,@n),DATEADD(day,-60,@n),NULL),
             ('{P5}',9300000021,'T3P501',DATEADD(day,-1,@n),DATEADD(day,-1,@n),NULL),
             ('{P5}',9300000022,'T3P502',DATEADD(day,-5,@n),DATEADD(day,-5,@n),NULL),
             ('{P5}',9300000023,'T3P503',DATEADD(day,-15,@n),DATEADD(day,-15,@n),NULL),
             ('{P5}',9300000024,'T3P504',DATEADD(day,-45,@n),DATEADD(day,-45,@n),NULL),
             ('{P5}',9300000025,'T3P505',DATEADD(day,-80,@n),DATEADD(day,-80,@n),NULL),
             ('{Boundary}',9300000031,'T3PB01',DATEADD(day,-90,@t),DATEADD(day,-90,@t),NULL),
             ('{Boundary}',9300000032,'T3PB02',DATEADD(day,-90,@t),DATEADD(millisecond,-1,DATEADD(day,-90,@t)),NULL),
             ('{Boundary}',9300000033,'T3PB03',DATEADD(day,-1,@t),DATEADD(day,-1,@t),NULL),
             ('{Boundary}',9300000034,'T3PB04',DATEADD(day,1,@t),DATEADD(day,1,@t),NULL);
            """);

        // 1. Quy tắc đếm trong database.
        string Count(string phone, string now = "SYSUTCDATETIME()") => $"SELECT NoShowCount FROM dbo.fn_PhoneNoShowCount('{phone}', {now})";
        Assert(await Scalar(connection, Count(P0)) == 0 && await Scalar(connection, Count(P3)) == 3 && await Scalar(connection, Count(P5)) == 5,
            "S3-09 T3: 0, 3 and 5 no-shows are counted for their phone numbers");
        Assert(await Scalar(connection, Count(P2)) == 2, "S3-09 T3: no-shows older than 90 days (91 and 120 days) are not counted");
        Assert(await Scalar(connection, Count(Boundary, "'2026-06-01T00:00:00.000'")) == 2,
            "S3-09 T3: a no-show exactly 90 days before is counted, 1 ms earlier is not, and a later one is ignored");
        Assert(await Scalar(connection, Count(Boundary, "'2026-06-01T00:00:00.001'")) == 1,
            "S3-09 T3: 1 ms after the boundary the 90-day-old no-show drops out of the window");

        await using var web = await Web.Start(connection, new() { ["Email__RetryPollSeconds"] = "0" });
        var waiter = await SignIn(web, "waiter", password);
        var manager = await SignIn(web, "manager", password);
        var kitchen = await SignIn(web, "kitchen", password);
        var guest = NewClient(web);

        // 2. API tra cứu khi nhập / đổi số điện thoại.
        async Task<JsonElement> Lookup(HttpClient client, string phone)
        {
            using var response = await PhoneHistory(client, phone, "/Reservations/Create");
            Assert(response.StatusCode == HttpStatusCode.OK, $"S3-09 T3: the waiter looks up {phone}");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return json.RootElement.Clone();
        }
        foreach (var (phone, count, warned) in new[] { (P0, 0, false), (P2, 2, false), (P3, 3, true), (P5, 5, true) })
        {
            var result = await Lookup(waiter, phone);
            Assert(result.GetProperty("valid").GetBoolean() && result.GetProperty("noShowCount").GetInt32() == count
                   && result.GetProperty("isWarning").GetBoolean() == warned
                   && (!warned || result.GetProperty("message").GetString()!.Contains($"{count} lần khách không tới")),
                $"S3-09 T3: {count} no-shows → {(warned ? "warning with the count" : "no warning")}");
        }
        foreach (var typed in new[] { "0985 309 303", "+84985309303", "+84 985 309 303", "84985309303", "098.530.9303" })
        {
            var result = await Lookup(waiter, typed);
            Assert(result.GetProperty("normalizedPhone").GetString() == P3 && result.GetProperty("noShowCount").GetInt32() == 3,
                $"S3-09 T3: \"{typed}\" gives the same result as {P3}");
        }
        Assert(!(await Lookup(waiter, "12345")).GetProperty("valid").GetBoolean(), "S3-09 T3: an invalid number gives no result");
        using (var blocked = await PhoneHistory(kitchen, P3, null))
            Assert(blocked.StatusCode == HttpStatusCode.Forbidden, "S3-09 T3: the kitchen account cannot look up no-show history");
        using (var blocked = await PhoneHistory(guest, P3, null))
            Assert(blocked.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.Redirect,
                "S3-09 T3: a guest who is not signed in cannot look up no-show history");

        // 3. Form đặt hộ khách của nhân viên.
        var slots = await FreeSlots(connection, 4);
        var staffForm = await Html(waiter, "/Reservations/Create");
        Assert(staffForm.Contains("id=\"phone-no-show\"") && staffForm.Contains("phone-no-show-warning.js"),
            "S3-09 T3: the staff booking form has the no-show warning");
        var guestForm = await Html(guest, "/Reservations/Create");
        Assert(!guestForm.Contains("phone-no-show"), "S3-09 T3: the public booking form has no no-show lookup");

        using (var refused = await Book(waiter, "S309 T3 Ba lần", P3, slots[0].Local))
        {
            var body = WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync());
            Assert(refused.StatusCode == HttpStatusCode.OK && body.Contains("có 3 lần khách không tới trong 90 ngày")
                   && body.Contains("Hãy đánh dấu ô xác nhận"),
                "S3-09 T3: booking a phone with 3 no-shows shows the warning and asks for confirmation");
        }
        Assert(await Scalar(connection, $"SELECT COUNT(*) FROM dbo.Reservations WHERE Phone='{P3}'") == 0,
            "S3-09 T3: no booking is created before the staff member confirms");
        using (var accepted = await Book(waiter, "S309 T3 Ba lần", P3, slots[0].Local, confirmed: true, acknowledged: 3))
            Assert(accepted.StatusCode == HttpStatusCode.Redirect && Location(accepted).StartsWith("/Reservations/Success"),
                "S3-09 T3: after confirming the booking is created");
        Assert(await Scalar(connection, $"SELECT COUNT(*) FROM dbo.Reservations WHERE Phone='{P3}'") == 1, "S3-09 T3: exactly one booking created");

        // Lịch sử thay đổi trong lúc tạo đặt bàn: lúc mở form 2 lần (không cảnh báo), trước khi gửi có thêm 1 lần → 3.
        Assert(!(await Lookup(waiter, P2)).GetProperty("isWarning").GetBoolean(), "S3-09 T3: 2 no-shows when the form is filled in");
        await DatabaseTool.Execute(connection, $"INSERT dbo.ReservationNoShowHistory(Phone,ReservationId,ReservationCode,AppointmentAt,NoShowAt,TableId) VALUES('{P2}',9300000005,'T3P205',DATEADD(hour,-1,SYSUTCDATETIME()),DATEADD(minute,-1,SYSUTCDATETIME()),NULL);");
        using (var changed = await Book(waiter, "S309 T3 Vừa đủ ba", P2, slots[1].Local))
        {
            var body = WebUtility.HtmlDecode(await changed.Content.ReadAsStringAsync());
            Assert(changed.StatusCode == HttpStatusCode.OK && body.Contains("có 3 lần khách không tới"),
                "S3-09 T3: a no-show added while the form was open is caught when the booking is sent");
        }
        using (var stale = await Book(waiter, "S309 T3 Vừa đủ ba", P2, slots[1].Local, confirmed: true, acknowledged: 2))
            Assert(stale.StatusCode == HttpStatusCode.OK, "S3-09 T3: a confirmation of an older, smaller count is not enough");
        using (var accepted = await Book(waiter, "S309 T3 Vừa đủ ba", P2, slots[1].Local, confirmed: true, acknowledged: 3))
            Assert(accepted.StatusCode == HttpStatusCode.Redirect, "S3-09 T3: confirming the current count creates the booking");

        // Cách nhập tương đương khi gửi form: lưu đúng số chuẩn, vẫn bị kiểm tra.
        using (var typed = await Book(waiter, "S309 T3 Năm lần", "+84 985 309 305", slots[2].Local))
            Assert(typed.StatusCode == HttpStatusCode.OK && WebUtility.HtmlDecode(await typed.Content.ReadAsStringAsync()).Contains("có 5 lần khách không tới"),
                "S3-09 T3: \"+84 985 309 305\" is checked against the history of 0985309305");
        using (var accepted = await Book(waiter, "S309 T3 Năm lần", "+84 985 309 305", slots[2].Local, confirmed: true, acknowledged: 5))
            Assert(accepted.StatusCode == HttpStatusCode.Redirect, "S3-09 T3: the confirmed booking typed with +84 is created");
        Assert(await Scalar(connection, $"SELECT COUNT(*) FROM dbo.Reservations WHERE Phone='{P5}'") == 1,
            "S3-09 T3: the booking typed with +84 is stored under the standard number");

        // Số chưa đạt ngưỡng: không cảnh báo, tạo ngay.
        using (var plain = await Book(waiter, "S309 T3 Chưa đạt", P0, slots[3].Local))
            Assert(plain.StatusCode == HttpStatusCode.Redirect, "S3-09 T3: a phone below the threshold is booked without any warning");

        // 4. Khách tự đặt: không thấy lịch sử, đặt bàn bình thường.
        using (var self = await Book(guest, "S309 T3 Khách tự đặt", P5, slots[3].Local))
            Assert(self.StatusCode == HttpStatusCode.Redirect, "S3-09 T3: a guest booking for themselves is never blocked or told about the history");

        // 5. Form tạo lượt của Quản lý (lịch bàn).
        var (tableId, _) = await FirstFreeTable(guest, slots[2].Local, 2);
        using (var refused = await ManagedBook(manager, tableId, slots[2].Local, P3))
            Assert(refused.StatusCode == HttpStatusCode.OK && WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync()).Contains("có 3 lần khách không tới"),
                "S3-09 T3: the manager's table schedule form shows the same warning");
        using (var accepted = await ManagedBook(manager, tableId, slots[2].Local, P3, confirmed: true, acknowledged: 3))
            Assert(accepted.StatusCode == HttpStatusCode.Redirect, "S3-09 T3: the manager can continue after confirming");
        Console.WriteLine("PASS: S3-09 Task 3 phone no-show warning checks.");
    }

    private static async Task<HttpResponseMessage> PhoneHistory(HttpClient client, string phone, string? tokenPage)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/reservation-alerts/phone-history") { Content = Form(("phone", phone)) };
        if (tokenPage is not null) request.Headers.Add("RequestVerificationToken", Token(await Html(client, tokenPage)));
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> Book(HttpClient client, string name, string phone, DateTime local,
        bool confirmed = false, int acknowledged = 0)
    {
        var form = await Html(client, "/Reservations/Create");
        var values = new List<(string, string)>
        {
            ("CustomerName", name), ("Phone", phone), ("GuestCount", "2"),
            ("StartsAt", local.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture)),
            ("NoShowCountAcknowledged", acknowledged.ToString(CultureInfo.InvariantCulture)),
            ("__RequestVerificationToken", Token(form))
        };
        if (confirmed) values.Add(("NoShowWarningConfirmed", "true"));
        values.Add(("NoShowWarningConfirmed", "false"));
        return await client.PostAsync("/Reservations/Create", Form(values.ToArray()));
    }

    private static async Task<HttpResponseMessage> ManagedBook(HttpClient client, int tableId, DateTime local, string phone,
        bool confirmed = false, int acknowledged = 0)
    {
        var form = await Html(client, "/TableReservations/Create");
        var values = new List<(string, string)>
        {
            ("TableId", tableId.ToString(CultureInfo.InvariantCulture)),
            ("ReservationDate", local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("StartTime", local.ToString("HH:mm", CultureInfo.InvariantCulture)),
            ("CustomerName", "S309 T3 Quản lý tạo"), ("Phone", phone), ("InitialStatus", "Pending"),
            ("NoShowCountAcknowledged", acknowledged.ToString(CultureInfo.InvariantCulture)),
            ("__RequestVerificationToken", Token(form))
        };
        if (confirmed) values.Add(("NoShowWarningConfirmed", "true"));
        values.Add(("NoShowWarningConfirmed", "false"));
        return await client.PostAsync("/TableReservations/Create", Form(values.ToArray()));
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
