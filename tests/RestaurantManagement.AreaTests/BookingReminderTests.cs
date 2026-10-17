using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantManagement.Web.Models.Reservations;
using RestaurantManagement.Web.Services;
using RestaurantManagement.Web.Services.EmailVerification;

/// <summary>
/// S4-08 Task 1 (không cần database): email nhắc trước giờ hẹn 2 tiếng.
/// - Lượt đã xác nhận được nhắc đúng thời điểm (còn 2 tiếng); lượt chưa tới mốc nhắc thì chưa gửi.
/// - Email có đúng mã đặt bàn, giờ hẹn và bàn đã xếp.
/// - Ghi nhận thời điểm gửi thành công; danh sách / chi tiết hiển thị đúng email nhận và trạng thái gửi.
/// Hàng đợi giả lập theo đúng quy tắc của migration 059 (usp_QueueDueBookingReminders, usp_ClaimDueBookingReminder).
/// </summary>
internal static class BookingReminderTests
{
    // 10:00 giờ Việt Nam ngày 17/10/2026.
    private static readonly DateTime Now = new(2026, 10, 17, 3, 0, 0, DateTimeKind.Utc);

    internal static async Task Run(Action<bool, string> check)
    {
        Rules(check);
        Content(check);
        await Dispatch(check);
        Display(check);
    }

    private static void Rules(Action<bool, string> check)
    {
        const string email = "khach@example.com";
        check(BookingReminderRules.IsDue("Confirmed", email, Now.AddHours(2), Now),
            "Reminder: a confirmed booking exactly 2 hours away is due");
        check(BookingReminderRules.IsDue("Confirmed", email, Now.AddMinutes(119), Now) && BookingReminderRules.IsDue("Confirmed", email, Now.AddMinutes(1), Now),
            "Reminder: a confirmed booking less than 2 hours away (still in the future) is due");
        check(!BookingReminderRules.IsDue("Confirmed", email, Now.AddHours(2).AddSeconds(1), Now) && !BookingReminderRules.IsDue("Confirmed", email, Now.AddDays(1), Now),
            "Reminder: a booking more than 2 hours away is not reminded yet");
        check(!BookingReminderRules.IsDue("Confirmed", email, Now, Now) && !BookingReminderRules.IsDue("Confirmed", email, Now.AddMinutes(-5), Now),
            "Reminder: a booking at or past its time is not reminded");
        check(!BookingReminderRules.IsDue("Pending", email, Now.AddHours(1), Now),
            "Reminder: a booking waiting for confirmation is not reminded");
        check(!BookingReminderRules.IsDue("Confirmed", null, Now.AddHours(1), Now) && !BookingReminderRules.IsDue("Confirmed", "  ", Now.AddHours(1), Now),
            "Reminder: a booking without email is not reminded");
        check(BookingReminderRules.RemindAt(new DateTime(2026, 10, 17, 19, 0, 0)) == new DateTime(2026, 10, 17, 17, 0, 0)
            && BookingReminderRules.DefaultLeadMinutes == 120,
            "Reminder: the reminder starts 2 hours before the booking time");
    }

    private static void Content(Action<bool, string> check)
    {
        var details = BookingConfirmationEmail.ParsePayload(Payload("ED095C", "A05", Now.AddHours(2), "Khách <b>An</b>"));
        var message = BookingReminderEmail.Create("an@example.com", details, "https://quan.example/ReservationLookup?code=ED095C");
        check(message.To == "an@example.com" && message.Subject == "Bếp Nhà - Nhắc lịch đặt bàn ED095C",
            "Reminder email: sent to the booking email with a subject naming the booking code");
        const string when = "12:00, Thứ Bảy ngày 17/10/2026";
        check(message.HtmlBody.Contains("ED095C") && message.HtmlBody.Contains("Giờ hẹn") && message.HtmlBody.Contains(when)
              && message.HtmlBody.Contains("data-field=\"table\">A05<"),
            "Reminder email (HTML): booking code, booking time in Vietnam time and the assigned table");
        check(message.TextBody.Contains("Mã đặt bàn:   ED095C") && message.TextBody.Contains($"Giờ hẹn:      {when}") && message.TextBody.Contains("Bàn:          A05"),
            "Reminder email (text): booking code, booking time and the assigned table");
        check(!message.HtmlBody.Contains("<b>An</b>") && message.HtmlBody.Contains("&lt;b&gt;An&lt;/b&gt;"),
            "Reminder email: customer input is HTML-encoded");

        var noTable = BookingReminderEmail.Create("x@example.com", BookingConfirmationEmail.ParsePayload(Payload("K1A2B3", null, Now.AddHours(2))));
        check(noTable.TextBody.Contains(BookingReminderEmail.NoTableLabel) && !noTable.HtmlBody.Contains("Xem lượt đặt bàn"),
            "Reminder email: old data without a table still sends a readable reminder");
    }

    private static async Task Dispatch(Action<bool, string> check)
    {
        var clock = new EmailRetryTests.Clock(Now);
        var outbox = new ReminderOutbox(clock);
        var sender = new CapturingSender();
        var dispatcher = new BookingEmailDispatcher(outbox, sender, NullLogger<BookingEmailDispatcher>.Instance);

        var due = outbox.Book(1, "R2H000", "due@example.com", "Confirmed", Now.AddHours(2), "A05");
        var notYet = outbox.Book(2, "R2H001", "later@example.com", "Confirmed", Now.AddHours(2).AddMinutes(1), "B07");
        var pending = outbox.Book(3, "R2H002", "pending@example.com", "Pending", Now.AddHours(1), "C02");
        var noEmail = outbox.Book(4, "R2H003", null, "Confirmed", Now.AddMinutes(90), "C03");

        var first = await dispatcher.SendDueRemindersAsync();
        check(first is { Queued: 1, Sent: 1, Failed: 0 } && sender.Sent.Count == 1 && sender.Sent[0].To == due.Email,
            "Reminder task: only the confirmed booking 2 hours away is sent a reminder");
        check(sender.Sent.All(m => m.To != notYet.Email && m.To != pending.Email) && outbox.EmailOf(notYet.Id) is null
              && outbox.EmailOf(pending.Id) is null && outbox.EmailOf(noEmail.Id) is null,
            "Reminder task: bookings not yet at the reminder time, unconfirmed or without email get nothing");
        var mail = sender.Sent[0];
        check(mail.HtmlBody.Contains(due.Code) && mail.HtmlBody.Contains(BookingConfirmationEmail.FormatDateTime(VietnamTime.FromUtc(due.StartsAtUtc)))
              && mail.HtmlBody.Contains("data-field=\"table\">A05<") && !mail.HtmlBody.Contains(notYet.Code),
            "Reminder task: the email holds the right booking code, booking time and table");
        check(outbox.EmailOf(due.Id) is { Status: "Sent", AttemptCount: 1 } sentEmail && sentEmail.SentAtUtc == Now && due.ReminderSentAtUtc == Now
              && due.ReminderStatus == "Sent",
            "Reminder task: the successful send time is recorded on the booking");

        var again = await dispatcher.SendDueRemindersAsync();
        check(again is { Queued: 0, Sent: 0, Failed: 0 } && sender.Sent.Count == 1,
            "Reminder task: running again right away sends nothing new");

        clock.Now = Now.AddMinutes(1);
        var later = await dispatcher.SendDueRemindersAsync();
        check(later is { Queued: 1, Sent: 1 } && sender.Sent.Count == 2 && sender.Sent[1].To == notYet.Email
              && sender.Sent[1].HtmlBody.Contains("data-field=\"table\">B07<") && notYet.ReminderSentAtUtc == Now.AddMinutes(1),
            "Reminder task: a booking is reminded once it reaches 2 hours before its time");

        // Lỗi gửi: ghi lần thử thất bại, chưa có thời điểm gửi thành công.
        var failing = outbox.Book(5, "R2H004", "fail@example.com", "Confirmed", clock.Now.AddMinutes(100), "D01");
        sender.FailNext = true;
        var failedRun = await dispatcher.SendDueRemindersAsync();
        check(failedRun is { Queued: 1, Sent: 0, Failed: 1 } && outbox.EmailOf(failing.Id) is { Status: "Pending", AttemptCount: 1, LastError: not null }
              && failing.ReminderSentAtUtc is null && failing.ReminderStatus == "Retrying",
            "Reminder task: a failed send records the error and no send time");
        check(failedRun.Summary.Contains("lỗi 1") && first.Summary.Contains("gửi thành công 1")
              && new BookingReminderRunResult(0, 0, 0).Summary == "Không có lượt đặt bàn nào đến giờ nhắc.",
            "Reminder task: run summary for the manager");
    }

    private static void Display(Action<bool, string> check)
    {
        var sent = new BookingReminderRecord(10, 1, "ED095C", "Khách An", Now.AddHours(2), "A05", "an@example.com", "Sent", 1, Now, null, Now);
        var waiting = new BookingReminderRecord(11, 2, "K1A2B3", "Khách Bình", Now.AddHours(2), null, "binh@example.com", "Pending", 0, null, null, Now.AddMinutes(-1));
        var failed = new BookingReminderRecord(12, 3, "K4C5D6", "Khách Chi", Now.AddHours(2), "C02", "chi@example.com", "Failed", 4, null, "550 hộp thư không tồn tại", Now.AddMinutes(-2));
        var list = BookingReminderRules.BuildList([sent, waiting, failed], sentOnly: false, leadMinutes: 120);
        var item = list.Items.Single(i => i.EmailId == 10);
        check(item is { ReservationCode: "ED095C", TableLabel: "A05", Recipient: "an@example.com", State: EmailDeliveryState.Sent, StateLabel: "Đã gửi thành công", ErrorMessage: null }
              && item.StartsAt == new DateTime(2026, 10, 17, 12, 0, 0) && item.SentAt == new DateTime(2026, 10, 17, 10, 0, 0),
            "Reminder list: booking code, booking time, table, recipient, status and send time in Vietnam time");
        check(list.Items.Single(i => i.EmailId == 11) is { TableLabel: "Chưa xếp bàn", State: EmailDeliveryState.Waiting, SentAt: null }
              && list.Items.Single(i => i.EmailId == 12) is { State: EmailDeliveryState.Failed, ErrorMessage: "550 hộp thư không tồn tại", SentAt: null },
            "Reminder list: waiting and failed reminders show their own status (and error)");
        check(list.SentCount == 1 && list.Items[0].EmailId == 10,
            "Reminder list: newest first, counts sent reminders");
        var sentOnly = BookingReminderRules.BuildList([sent, waiting, failed], sentOnly: true, leadMinutes: 120);
        check(sentOnly.Items.Count == 1 && sentOnly.Items[0].EmailId == 10 && sentOnly.SentOnly,
            "Reminder list: 'sent only' filter keeps only successfully sent reminders");

        var start = new DateTime(2026, 10, 17, 19, 0, 0);
        var now = new DateTime(2026, 10, 17, 10, 0, 0);
        check(BookingReminderRules.DetailLabel(null, "Confirmed", "a@example.com", start, now) == "Chưa tới giờ nhắc"
              && BookingReminderRules.DetailLabel(null, "Confirmed", "a@example.com", now.AddMinutes(90), now) == "Đến giờ nhắc, đang chờ tác vụ gửi"
              && BookingReminderRules.DetailLabel("Sent", "Confirmed", "a@example.com", start, now) == "Đã gửi thành công"
              && BookingReminderRules.DetailLabel(null, "Confirmed", null, start, now).StartsWith("Không gửi nhắc")
              && BookingReminderRules.DetailLabel(null, "Pending", "a@example.com", start, now).StartsWith("Không gửi nhắc"),
            "Reservation details: reminder status label for each situation");

        var model = new ReservationListItemViewModel
        {
            Code = "ED095C", Email = "an@example.com", Status = "Confirmed", StartsAt = start,
            ReminderEmailStatus = "Sent", ReminderEmailSentAt = start.AddHours(-2)
        };
        check(model.ReminderAt == start.AddHours(-2) && model.ReminderEmailLabel == "Đã gửi thành công" && model.ReminderEmailSentAt == start.AddHours(-2),
            "Reservation details: reminder recipient comes from the booking, with status and send time");
        check(ReservationEmailStatus.TypeLabel(BookingReminderRules.MessageType) == "Nhắc lịch đặt bàn"
              && BookingEmailDispatcher.BookingReminder == "BookingReminder",
            "Reservation details: the reminder appears in the email history as 'Nhắc lịch đặt bàn'");
    }

    private static string Payload(string code, string? table, DateTime startsAtUtc, string name = "Khách An") => JsonSerializer.Serialize(new
    {
        Code = code, TableCode = table, CustomerName = name, StartsAt = DateTime.SpecifyKind(startsAtUtc, DateTimeKind.Unspecified),
        GuestCount = 4, Status = "Confirmed", RestaurantName = "Bếp Nhà", RestaurantAddress = "45 Nguyễn Huệ, Quận 1, TP.HCM", RestaurantPhone = "0287654321"
    });

    private sealed class Booking
    {
        public long Id { get; init; }
        public string Code { get; init; } = "";
        public string? Email { get; init; }
        public string Status { get; init; } = "Confirmed";
        public DateTime StartsAtUtc { get; init; }
        public string? TableCode { get; init; }
        public string? ReminderStatus { get; set; }
        public DateTime? ReminderSentAtUtc { get; set; }
    }

    /// <summary>Hàng đợi email nhắc giả lập theo migration 059 (xếp hàng một lần / lượt, nhận email đang chờ, ghi kết quả).</summary>
    private sealed class ReminderOutbox(EmailRetryTests.Clock clock) : IBookingEmailOutbox
    {
        private readonly List<Booking> _bookings = [];
        private readonly List<EmailRetryTests.SimulatedEmail> _emails = [];

        public Booking Book(long id, string code, string? email, string status, DateTime startsAtUtc, string? table)
        {
            var booking = new Booking { Id = id, Code = code, Email = email, Status = status, StartsAtUtc = startsAtUtc, TableCode = table };
            _bookings.Add(booking);
            return booking;
        }

        public EmailRetryTests.SimulatedEmail? EmailOf(long reservationId) => _emails.SingleOrDefault(e => e.ReservationId == reservationId);

        public Task<int> QueueDueRemindersAsync(CancellationToken cancellationToken = default)
        {
            var queued = 0;
            foreach (var b in _bookings.Where(b => BookingReminderRules.IsDue(b.Status, b.Email, b.StartsAtUtc, clock.Now) && EmailOf(b.Id) is null))
            {
                _emails.Add(new EmailRetryTests.SimulatedEmail
                {
                    Id = 1000 + b.Id, ReservationId = b.Id, MessageType = BookingReminderRules.MessageType, Recipient = b.Email!,
                    PayloadJson = Payload(b.Code, b.TableCode, b.StartsAtUtc), NextAttemptAtUtc = clock.Now, CreatedAtUtc = clock.Now
                });
                b.ReminderStatus = "Pending";
                queued++;
            }
            return Task.FromResult(queued);
        }

        public Task<ClaimedEmail?> ClaimDueReminderAsync(CancellationToken cancellationToken = default)
        {
            var email = _emails.Where(e => e.Status == "Pending" && e.AttemptCount < EmailRetryPolicy.MaxAttempts && e.NextAttemptAtUtc <= clock.Now
                    && _bookings.Single(b => b.Id == e.ReservationId).StartsAtUtc > clock.Now)
                .OrderBy(e => e.Id).FirstOrDefault();
            if (email is null) return Task.FromResult<ClaimedEmail?>(null);
            email.Status = "Processing";
            email.AttemptCount++;
            email.LastAttemptAtUtc = clock.Now;
            return Task.FromResult<ClaimedEmail?>(new ClaimedEmail(email.Id, email.AttemptCount, email.Recipient, "Nhắc lịch đặt bàn",
                email.PayloadJson, email.MessageType, email.ReservationId));
        }

        public Task<ClaimedEmail?> ClaimAsync(long reservationId, string messageType, CancellationToken cancellationToken = default) =>
            Task.FromResult<ClaimedEmail?>(null);

        public Task CompleteAsync(long emailId, int attemptNumber, bool succeeded, string? error, CancellationToken cancellationToken = default)
        {
            var email = _emails.Single(e => e.Id == emailId);
            var outcome = EmailRetryPolicy.AfterAttempt(attemptNumber, succeeded, clock.Now);
            email.Status = outcome.Status;
            email.LastError = succeeded ? null : error;
            email.NextAttemptAtUtc = outcome.NextAttemptAtUtc ?? clock.Now.AddMinutes(5);
            if (succeeded) email.SentAtUtc = clock.Now;
            var booking = _bookings.Single(b => b.Id == email.ReservationId);
            booking.ReminderStatus = email.Status == "Pending" ? "Retrying" : email.Status;
            if (succeeded) booking.ReminderSentAtUtc = clock.Now;
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];
        public bool FailNext { get; set; }

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("SMTP 421: máy chủ bận");
            }
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }
}
