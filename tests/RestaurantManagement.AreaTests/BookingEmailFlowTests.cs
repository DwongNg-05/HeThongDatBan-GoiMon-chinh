using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RestaurantManagement.DbTool;
using RestaurantManagement.Web.Models.Reservations;
using RestaurantManagement.Web.Services;
using RestaurantManagement.Web.Services.EmailVerification;

/// <summary>
/// S2-09 Task 3 (không cần database): luồng gửi email đặt bàn chạy bằng SmtpEmailSender thật của web,
/// gửi qua mạng tới máy chủ SMTP giả trên máy (FakeSmtpServer), hàng đợi giả lập theo đúng quy tắc của migration 030.
/// Ba tình huống (thành công ngay / gửi lại thành công / thất bại cả 3 lần gửi lại), nhiều lượt liên tiếp,
/// và nội dung email đọc lại từ SMTP khớp với từng lượt đặt bàn. Lệnh "verify-booking-email" kiểm tra cùng luồng với SQL Server + web thật.
/// </summary>
internal static class BookingEmailFlowTests
{
    private const string Address = "45 Nguyễn Huệ, Quận 1, TP.HCM";
    private static readonly DateTime Start = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);

    private sealed record Booking(long Id, string Name, string Email, string TableCode, string Code, int Guests, DateTime StartsAtUtc)
    {
        public string Payload => JsonSerializer.Serialize(new
        {
            Code, TableCode, CustomerName = Name, StartsAt = DateTime.SpecifyKind(StartsAtUtc, DateTimeKind.Unspecified),
            GuestCount = Guests, Status = "Pending", RestaurantName = "Bếp Nhà", RestaurantAddress = Address, RestaurantPhone = "0287654321"
        });

        public string When => BookingConfirmationEmail.FormatDateTime(VietnamTime.FromUtc(StartsAtUtc));
    }

    internal static async Task Run(Action<bool, string> check)
    {
        await using var smtp = new FakeSmtpServer();
        var sender = new SmtpEmailSender(
            Options.Create(new EmailOptions { Host = "127.0.0.1", Port = smtp.Port, EnableSsl = false, FromAddress = "no-reply@example.com" }),
            new WebEnvironment(), NullLogger<SmtpEmailSender>.Instance);
        var clock = new EmailRetryTests.Clock(Start);
        var outbox = new EmailRetryTests.SimulatedOutbox(clock);
        var dispatcher = new BookingEmailDispatcher(outbox, sender, NullLogger<BookingEmailDispatcher>.Instance);

        var ok = new Booking(1, "Khách An", "ok.flow@example.com", "A05", "K1A2B3", 4, Start.AddDays(3));
        var retry = new Booking(2, "Khách Bình", "retry.flow@example.com", "B07", "K4C5D6", 2, Start.AddDays(4));
        var fail = new Booking(3, "Khách Chi", "fail.flow@example.com", "C02", "K7E8F9", 3, Start.AddDays(5));
        smtp.FailFirst(retry.Email, 1);
        smtp.FailFirst(fail.Email, int.MaxValue);

        async Task<BookingEmailResult> Book(Booking b)
        {
            outbox.Queue(b.Id, b.Id * 100, recipient: b.Email, payloadJson: b.Payload);
            return await dispatcher.SendBookingReceivedAsync(b.Id * 100);
        }

        // Đặt bàn và gửi email ngay.
        var okResult = await Book(ok);
        var retryResult = await Book(retry);
        var failResult = await Book(fail);
        check(okResult.Outcome == BookingEmailOutcome.Sent && smtp.MailsTo(ok.Email).Count == 1 && outbox.Email(ok.Id) is { Status: "Sent", AttemptCount: 1 },
            "Booking flow A: email sent over SMTP on the first attempt");
        check(retryResult.Outcome == BookingEmailOutcome.Failed && failResult.Outcome == BookingEmailOutcome.Failed
            && smtp.MailsTo(retry.Email).Count == 0 && outbox.Email(retry.Id) is { Status: "Pending", AttemptCount: 1 }
            && outbox.Email(retry.Id).Attempts[0].Error!.Contains("SmtpFailedRecipient"),
            "Booking flow B, C: a rejected first send is recorded as a failed attempt waiting for a retry");
        CheckMail(check, smtp.MailsTo(ok.Email).Single(), ok, [retry, fail]);

        // Trang xác nhận khi email chưa gửi được vẫn có mã đặt bàn.
        var waiting = BookingEmailCustomerStatus.From(outbox.Record(retry.Id), retry.TableCode, "r***y@example.com")!;
        check(waiting is { IsFinal: false } && waiting.Message.Contains($"mã đặt bàn {retry.TableCode}") && waiting.Message.Contains("tự gửi lại lúc"),
            "Booking flow B: the customer keeps the booking code and sees the automatic retry");

        // Lần gửi lại 1 (sau 5 phút): B thành công, C lỗi.
        clock.Now = Start.AddMinutes(5).AddSeconds(-1);
        check(await dispatcher.RetryDueAsync() == 0, "Booking flow: nothing is sent again before 5 minutes");
        clock.Now = Start.AddMinutes(5);
        check(await dispatcher.RetryDueAsync() == 2, "Booking flow: both failed emails are retried after 5 minutes");
        check(outbox.Email(retry.Id) is { Status: "Sent", AttemptCount: 2 } && smtp.MailsTo(retry.Email).Count == 1 && smtp.Attempts(retry.Email) == 2,
            "Booking flow B: success at attempt 2 is the final result; the customer gets one email");
        CheckMail(check, smtp.MailsTo(retry.Email).Single(), retry, [ok, fail]);

        // Lần gửi lại 2, 3 của C.
        for (var minutes = 10; minutes <= 15; minutes += 5)
        {
            clock.Now = Start.AddMinutes(minutes);
            check(await dispatcher.RetryDueAsync() == 1, $"Booking flow C: retry at +{minutes} minutes");
        }
        clock.Now = Start.AddHours(3);
        check(await dispatcher.RetryDueAsync() == 0 && smtp.Attempts(fail.Email) == 4 && smtp.MailsTo(fail.Email).Count == 0
            && outbox.Email(fail.Id) is { Status: "Failed", AttemptCount: 4 }, "Booking flow C: after 3 failed retries the result is final; no 5th attempt");

        // Nhân viên xem đúng trạng thái trong cả ba tình huống.
        var okItem = ReservationEmailStatus.ToItem(outbox.Record(ok.Id));
        var retryItem = ReservationEmailStatus.ToItem(outbox.Record(retry.Id));
        var failItem = ReservationEmailStatus.ToItem(outbox.Record(fail.Id));
        check(okItem is { State: EmailDeliveryState.Sent, AttemptCount: 1, IsFinal: true } && okItem.Attempts.Count == 1
            && retryItem is { State: EmailDeliveryState.Sent, AttemptCount: 2, RetryCount: 1 } && retryItem.FinalResult.EndsWith("(ở lần thử thứ 2)")
            && failItem is { State: EmailDeliveryState.Failed, AttemptCount: 4, RetryCount: 3 } && failItem.FinalResult == "Thất bại sau 4 lần thử (lần gửi đầu và 3 lần gửi lại)"
            && new[] { okItem, retryItem, failItem }.Select(i => i.Recipient).SequenceEqual(new[] { ok.Email, retry.Email, fail.Email }),
            "Booking flow: staff see the right status, attempt count and final result for each of the three cases");
        var final = BookingEmailCustomerStatus.From(outbox.Record(fail.Id), fail.TableCode, "f***l@example.com")!;
        check(final is { State: EmailDeliveryState.Failed, IsFinal: true } && final.Message.Contains($"mã đặt bàn {fail.TableCode}"),
            "Booking flow C: the customer sees the final result and the booking code");

        // Nhiều lượt đặt bàn liên tiếp: không nhầm mã hay thông tin email.
        Booking[] row =
        [
            new(4, "Khách Dũng", "d.flow@example.com", "A06", "K0G1H2", 2, Start.AddDays(6)),
            new(5, "Khách Giang", "g.flow@example.com", "A07", "K3I4J5", 4, Start.AddDays(6)),
            new(6, "Khách Hoa", "h.flow@example.com", "A08", "K6L7M8", 3, Start.AddDays(6))
        ];
        foreach (var b in row) check((await Book(b)).Outcome == BookingEmailOutcome.Sent, $"Booking flow: consecutive booking {b.TableCode} sent");
        Booking[] everyone = [ok, retry, fail, .. row];
        foreach (var b in row)
            CheckMail(check, smtp.MailsTo(b.Email).Single(), b, everyone.Where(x => x != b));
        check(smtp.All.Count == 5, "Booking flow: exactly 5 emails delivered in total (none for the failed case)");
    }

    private static void CheckMail(Action<bool, string> check, ReceivedMail mail, Booking b, IEnumerable<Booking> others)
    {
        var html = System.Net.WebUtility.HtmlDecode(mail.Html);
        check(mail.Subject == $"Xác nhận đặt bàn {b.TableCode} – Bếp Nhà", $"Booking flow {b.TableCode}: subject decoded from SMTP has the booking code");
        foreach (var body in new[] { mail.Text, html })
            check(body.Contains(b.TableCode) && body.Contains(b.When) && body.Contains($"{b.Guests} người") && body.Contains(Address)
                && body.Contains(b.Name) && !body.Contains(b.Code) && others.All(o => !body.Contains(o.Name) && !body.Contains(o.TableCode)),
                $"Booking flow {b.TableCode}: email content matches its reservation only");
    }

    private sealed class WebEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "RestaurantManagement.AreaTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
