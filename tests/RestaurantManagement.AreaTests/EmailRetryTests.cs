using System.Net.Mail;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantManagement.Web.Models.Reservations;
using RestaurantManagement.Web.Services;
using RestaurantManagement.Web.Services.EmailVerification;

/// <summary>
/// S2-09 Task 2: tự động gửi lại email khi lần gửi đầu thất bại (không cần database).
/// Hàng đợi giả lập làm đúng như usp_ClaimDueBookingEmail / usp_CompleteEmail (theo EmailRetryPolicy) với đồng hồ giả,
/// nên "chờ 5 phút" được kiểm tra chính xác mà không phải chờ thật. Lệnh verify kiểm tra cùng các trường hợp trên SQL Server thật.
/// </summary>
internal static class EmailRetryTests
{
    private const string Payload = """{"Code":"R2T2X9","TableCode":"B03","StartsAt":"2026-10-05T12:00:00","GuestCount":2,"RestaurantName":"Bếp Nhà"}""";
    private static readonly DateTime Start = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc); // 19:00 giờ Việt Nam

    internal static async Task Run(Action<bool, string> check)
    {
        // Quy tắc: lần gửi đầu + tối đa 3 lần gửi lại, cách nhau 5 phút.
        check(EmailRetryPolicy.MaxRetries == 3 && EmailRetryPolicy.MaxAttempts == 4 && EmailRetryPolicy.RetryDelay == TimeSpan.FromMinutes(5)
            && ReservationEmailStatus.MaxAttempts == EmailRetryPolicy.MaxAttempts, "Email retry: first attempt plus at most 3 retries, 5 minutes apart");
        var afterFirst = EmailRetryPolicy.AfterAttempt(1, false, Start);
        check(afterFirst is { Status: "Pending", IsFinal: false } && afterFirst.NextAttemptAtUtc == Start.AddMinutes(5),
            "Email retry: a failed attempt is retried 5 minutes later");
        check(EmailRetryPolicy.AfterAttempt(2, true, Start) is { Status: "Sent", IsFinal: true, NextAttemptAtUtc: null }
            && EmailRetryPolicy.AfterAttempt(3, false, Start) is { Status: "Pending", IsFinal: false }
            && EmailRetryPolicy.AfterAttempt(4, false, Start) is { Status: "Failed", IsFinal: true, NextAttemptAtUtc: null },
            "Email retry: success or the third failed retry is the final result");
        check(!EmailRetryPolicy.IsDueForRetry("Pending", 0, Start, Start) && EmailRetryPolicy.IsDueForRetry("Pending", 1, Start, Start)
            && !EmailRetryPolicy.IsDueForRetry("Pending", 1, Start, Start.AddSeconds(-1)) && EmailRetryPolicy.IsDueForRetry("Pending", 3, Start, Start)
            && !EmailRetryPolicy.IsDueForRetry("Pending", 4, Start, Start) && !EmailRetryPolicy.IsDueForRetry("Failed", 4, Start, Start)
            && !EmailRetryPolicy.IsDueForRetry("Sent", 2, Start, Start) && !EmailRetryPolicy.IsDueForRetry("Processing", 2, Start, Start),
            "Email retry: only failed emails with retries left are due, and only once their time has come");
        check(EmailRetryPolicy.AttemptLabel(1) == "Lần gửi đầu" && EmailRetryPolicy.AttemptLabel(2) == "Lần gửi lại 1" && EmailRetryPolicy.AttemptLabel(4) == "Lần gửi lại 3",
            "Email retry: attempts are labelled first send / retry 1–3");

        // 1) Lần gửi đầu thất bại → gửi lại sau 5 phút → thành công ở lần thử thứ 2.
        var clock = new Clock(Start);
        var outbox = new SimulatedOutbox(clock);
        var sender = new ScriptedSender(failures: 1);
        var dispatcher = Dispatcher(outbox, sender);
        outbox.Queue(1, 500);
        var first = await dispatcher.SendBookingReceivedAsync(500);
        var email = outbox.Email(1);
        check(first.Outcome == BookingEmailOutcome.Failed && email is { Status: "Pending", AttemptCount: 1 } && email.NextAttemptAtUtc == Start.AddMinutes(5),
            "Email retry: the failed first send is kept for a retry in 5 minutes");
        var retryingStatus = BookingEmailCustomerStatus.From(outbox.Record(1), "B03", "k***h@example.com")!;
        check(retryingStatus is { State: EmailDeliveryState.Retrying, IsFinal: false } && retryingStatus.Message.Contains("Email xác nhận chưa gửi được")
            && retryingStatus.Message.Contains("tự gửi lại lúc 19:05") && retryingStatus.Message.Contains("đã thử 1/4 lần") && retryingStatus.Message.Contains("mã đặt bàn B03"),
            "Email retry: the customer sees that the email will be sent again automatically");
        clock.Now = Start.AddMinutes(5).AddSeconds(-1);
        check(await dispatcher.RetryDueAsync() == 0 && sender.Calls == 1, "Email retry: nothing is sent again before 5 minutes");
        clock.Now = Start.AddMinutes(5);
        check(await dispatcher.RetryDueAsync() == 1 && sender.Calls == 2, "Email retry: the email is sent again after the first send failed");
        check(email is { Status: "Sent", AttemptCount: 2, LastError: null } && email.Attempts.Select(a => a.Status).SequenceEqual(new[] { "Failed", "Succeeded" })
            && email.Attempts[0].Error!.Contains("421") && email.Attempts[1].StartedAtUtc - email.Attempts[0].StartedAtUtc == TimeSpan.FromMinutes(5),
            "Email retry: success on attempt 2 is stored as the final result with each attempt's result");
        clock.Now = Start.AddHours(2);
        check(await dispatcher.RetryDueAsync() == 0 && sender.Calls == 2, "Email retry: a sent email is never sent again");

        var sentItem = ReservationEmailStatus.ToItem(outbox.Record(1));
        check(sentItem is { State: EmailDeliveryState.Sent, AttemptCount: 2, RetryCount: 1, IsFinal: true, ErrorMessage: null }
            && sentItem.FinalResult.EndsWith("(ở lần thử thứ 2)") && sentItem.Attempts.Count == 2
            && sentItem.Attempts[0] is { Label: "Lần gửi đầu", ResultLabel: "Thất bại" } && sentItem.Attempts[0].ErrorMessage!.Contains("421")
            && sentItem.Attempts[1] is { Label: "Lần gửi lại 1", ResultLabel: "Thành công", ErrorMessage: null },
            "Email retry: staff see 2 attempts, the first error and the final result 'sent at attempt 2'");
        var sentStatus = BookingEmailCustomerStatus.From(outbox.Record(1), "B03", "k***h@example.com")!;
        check(sentStatus is { State: EmailDeliveryState.Sent, IsFinal: true } && sentStatus.Message.Contains("đã được gửi tới k***h@example.com")
            && sentStatus.Message.Contains("lần thử thứ 2"), "Email retry: the customer sees the email was finally sent");

        // 2) Cả 3 lần gửi lại đều thất bại → kết quả cuối cùng là thất bại, không thử lần thứ 5.
        clock.Now = Start;
        var failing = new ScriptedSender(failures: int.MaxValue);
        dispatcher = Dispatcher(outbox, failing);
        outbox.Queue(2, 600);
        await dispatcher.SendBookingReceivedAsync(600);
        var lost = outbox.Email(2);
        for (var retry = 1; retry <= EmailRetryPolicy.MaxRetries; retry++)
        {
            clock.Now = lost.NextAttemptAtUtc.AddSeconds(-1);
            var early = await dispatcher.RetryDueAsync();
            clock.Now = lost.NextAttemptAtUtc;
            check(early == 0 && await dispatcher.RetryDueAsync() == 1 && lost.AttemptCount == retry + 1,
                $"Email retry: retry {retry} of 3 runs exactly 5 minutes after the previous attempt");
        }
        var gaps = lost.Attempts.Zip(lost.Attempts.Skip(1), (a, b) => b.StartedAtUtc - a.StartedAtUtc).ToArray();
        check(gaps.Length == 3 && gaps.All(g => g == TimeSpan.FromMinutes(5)), "Email retry: attempts are 5 minutes apart");
        check(lost is { Status: "Failed", AttemptCount: 4 } && lost.Attempts.Count == 4 && lost.Attempts.All(a => a.Status == "Failed" && a.CompletedAtUtc is not null),
            "Email retry: all 3 retries failed and the final result 'failed' is recorded");
        clock.Now = Start.AddDays(1);
        check(await dispatcher.RetryDueAsync() == 0 && failing.Calls == 4, "Email retry: never more than 3 retries (4 attempts in total)");

        var failedItem = ReservationEmailStatus.ToItem(outbox.Record(2));
        check(failedItem is { State: EmailDeliveryState.Failed, AttemptCount: 4, MaxAttempts: 4, RetryCount: 3, MaxRetries: 3, IsFinal: true }
            && failedItem.FinalResult == "Thất bại sau 4 lần thử (lần gửi đầu và 3 lần gửi lại)"
            && failedItem.Attempts.Select(a => a.Label).SequenceEqual(new[] { "Lần gửi đầu", "Lần gửi lại 1", "Lần gửi lại 2", "Lần gửi lại 3" })
            && failedItem.Attempts.All(a => a.ResultLabel == "Thất bại" && a.ErrorMessage is not null),
            "Email retry: staff see the right attempt count, every attempt and the final result");
        var failedStatus = BookingEmailCustomerStatus.From(outbox.Record(2), "B03", "k***h@example.com")!;
        check(failedStatus is { State: EmailDeliveryState.Failed, IsFinal: true } && failedStatus.Message.Contains("Không gửi được email xác nhận")
            && failedStatus.Message.Contains("sau 4 lần thử (lần gửi đầu và 3 lần gửi lại)") && failedStatus.Message.Contains("mã đặt bàn B03"),
            "Email retry: the customer sees the final result when the email could not be sent");

        // 3) Email chưa thử lần nào (đang chờ lần gửi đầu) và loại email khác không thuộc việc gửi lại.
        clock.Now = Start.AddDays(2);
        outbox.Queue(3, 700);
        outbox.Queue(4, 800, "BookingReminder").AttemptCount = 1;
        check(await dispatcher.RetryDueAsync() == 0, "Email retry: the worker only retries booking emails whose first send failed");

        // Trạng thái email cập nhật trên lượt đặt bàn.
        string? Label(string? status, int attempts) => new ReservationListItemViewModel { ConfirmationEmailStatus = status, ConfirmationEmailAttempts = attempts }.ConfirmationEmailLabel;
        check(Label("Retrying", 2) == "Email: chưa gửi được, sẽ tự gửi lại (đã thử 2/4)" && Label("Sent", 2) == "Email: đã gửi (ở lần thử thứ 2)"
            && Label("Sent", 1) == "Email: đã gửi" && Label("Failed", 4) == "Email: gửi thất bại sau 4 lần thử" && Label(null, 0) is null,
            "Email retry: the reservation shows the email status after every attempt");

        // Trang khách chỉ đọc lượt đặt bàn của chính trình duyệt (Id lấy từ TempData, không nhận từ URL).
        var statusAction = typeof(RestaurantManagement.Web.Controllers.ReservationsController).GetMethod("BookingEmailStatus")!;
        check(statusAction.GetParameters().All(p => p.ParameterType != typeof(long) && p.ParameterType != typeof(long?)),
            "Email retry: the customer status endpoint takes no reservation id from the request");
    }

    private static BookingEmailDispatcher Dispatcher(IBookingEmailOutbox outbox, IEmailSender sender) =>
        new(outbox, sender, NullLogger<BookingEmailDispatcher>.Instance);

    private sealed class Clock(DateTime now)
    {
        public DateTime Now { get; set; } = now;
    }

    private sealed class SimulatedEmail
    {
        public long Id { get; init; }
        public long ReservationId { get; init; }
        public string MessageType { get; init; } = "BookingReceived";
        public string Status { get; set; } = "Pending";
        public int AttemptCount { get; set; }
        public DateTime NextAttemptAtUtc { get; set; }
        public DateTime? LastAttemptAtUtc { get; set; }
        public DateTime? SentAtUtc { get; set; }
        public string? LastError { get; set; }
        public DateTime CreatedAtUtc { get; init; }
        public List<EmailAttemptRecord> Attempts { get; } = [];
    }

    /// <summary>Hàng đợi giả lập dbo.EmailOutbox + dbo.EmailAttempts theo đúng quy tắc của migration 030.</summary>
    private sealed class SimulatedOutbox(Clock clock) : IBookingEmailOutbox
    {
        private readonly List<SimulatedEmail> _emails = [];

        public SimulatedEmail Queue(long id, long reservationId, string messageType = "BookingReceived")
        {
            var email = new SimulatedEmail { Id = id, ReservationId = reservationId, MessageType = messageType, NextAttemptAtUtc = clock.Now, CreatedAtUtc = clock.Now };
            _emails.Add(email);
            return email;
        }

        public SimulatedEmail Email(long id) => _emails.Single(e => e.Id == id);

        public Task<ClaimedEmail?> ClaimAsync(long reservationId, string messageType, CancellationToken cancellationToken = default) =>
            Task.FromResult(StartAttempt(_emails.FirstOrDefault(e => e.ReservationId == reservationId && e.MessageType == messageType
                && e.Status == "Pending" && e.AttemptCount < EmailRetryPolicy.MaxAttempts)));

        public Task<ClaimedEmail?> ClaimDueRetryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(StartAttempt(_emails
                .Where(e => EmailRetryPolicy.RetryableMessageTypes.Contains(e.MessageType)
                    && EmailRetryPolicy.IsDueForRetry(e.Status, e.AttemptCount, e.NextAttemptAtUtc, clock.Now))
                .OrderBy(e => e.NextAttemptAtUtc).FirstOrDefault()));

        private ClaimedEmail? StartAttempt(SimulatedEmail? email)
        {
            if (email is null) return null;
            email.Status = "Processing";
            email.AttemptCount++;
            email.LastAttemptAtUtc = clock.Now;
            email.Attempts.Add(new EmailAttemptRecord(email.AttemptCount, "Sending", clock.Now, null, null));
            return new ClaimedEmail(email.Id, email.AttemptCount, "khach@example.com", "Xác nhận đặt bàn B03", Payload, email.MessageType, email.ReservationId);
        }

        public Task CompleteAsync(long emailId, int attemptNumber, bool succeeded, string? error, CancellationToken cancellationToken = default)
        {
            var email = Email(emailId);
            if (email.Status != "Processing" || email.AttemptCount != attemptNumber) return Task.CompletedTask;
            var outcome = EmailRetryPolicy.AfterAttempt(attemptNumber, succeeded, clock.Now);
            email.Status = outcome.Status;
            email.NextAttemptAtUtc = outcome.NextAttemptAtUtc ?? clock.Now + EmailRetryPolicy.RetryDelay;
            email.SentAtUtc = succeeded ? clock.Now : null;
            email.LastError = succeeded ? null : error;
            email.Attempts[^1] = email.Attempts[^1] with
            {
                Status = succeeded ? "Succeeded" : "Failed", CompletedAtUtc = clock.Now, Error = succeeded ? null : error
            };
            return Task.CompletedTask;
        }

        public ReservationEmailRecord Record(long id)
        {
            var e = Email(id);
            return new ReservationEmailRecord(e.Id, e.ReservationId, e.MessageType, "khach@example.com", e.Status, e.AttemptCount,
                e.LastAttemptAtUtc, e.NextAttemptAtUtc, e.SentAtUtc, e.LastError, e.CreatedAtUtc, e.Attempts.ToArray());
        }
    }

    /// <summary>Máy chủ email giả: lỗi ở <c>failures</c> lần gửi đầu tiên, sau đó gửi được.</summary>
    private sealed class ScriptedSender(int failures) : IEmailSender
    {
        public int Calls { get; private set; }

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Calls <= failures) throw new SmtpException("421 4.3.2 Service not available");
            return Task.CompletedTask;
        }
    }
}
