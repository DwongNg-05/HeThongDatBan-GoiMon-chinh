using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using static RestaurantManagement.DbTool.BookingConfirmationVerification;

namespace RestaurantManagement.DbTool;

/// <summary>
/// S2-09 Task 3 — nghiệm thu toàn bộ luồng: đặt bàn → hiển thị mã → gửi email (qua SMTP thật tới máy chủ SMTP giả)
/// → thử lại khi thất bại → ghi nhận kết quả → nhân viên xem trạng thái.
/// SQL Server thật + web thật (worker gửi lại kiểm tra mỗi giây). "Chờ 5 phút" được tua nhanh sau khi đã kiểm tra
/// database hẹn đúng 300 giây và web không gửi sớm.
/// Ba tình huống chạy song song trên cùng web, mỗi khách một trình duyệt (cookie) riêng:
///   A. gửi thành công ngay lần đầu;  B. lần đầu lỗi, lần gửi lại 1 thành công;  C. lỗi cả lần đầu và 3 lần gửi lại.
/// Thêm 3 lượt đặt bàn liên tiếp để chắc chắn mã và thông tin email không bị nhầm giữa các lượt.
/// </summary>
internal static class BookingEmailEndToEndVerification
{
    private const string Address = "S2-09 E2E · 45 Nguyễn Huệ, Quận 1, TP.HCM";

    private sealed record Booking(HttpClient Client, string Name, string Phone, int Guests, DateTime Local, string Email,
        int TableId, string TableCode, string ShownCode, string InternalCode, long Id, string Page, DateTime PostedAtUtc);

    internal static async Task Run(string connection, string password)
    {
        await DatabaseTool.Execute(connection, $"""
            UPDATE dbo.RestaurantSettings SET Name=N'Bếp Nhà',Address=N'{Address}',Phone='0287654321' WHERE Id=1;
            UPDATE dbo.Users SET MustChangePassword=0,FailedLoginCount=0,LockedUntil=NULL,IsActive=1 WHERE UserName=N'manager';
            UPDATE a SET IsActive=1 FROM dbo.Areas a WHERE a.Id IN (SELECT t.AreaId FROM dbo.DiningTables t WHERE t.IsActive=1 AND t.MaxCapacity>=4);
            """);
        var slots = await FreeSlots(connection, 4);
        var usedTables = new HashSet<string>();

        await using var smtp = new FakeSmtpServer();
        const string okEmail = "ok.e2e@example.com", retryEmail = "retry.e2e@example.com", failEmail = "fail.e2e@example.com";
        smtp.FailFirst(retryEmail, 1);
        smtp.FailFirst(failEmail, int.MaxValue);

        await using var web = await Web.Start(connection, new()
        {
            ["Email__Host"] = "127.0.0.1", ["Email__Port"] = smtp.Port.ToString(), ["Email__EnableSsl"] = "false",
            ["Email__FromAddress"] = "no-reply@example.com", ["Email__RetryPollSeconds"] = "1"
        });
        await Login(web.Client, "manager", password);

        // ---------------- Đặt bàn ----------------
        var ok = await BookAs(web, connection, password, "Khách E2E An", "0912093001", 4, slots[0].Local, okEmail, usedTables);
        var retry = await BookAs(web, connection, password, "Khách E2E Bình", "0912093002", 2, slots[1].Local, retryEmail, usedTables);
        var fail = await BookAs(web, connection, password, "Khách E2E Chi", "0912093003", 3, slots[2].Local, failEmail, usedTables);
        Booking[] all = [ok, retry, fail];

        // Mọi lượt: trang xác nhận hiển thị đầy đủ mã và thông tin, kể cả khi email lỗi; lượt đặt bàn lưu đúng dữ liệu đã nhập.
        foreach (var b in all)
        {
            Assert(b.ShownCode == b.TableCode && b.Page.Contains($"data-booking-code=\"{b.TableCode}\">{b.TableCode}</p>")
                && b.Page.Contains(BookingWhen(b.Local)) && b.Page.Contains($"{b.Guests} người") && b.Page.Contains($"lưu lại mã đặt bàn {b.TableCode}"),
                $"{b.Name}: confirmation page shows the full booking code {b.TableCode}, date/time and guest count");
            await Check(connection, $"""
                SELECT CASE WHEN r.CustomerName=N'{b.Name}' AND r.Phone='{b.Phone}' AND r.Email=N'{b.Email}' AND r.GuestCount={b.Guests}
                 AND r.TableId={b.TableId} AND r.StartsAt='{b.Local.AddHours(-7).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)}' AND r.Status='Pending' THEN 1 ELSE 0 END
                FROM dbo.Reservations r WHERE r.Id={b.Id}
                """, $"{b.Name}: the reservation stores exactly what was entered");
        }

        // ---------------- A. Thành công ngay lần đầu ----------------
        Assert(ok.Page.Contains("data-email-outcome=\"Sent\"") && ok.Page.Contains("Email xác nhận đã được gửi tới o***e@example.com"),
            "A: confirmation page says the email was sent");
        var okMail = smtp.MailsTo(okEmail);
        Assert(okMail.Count == 1 && smtp.Attempts(okEmail) == 1, "A: exactly one email delivered on the first attempt");
        var delivery = okMail[0].ReceivedAtUtc - ok.PostedAtUtc;
        Assert(delivery < TimeSpan.FromSeconds(60), $"A: email delivered {delivery.TotalMilliseconds:0} ms after booking (< 60 s)");
        await Check(connection, $"""
            SELECT CASE WHEN o.Status='Sent' AND o.AttemptCount=1 AND DATEDIFF(millisecond,r.CreatedAt,o.SentAt) BETWEEN 0 AND 60000
             AND (SELECT COUNT(*) FROM dbo.EmailAttempts a WHERE a.EmailId=o.Id AND a.Status='Succeeded')=1
             AND r.ConfirmationEmailStatus='Sent' AND r.ConfirmationEmailAttempts=1 THEN 1 ELSE 0 END
            FROM dbo.Reservations r JOIN dbo.EmailOutbox o ON o.ReservationId=r.Id AND o.MessageType='BookingReceived' WHERE r.Id={ok.Id}
            """, "A: database records 'sent' within 60 s of the booking being saved, 1 attempt");
        CheckMail(okMail[0], ok, all);

        // ---------------- B. Lần đầu lỗi, gửi lại thành công ----------------
        Assert(retry.Page.Contains("data-email-outcome=\"Failed\"") && retry.Page.Contains("Email xác nhận chưa gửi được")
            && retry.Page.Contains("Hệ thống sẽ tự gửi lại lúc") && retry.Page.Contains("data-email-final=\"false\""),
            "B: confirmation page keeps the booking and says the email will be sent again");
        await Check(connection, Waiting(retry.Id, 1), "B: after the failed first send the next try is scheduled exactly 5 minutes later");
        await StaffSees(web, retry, "Đang thử gửi lại", "1/4", "Lỗi lần thử trước", "Email: chưa gửi được, sẽ tự gửi lại (đã thử 1/4)");

        // ---------------- C. Lỗi lần đầu ----------------
        Assert(fail.Page.Contains("data-email-outcome=\"Failed\"") && fail.Page.Contains("Email xác nhận chưa gửi được"),
            "C: confirmation page keeps the booking although the email failed");
        await Check(connection, Waiting(fail.Id, 1), "C: after the failed first send the next try is scheduled exactly 5 minutes later");

        await Task.Delay(3000);
        Assert(smtp.Attempts(retryEmail) == 1 && smtp.Attempts(failEmail) == 1, "B, C: nothing is sent again before the 5 minutes are up");

        // Lần gửi lại 1: B thành công, C lỗi.
        await FastForward(connection, retry.Id, fail.Id);
        await WaitFor(connection, $"SELECT CASE WHEN Status='Sent' THEN 1 ELSE 0 END FROM dbo.EmailOutbox WHERE ReservationId={retry.Id} AND MessageType='BookingReceived'",
            "B: the email is sent again and succeeds on attempt 2");
        await WaitFor(connection, Settled(fail.Id, 2), "C: retry 1 of 3 was attempted");
        await Check(connection, Waiting(fail.Id, 2), "C: retry 1 failed; status updated and the next try is 5 minutes later");
        await Check(connection, $"""
            SELECT CASE WHEN o.AttemptCount=2 AND o.LastError IS NULL AND r.ConfirmationEmailStatus='Sent' AND r.ConfirmationEmailAttempts=2
             AND (SELECT COUNT(*) FROM dbo.EmailAttempts a WHERE a.EmailId=o.Id)=2
             AND EXISTS(SELECT 1 FROM dbo.EmailAttempts a WHERE a.EmailId=o.Id AND a.AttemptNumber=1 AND a.Status='Failed' AND a.Error LIKE N'%Smtp%')
             AND EXISTS(SELECT 1 FROM dbo.EmailAttempts a WHERE a.EmailId=o.Id AND a.AttemptNumber=2 AND a.Status='Succeeded')
             THEN 1 ELSE 0 END
            FROM dbo.Reservations r JOIN dbo.EmailOutbox o ON o.ReservationId=r.Id AND o.MessageType='BookingReceived' WHERE r.Id={retry.Id}
            """, "B: final result 'sent at attempt 2' is recorded with both attempts and the reservation email status");
        var retryMail = smtp.MailsTo(retryEmail);
        Assert(retryMail.Count == 1 && smtp.Attempts(retryEmail) == 2, "B: the customer receives exactly one email after one retry");
        CheckMail(retryMail[0], retry, all);

        // Lần gửi lại 2 và 3 của C: vẫn lỗi.
        await FastForward(connection, fail.Id);
        await WaitFor(connection, Settled(fail.Id, 3), "C: retry 2 of 3 was attempted");
        await Check(connection, Waiting(fail.Id, 3), "C: retry 2 failed; status updated and the next try is 5 minutes later");
        await FastForward(connection, fail.Id);
        await WaitFor(connection, Settled(fail.Id, 4), "C: retry 3 of 3 was attempted");
        await Check(connection, $"""
            SELECT CASE WHEN o.Status='Failed' AND o.AttemptCount=4 AND o.SentAt IS NULL AND o.LastError LIKE N'%Smtp%'
             AND (SELECT COUNT(*) FROM dbo.EmailAttempts a WHERE a.EmailId=o.Id AND a.Status='Failed')=4
             AND r.ConfirmationEmailStatus='Failed' AND r.ConfirmationEmailAttempts=4 AND r.Status='Pending' THEN 1 ELSE 0 END
            FROM dbo.Reservations r JOIN dbo.EmailOutbox o ON o.ReservationId=r.Id AND o.MessageType='BookingReceived' WHERE r.Id={fail.Id}
            """, "C: all 3 retries failed; the final result 'failed' is recorded and the booking stays valid");
        await FastForward(connection, fail.Id);
        await Task.Delay(3000);
        Assert(smtp.Attempts(failEmail) == 4 && smtp.MailsTo(failEmail).Count == 0, "C: the system never tries more than 3 retries");

        // Mã đặt bàn không đổi trong suốt quá trình gửi / gửi lại / thất bại.
        foreach (var b in all)
            await Check(connection, $"SELECT CASE WHEN r.Code='{b.InternalCode}' AND r.TableId={b.TableId} AND t.Code='{b.TableCode}' THEN 1 ELSE 0 END FROM dbo.Reservations r JOIN dbo.DiningTables t ON t.Id=r.TableId WHERE r.Id={b.Id}",
                $"{b.Name}: booking code {b.TableCode} is unchanged after all email attempts");

        // ---------------- Khách xem kết quả cuối cùng trên trang xác nhận ----------------
        var retryPage = await Html(retry.Client, "/Reservations/Success");
        Assert(retryPage.Contains($">{retry.TableCode}</p>") && retryPage.Contains("Email xác nhận đã được gửi tới r***e@example.com")
            && retryPage.Contains("lần thử thứ 2") && retryPage.Contains("data-email-final=\"true\""), "B: customer sees the email was finally sent");
        var failPage = await Html(fail.Client, "/Reservations/Success");
        Assert(failPage.Contains($">{fail.TableCode}</p>") && failPage.Contains("Không gửi được email xác nhận tới f***e@example.com sau 4 lần thử")
            && failPage.Contains($"lưu lại mã đặt bàn {fail.TableCode}") && failPage.Contains("data-email-final=\"true\""),
            "C: customer sees the final result and still has the full booking code");
        var okPage = await Html(ok.Client, "/Reservations/Success");
        Assert(okPage.Contains($">{ok.TableCode}</p>") && okPage.Contains("đã được gửi tới o***e@example.com") && !okPage.Contains(retry.TableCode + "</p>"),
            "A: each customer's page still shows only their own booking");

        // ---------------- Nhân viên xem đúng trạng thái trong cả ba tình huống ----------------
        await StaffSees(web, ok, "Đã gửi thành công", "1/4", "1. Lần gửi đầu", "Email: đã gửi");
        await StaffSees(web, retry, "Đã gửi thành công", "2/4", "(ở lần thử thứ 2)", "2. Lần gửi lại 1", "Email: đã gửi (ở lần thử thứ 2)");
        await StaffSees(web, fail, "Gửi thất bại", "4/4", "Thất bại sau 4 lần thử (lần gửi đầu và 3 lần gửi lại)", "4. Lần gửi lại 3",
            "Email: gửi thất bại sau 4 lần thử");
        var list = await Html(web.Client, "/Reservations");
        Assert(list.Contains("Email: đã gửi (ở lần thử thứ 2)") && list.Contains("Email: gửi thất bại sau 4 lần thử"),
            "Staff list shows the email result of each reservation");

        // ---------------- Nhiều lượt đặt bàn liên tiếp (cùng khung giờ, các bàn khác nhau) ----------------
        var d1 = await BookAs(web, connection, password, "Khách E2E Dũng", "0912093004", 2, slots[3].Local, "an.e2e@example.com", usedTables);
        var d2 = await BookAs(web, connection, password, "Khách E2E Giang", "0912093005", 4, slots[3].Local, "binh.e2e@example.com", usedTables);
        var d3 = await BookAs(web, connection, password, "Khách E2E Hoa", "0912093006", 3, slots[3].Local, "chi.e2e@example.com", usedTables);
        Booking[] row = [d1, d2, d3];
        Booking[] everyone = [.. all, .. row];
        Assert(row.Select(b => b.TableCode).Distinct().Count() == 3 && row.Select(b => b.Id).Distinct().Count() == 3,
            "Consecutive bookings get distinct codes");
        foreach (var b in row)
        {
            var mails = smtp.MailsTo(b.Email);
            Assert(mails.Count == 1 && smtp.Attempts(b.Email) == 1, $"{b.Name}: exactly one email, sent to {b.Email}");
            CheckMail(mails[0], b, everyone);
            await Check(connection, $"""
                SELECT CASE WHEN o.Recipient=N'{b.Email}' AND JSON_VALUE(o.PayloadJson,'$.TableCode')='{b.TableCode}'
                 AND JSON_VALUE(o.PayloadJson,'$.Code')='{b.InternalCode}' AND JSON_VALUE(o.PayloadJson,'$.CustomerName')=N'{b.Name}'
                 AND (SELECT COUNT(*) FROM dbo.EmailOutbox x WHERE x.ReservationId={b.Id})=1 THEN 1 ELSE 0 END
                FROM dbo.EmailOutbox o WHERE o.ReservationId={b.Id} AND o.MessageType='BookingReceived'
                """, $"{b.Name}: the queued email belongs to this reservation only");
            var page = await Html(b.Client, "/Reservations/Success");
            Assert(page.Contains($"data-booking-code=\"{b.TableCode}\">{b.TableCode}</p>") && page.Contains($"tới {b.Email[0]}***e@example.com")
                && everyone.Where(x => x != b).All(x => !page.Contains($">{x.TableCode}</p>")), $"{b.Name}: confirmation page shows only this booking");
            var details = await Html(web.Client, $"/Reservations/Details/{b.Id}");
            Assert(details.Contains(b.Email) && everyone.Where(x => x != b).All(x => !details.Contains(x.Email)) && details.Contains("Đã gửi thành công"),
                $"{b.Name}: staff details show only this reservation's email");
        }
        Assert(smtp.All.Count == 5, "Exactly 5 emails delivered in total (A, B after retry, 3 consecutive bookings; none for C)");
        Console.WriteLine("PASS: S2-09 Task 3 end-to-end booking email checks.");
    }

    private static async Task<Booking> BookAs(Web web, string connection, string password, string name, string phone, int guests, DateTime local, string email,
        HashSet<string> usedTables)
    {
        // Mỗi khách một trình duyệt (cookie riêng), nên trang xác nhận của ai chỉ thấy lượt của người đó.
        var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() };
        var client = new HttpClient(handler, disposeHandler: true) { BaseAddress = web.Client.BaseAddress, Timeout = TimeSpan.FromSeconds(70) };
        await Login(client, "manager", password);
        var (tableId, tableCode) = await PickTable(client, local, guests, usedTables);
        var posted = DateTime.UtcNow;
        var (shown, page) = await Book(client, name, phone, guests, local, email, tableId);
        var id = await Scalar(connection, $"SELECT TOP(1) Id FROM dbo.Reservations WHERE Phone='{phone}' ORDER BY Id DESC");
        return new Booking(client, name, phone, guests, local, email, tableId, tableCode, shown, await InternalCode(connection, phone), id, page, posted);
    }

    /// <summary>Bàn còn trống đầu tiên chưa dùng cho lượt khác trong bài kiểm tra, để mỗi lượt có mã đặt bàn (mã bàn) riêng.</summary>
    private static async Task<(int Id, string Code)> PickTable(HttpClient client, DateTime local, int guests, HashSet<string> usedTables)
    {
        var at = Uri.EscapeDataString(local.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture));
        using var response = await client.GetAsync($"/Reservations/AvailableTables?startsAt={at}&guestCount={guests}");
        Assert(response.StatusCode == HttpStatusCode.OK, "Free tables can be loaded for the booking form");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach (var table in json.RootElement.GetProperty("tables").EnumerateArray())
        {
            var code = table.GetProperty("code").GetString()!;
            if (usedTables.Add(code)) return (table.GetProperty("id").GetInt32(), code);
        }
        throw new Exception("FAIL: no unused free table for the end-to-end booking");
    }

    /// <summary>Email khách nhận (đọc lại từ SMTP) khớp đúng lượt đặt bàn và không chứa thông tin của lượt khác.</summary>
    private static void CheckMail(ReceivedMail mail, Booking b, IEnumerable<Booking> others)
    {
        var html = WebUtility.HtmlDecode(mail.Html);
        Assert(mail.Subject == $"Xác nhận đặt bàn {b.TableCode} – Bếp Nhà", $"{b.Name}: subject has the booking code ({mail.Subject})");
        foreach (var (body, kind) in new[] { (mail.Text, "text"), (html, "HTML") })
        {
            Assert(body.Contains(b.TableCode) && body.Contains(BookingWhen(b.Local)) && body.Contains($"{b.Guests} người")
                && body.Contains(Address) && body.Contains(b.Name) && !body.Contains(b.InternalCode),
                $"{b.Name}: email ({kind}) matches the reservation: code {b.TableCode}, {BookingWhen(b.Local)}, {b.Guests} guests, address");
            Assert(others.Where(o => o != b).All(o => !body.Contains(o.Name) && !Regex.IsMatch(body, $@"\b{Regex.Escape(o.TableCode)}\b")),
                $"{b.Name}: email ({kind}) contains no other reservation's name or code");
        }
    }

    private static async Task StaffSees(Web web, Booking b, params string[] texts)
    {
        var details = await Html(web.Client, $"/Reservations/Details/{b.Id}");
        var missing = texts.Where(t => !details.Contains(t)).ToArray();
        Assert(missing.Length == 0 && details.Contains($"data-table-code=\"{b.TableCode}\">{b.TableCode}</strong>") && details.Contains(b.Email),
            $"{b.Name}: staff see the email status ({string.Join(" | ", texts)})" + (missing.Length > 0 ? " — missing: " + string.Join(" | ", missing) : ""));
    }

    /// <summary>Lần thử thứ <paramref name="attempt"/> vừa thất bại: chờ gửi lại, hẹn đúng 300 giây sau, lượt đặt bàn cập nhật theo.</summary>
    private static string Waiting(long reservationId, int attempt) => $"""
        SELECT CASE WHEN o.Status='Pending' AND o.AttemptCount={attempt} AND a.Status='Failed' AND a.Error LIKE N'%Smtp%'
         AND DATEDIFF(second,a.CompletedAt,o.NextAttemptAt)=300
         AND r.ConfirmationEmailStatus='Retrying' AND r.ConfirmationEmailAttempts={attempt} THEN 1 ELSE 0 END
        FROM dbo.EmailOutbox o JOIN dbo.EmailAttempts a ON a.EmailId=o.Id AND a.AttemptNumber={attempt}
        JOIN dbo.Reservations r ON r.Id=o.ReservationId WHERE o.ReservationId={reservationId} AND o.MessageType='BookingReceived'
        """;

    private static string Settled(long reservationId, int attempt) =>
        $"SELECT CASE WHEN AttemptCount={attempt} AND Status IN ('Pending','Failed','Sent') THEN 1 ELSE 0 END FROM dbo.EmailOutbox WHERE ReservationId={reservationId} AND MessageType='BookingReceived'";

    /// <summary>Tua nhanh 5 phút chờ của các lượt đặt bàn đang chờ gửi lại.</summary>
    private static Task FastForward(string connection, params long[] reservationIds) =>
        DatabaseTool.Execute(connection, $"UPDATE dbo.EmailOutbox SET NextAttemptAt=SYSUTCDATETIME() WHERE MessageType='BookingReceived' AND ReservationId IN ({string.Join(',', reservationIds)});");
}
