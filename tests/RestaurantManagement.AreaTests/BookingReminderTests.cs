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
        SkipRules(check);
        await SkipInvalid(check);
        await OneReminderOnly(check);
    }

    /// <summary>S4-08 Task 3: mỗi lượt đặt bàn chỉ nhận tối đa một email nhắc.</summary>
    private static async Task OneReminderOnly(Action<bool, string> check)
    {
        // Chạy tác vụ nhiều lần liên tiếp.
        {
            var clock = new EmailRetryTests.Clock(Now);
            var outbox = new ReminderOutbox(clock);
            var sender = new CapturingSender();
            var dispatcher = new BookingEmailDispatcher(outbox, sender, NullLogger<BookingEmailDispatcher>.Instance);
            var booking = outbox.Book(21, "ON0001", "once@example.com", "Confirmed", Now.AddMinutes(110), "B01");
            var runs = new List<BookingReminderRunResult>();
            for (var i = 0; i < 6; i++)
            {
                runs.Add(await dispatcher.SendDueRemindersAsync());
                clock.Now = clock.Now.AddMinutes(1);
            }
            check(sender.Sent.Count(m => m.To == booking.Email) == 1 && runs.Sum(r => r.Sent) == 1 && runs.Sum(r => r.Queued) == 1
                  && outbox.EmailCount(booking.Id) == 1,
                "One reminder: running the task 6 times in a row sends exactly one reminder");
            check(booking.ReminderSentAtUtc == Now && booking.ReminderStatus == "Sent",
                "One reminder: the booking is marked as reminded at the successful send");
            // Dù hàng đợi lỡ có email nhắc thứ hai (dữ liệu bất thường), lượt đã nhắc không bị gửi lại.
            outbox.ForceSecondReminder(booking.Id);
            var forced = await dispatcher.SendDueRemindersAsync();
            check(forced.Sent == 0 && sender.Sent.Count(m => m.To == booking.Email) == 1,
                "One reminder: a booking already reminded successfully is never sent again");
        }

        // Hai tác vụ chạy đồng thời.
        {
            var clock = new EmailRetryTests.Clock(Now);
            var outbox = new ReminderOutbox(clock);
            var sender = new CapturingSender { Delay = TimeSpan.FromMilliseconds(30) };
            var first = new BookingEmailDispatcher(outbox, sender, NullLogger<BookingEmailDispatcher>.Instance);
            var second = new BookingEmailDispatcher(outbox, sender, NullLogger<BookingEmailDispatcher>.Instance);
            var bookings = Enumerable.Range(0, 5)
                .Select(i => outbox.Book(30 + i, $"PAR00{i}", $"par{i}@example.com", "Confirmed", Now.AddMinutes(90 + i), $"C0{i}")).ToArray();
            var results = await Task.WhenAll(
                Task.Run(() => first.SendDueRemindersAsync()), Task.Run(() => second.SendDueRemindersAsync()),
                Task.Run(() => first.SendDueRemindersAsync()), Task.Run(() => second.SendDueRemindersAsync()));
            check(bookings.All(b => sender.Sent.Count(m => m.To == b.Email) == 1) && sender.Sent.Count == bookings.Length
                  && results.Sum(r => r.Sent) == bookings.Length && bookings.All(b => outbox.EmailCount(b.Id) == 1),
                "One reminder: four task runs at the same time send each booking exactly one reminder");
        }

        // Khoá hết hạn: tác vụ khác đã nhận lại email -> tác vụ cũ không được gửi.
        {
            var clock = new EmailRetryTests.Clock(Now);
            var outbox = new ReminderOutbox(clock);
            var booking = outbox.Book(41, "STALE1", "stale@example.com", "Confirmed", Now.AddMinutes(100), "D01");
            await outbox.QueueDueRemindersAsync();
            var claimed = (await outbox.ClaimDueReminderAsync())!;
            outbox.ExpireAndReclaim(claimed.Id);
            var reason = await outbox.CheckReminderBeforeSendAsync(claimed.Id, claimed.AttemptNumber);
            check(reason == BookingReminderRules.NotClaimed && BookingReminderRules.IsDuplicateGuard(reason)
                  && BookingReminderRules.SkipReasonLabel(reason) == "Email đang do tác vụ khác xử lý",
                "One reminder: a task whose claim was taken over by another task does not send");
            outbox.MarkSent(booking.Id);
            check(await outbox.CheckReminderBeforeSendAsync(claimed.Id, claimed.AttemptNumber + 1) == BookingReminderRules.AlreadySent,
                "One reminder: a booking already reminded is detected right before sending");
        }

        // Gửi thất bại: được thử lại; gửi thành công rồi thì không gửi lại.
        {
            var clock = new EmailRetryTests.Clock(Now);
            var outbox = new ReminderOutbox(clock);
            var sender = new CapturingSender { FailNext = true };
            var dispatcher = new BookingEmailDispatcher(outbox, sender, NullLogger<BookingEmailDispatcher>.Instance);
            var booking = outbox.Book(51, "RETRY1", "retry@example.com", "Confirmed", Now.AddMinutes(100), "E01");
            var failed = await dispatcher.SendDueRemindersAsync();
            check(failed is { Sent: 0, Failed: 1 } && booking.ReminderSentAtUtc is null && booking.ReminderStatus == "Retrying"
                  && outbox.EmailOf(booking.Id) is { Status: "Pending", AttemptCount: 1 },
                "One reminder: a failed send does not mark the booking as reminded");
            var tooSoon = await dispatcher.SendDueRemindersAsync();
            check(tooSoon.Sent == 0 && sender.Sent.Count == 0, "One reminder: a failed reminder waits 5 minutes before retrying");
            clock.Now = Now.AddMinutes(5);
            var retried = await dispatcher.SendDueRemindersAsync();
            check(retried.Sent == 1 && sender.Sent.Count == 1 && booking.ReminderSentAtUtc == Now.AddMinutes(5)
                  && outbox.EmailOf(booking.Id) is { Status: "Sent", AttemptCount: 2 },
                "One reminder: a failed reminder can be retried and then succeeds");
            for (var i = 0; i < 3; i++) { clock.Now = clock.Now.AddMinutes(6); await dispatcher.SendDueRemindersAsync(); }
            check(sender.Sent.Count == 1, "One reminder: after a successful send the booking is not sent again");

            var list = BookingReminderRules.BuildList(outbox.Records(), BookingReminderFilter.All, 120);
            var row = list.Items.Single(i => i.ReservationId == booking.Id);
            check(row is { AttemptCount: 2, MaxAttempts: 4, FinalResult: "Gửi thành công ở lần thử 2", State: EmailDeliveryState.Sent },
                "Reminder list: shows the number of attempts and the final result");
        }
        check(BookingReminderRules.FinalResult(EmailDeliveryState.Failed, 4, false) == "Thất bại sau 4 lần thử"
              && BookingReminderRules.FinalResult(EmailDeliveryState.Retrying, 2, false) == "Lỗi, sẽ tự gửi lại (đã thử 2/4)"
              && BookingReminderRules.FinalResult(EmailDeliveryState.Waiting, 0, false) == "Chưa gửi"
              && BookingReminderRules.FinalResult(EmailDeliveryState.Cancelled, 0, true) == "Đã bỏ qua, không gửi",
            "Reminder list: final result label for each delivery state");
        check(new BookingReminderRunResult(0, 1, 0, 0, 2).Summary.Contains("chặn 2 lần gửi trùng"),
            "Reminder task: run summary reports blocked duplicate sends");
        var model = new ReservationListItemViewModel { Status = "Confirmed", ReminderEmailAttempts = 2, ReminderEmailSentAt = new DateTime(2026, 10, 17, 10, 0, 0) };
        check(model.ReminderAlreadySent && model.ReminderEmailAttempts == 2,
            "Reservation details: shows the reminder attempts and that the booking was already reminded");
    }

    /// <summary>S4-08 Task 2: lý do bỏ qua theo trạng thái lượt đặt bàn tại thời điểm gửi.</summary>
    private static void SkipRules(Action<bool, string> check)
    {
        var start = Now.AddHours(1);
        check(BookingReminderRules.SkipReasonFor("Confirmed", start, Now) is null,
            "Skip rule: a booking still confirmed is sent its reminder");
        check(BookingReminderRules.SkipReasonFor("Cancelled", start, Now) == "Cancelled" && BookingReminderRules.SkipReasonFor(null, start, Now) == "Cancelled",
            "Skip rule: a cancelled (or deleted) booking is skipped as cancelled");
        check(BookingReminderRules.SkipReasonFor("Arrived", start, Now) == "Arrived",
            "Skip rule: a booking whose guest has arrived is skipped");
        check(BookingReminderRules.SkipReasonFor("Rejected", start, Now) == "Rejected" && BookingReminderRules.SkipReasonFor("NoShow", start, Now) == "NoShow"
              && BookingReminderRules.SkipReasonFor("Pending", start, Now) == "NotConfirmed" && BookingReminderRules.SkipReasonFor("Confirmed", Now, Now) == "StartPassed",
            "Skip rule: rejected, no-show, unconfirmed and past bookings are skipped with their own reason");
        check(BookingReminderRules.SkipReasonLabel("Cancelled") == "Lượt đặt bàn đã huỷ" && BookingReminderRules.SkipReasonLabel("Arrived") == "Khách đã tới"
              && BookingReminderRules.SkipReasonLabel(null) is null,
            "Skip rule: reasons are shown in Vietnamese");
        check(BookingReminderRules.ReservationStatusCss("Cancelled") == "reservation-state-cancelled"
              && BookingReminderRules.ReservationStatusCss("Arrived") == "reservation-state-arrived"
              && BookingReminderRules.ReservationStatusCss("Confirmed") == "reservation-state-confirmed"
              && ReservationStatusDisplay.Label("Arrived") == "Khách đã tới" && ReservationStatusDisplay.Label("Cancelled") == "Đã huỷ"
              && ReservationStatusDisplay.Label("Confirmed") == "Đã xác nhận",
            "Reservation details: cancelled, confirmed and arrived statuses have their own label and style");
        var start19 = new DateTime(2026, 10, 17, 19, 0, 0);
        var at10 = new DateTime(2026, 10, 17, 10, 0, 0);
        check(BookingReminderRules.DetailLabel("Cancelled", "Arrived", "a@example.com", start19, at10, "Arrived") == "Đã bỏ qua gửi nhắc: Khách đã tới"
              && BookingReminderRules.DetailLabel("Cancelled", "Cancelled", "a@example.com", start19, at10, "Cancelled") == "Đã bỏ qua gửi nhắc: Lượt đặt bàn đã huỷ"
              && BookingReminderRules.DetailLabel(null, "Cancelled", "a@example.com", start19, at10) == "Không gửi nhắc: Lượt đặt bàn đã huỷ",
            "Reservation details: the reminder status explains why the reminder was skipped");
    }

    /// <summary>S4-08 Task 2: tác vụ nhắc lịch bỏ qua lượt đã huỷ / khách đã tới, kể cả khi đổi trạng thái ngay trước lúc gửi.</summary>
    private static async Task SkipInvalid(Action<bool, string> check)
    {
        var clock = new EmailRetryTests.Clock(Now);
        var outbox = new ReminderOutbox(clock);
        var sender = new CapturingSender();
        var dispatcher = new BookingEmailDispatcher(outbox, sender, NullLogger<BookingEmailDispatcher>.Instance);

        var cancelled = outbox.Book(11, "SK0001", "cancelled@example.com", "Confirmed", Now.AddMinutes(100), "A01");
        var arrived = outbox.Book(12, "SK0002", "arrived@example.com", "Confirmed", Now.AddMinutes(100), "A02");
        var confirmed = outbox.Book(13, "SK0003", "confirmed@example.com", "Confirmed", Now.AddMinutes(100), "A03");
        var lastMinute = outbox.Book(14, "SK0004", "lastminute@example.com", "Confirmed", Now.AddMinutes(100), "A04");

        // Email nhắc đã xếp hàng, rồi lượt đổi trạng thái trước khi tác vụ gửi.
        await outbox.QueueDueRemindersAsync();
        cancelled.Status = "Cancelled";
        arrived.Status = "Arrived";
        // Lượt này đổi trạng thái sau khi web đã nhận email nhưng ngay trước lúc gửi.
        outbox.BeforeSend = claimed => { if (claimed.ReservationId == lastMinute.Id) lastMinute.Status = "Cancelled"; };

        var run = await dispatcher.SendDueRemindersAsync();
        check(sender.Sent.All(m => m.To != cancelled.Email!) && cancelled.ReminderStatus == "Cancelled" && cancelled.ReminderSkipReason == "Cancelled"
              && cancelled.ReminderSentAtUtc is null,
            "Skip invalid: a cancelled booking gets no reminder email");
        check(sender.Sent.All(m => m.To != arrived.Email!) && arrived.ReminderStatus == "Cancelled" && arrived.ReminderSkipReason == "Arrived",
            "Skip invalid: a booking whose guest has arrived gets no reminder email");
        check(sender.Sent.Count(m => m.To == confirmed.Email!) == 1 && confirmed.ReminderStatus == "Sent" && confirmed.ReminderSkipReason is null,
            "Skip invalid: a booking still confirmed gets its reminder email");
        check(sender.Sent.All(m => m.To != lastMinute.Email!) && lastMinute.ReminderSkipReason == "Cancelled"
              && outbox.EmailOf(lastMinute.Id) is { Status: "Cancelled", AttemptCount: 0 },
            "Skip invalid: a booking that changes status right before sending gets no email (no send attempt counted)");
        check(run is { Sent: 1, Failed: 0, Skipped: 1 } && sender.Sent.Count == 1 && run.Summary.Contains("bỏ qua 1"),
            "Skip invalid: run summary counts the reminder skipped right before sending");

        // Chạy lại tác vụ: không có email mới nào được gửi.
        var again = await dispatcher.SendDueRemindersAsync();
        check(again is { Queued: 0, Sent: 0, Skipped: 0 } && sender.Sent.Count == 1,
            "Skip invalid: running the task again sends no new email");

        // Danh sách theo dõi: lý do bỏ qua đúng từng lượt.
        var list = BookingReminderRules.BuildList(outbox.Records(), BookingReminderFilter.All, 120);
        BookingReminderItem Row(Booking b) => list.Items.Single(i => i.ReservationId == b.Id);
        check(Row(cancelled) is { IsSkipped: true, SkipReasonLabel: "Lượt đặt bàn đã huỷ", ReservationStatusLabel: "Đã huỷ", SentAt: null }
              && Row(arrived) is { IsSkipped: true, SkipReasonLabel: "Khách đã tới", ReservationStatusLabel: "Khách đã tới" }
              && Row(lastMinute) is { IsSkipped: true, SkipReasonLabel: "Lượt đặt bàn đã huỷ" }
              && Row(confirmed) is { IsSkipped: false, SkipReasonLabel: null, State: EmailDeliveryState.Sent, ReservationStatusLabel: "Đã xác nhận" },
            "Reminder list: shows the right skip reason (or none) for each booking");
        var skippedOnly = BookingReminderRules.BuildList(outbox.Records(), BookingReminderFilter.Skipped, 120);
        check(skippedOnly.Items.Count == 3 && skippedOnly.Items.All(i => i.IsSkipped) && skippedOnly.Filter == "skipped" && list.SkippedCount == 3,
            "Reminder list: 'skipped' filter lists only skipped reminders");
        check(BookingReminderFilter.Normalize("SKIPPED") == "all" && BookingReminderFilter.Normalize(null) == "all" && BookingReminderFilter.Normalize("sent") == "sent",
            "Reminder list: unknown filters fall back to all");
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
        public string Status { get; set; } = "Confirmed";
        public DateTime StartsAtUtc { get; init; }
        public string? TableCode { get; init; }
        public string? ReminderStatus { get; set; }
        public DateTime? ReminderSentAtUtc { get; set; }
        public string? ReminderSkipReason { get; set; }
    }

    /// <summary>Hàng đợi email nhắc giả lập theo migration 059 (xếp hàng một lần / lượt, nhận email đang chờ, ghi kết quả).</summary>
    private sealed class ReminderOutbox(EmailRetryTests.Clock clock) : IBookingEmailOutbox
    {
        private readonly List<Booking> _bookings = [];
        private readonly List<EmailRetryTests.SimulatedEmail> _emails = [];
        private readonly Dictionary<long, string> _skipReasons = new();
        // Giống khoá usp_LockOperations: mọi thao tác trên hàng đợi chạy lần lượt, kể cả khi nhiều tác vụ chạy đồng thời.
        private readonly object _gate = new();

        public int EmailCount(long reservationId) { lock (_gate) return _emails.Count(e => e.ReservationId == reservationId); }

        /// <summary>Giả lập dữ liệu bất thường: thêm một email nhắc thứ hai đang chờ cho lượt đã nhắc.</summary>
        public void ForceSecondReminder(long reservationId)
        {
            lock (_gate)
            {
                var b = _bookings.Single(x => x.Id == reservationId);
                _emails.Add(new EmailRetryTests.SimulatedEmail
                {
                    Id = 5000 + b.Id, ReservationId = b.Id, MessageType = BookingReminderRules.MessageType, Recipient = b.Email!,
                    PayloadJson = Payload(b.Code, b.TableCode, b.StartsAtUtc), NextAttemptAtUtc = clock.Now, CreatedAtUtc = clock.Now
                });
            }
        }

        /// <summary>Giả lập khoá hết hạn: email được trả về hàng đợi rồi một tác vụ khác nhận lại (lần thử mới).</summary>
        public void ExpireAndReclaim(long emailId)
        {
            lock (_gate)
            {
                var e = _emails.Single(x => x.Id == emailId);
                e.Status = "Processing";
                e.AttemptCount++;
            }
        }

        public void MarkSent(long reservationId) { lock (_gate) _bookings.Single(b => b.Id == reservationId).ReminderSentAtUtc = clock.Now; }

        /// <summary>Chạy ngay trước bước kiểm tra lại (giả lập lượt đổi trạng thái sát giờ gửi).</summary>
        public Action<ClaimedEmail>? BeforeSend { get; set; }

        private Booking BookingOf(EmailRetryTests.SimulatedEmail e) => _bookings.Single(b => b.Id == e.ReservationId);

        private void Skip(EmailRetryTests.SimulatedEmail email, string reason)
        {
            email.Status = "Cancelled";
            _skipReasons[email.Id] = reason;
            var booking = BookingOf(email);
            booking.ReminderStatus = "Cancelled";
            booking.ReminderSkipReason = reason;
        }

        public IReadOnlyList<BookingReminderRecord> Records() => _emails.Select(e =>
        {
            var b = BookingOf(e);
            return new BookingReminderRecord(e.Id, b.Id, b.Code, "Khách", b.StartsAtUtc, b.TableCode, e.Recipient, e.Status, e.AttemptCount,
                e.SentAtUtc, e.LastError, e.CreatedAtUtc, _skipReasons.GetValueOrDefault(e.Id), b.Status);
        }).ToArray();

        public Task<string?> CheckReminderBeforeSendAsync(long emailId, int attemptNumber, CancellationToken cancellationToken = default)
        {
            lock (_gate) return Task.FromResult(CheckCore(emailId, attemptNumber));
        }

        private string? CheckCore(long emailId, int attemptNumber)
        {
            var email = _emails.Single(e => e.Id == emailId);
            BeforeSend?.Invoke(new ClaimedEmail(email.Id, attemptNumber, email.Recipient, "", email.PayloadJson, email.MessageType, email.ReservationId));
            // Migration 061: email không còn do tác vụ này giữ, hoặc lượt đã được nhắc thành công -> không gửi.
            if (email.Status != "Processing" || email.AttemptCount != attemptNumber) return BookingReminderRules.NotClaimed;
            if (BookingOf(email).ReminderSentAtUtc is not null)
            {
                email.Status = "Cancelled";
                email.AttemptCount--;
                return BookingReminderRules.AlreadySent;
            }
            var reason = BookingReminderRules.SkipReasonFor(BookingOf(email).Status, BookingOf(email).StartsAtUtc, clock.Now);
            if (reason is not null && email.Status == "Processing" && email.AttemptCount == attemptNumber)
            {
                email.AttemptCount--;
                Skip(email, reason);
            }
            return reason;
        }

        public Booking Book(long id, string code, string? email, string status, DateTime startsAtUtc, string? table)
        {
            var booking = new Booking { Id = id, Code = code, Email = email, Status = status, StartsAtUtc = startsAtUtc, TableCode = table };
            _bookings.Add(booking);
            return booking;
        }

        public EmailRetryTests.SimulatedEmail? EmailOf(long reservationId) => _emails.SingleOrDefault(e => e.ReservationId == reservationId);

        public Task<int> QueueDueRemindersAsync(CancellationToken cancellationToken = default)
        {
            lock (_gate) return Task.FromResult(QueueCore());
        }

        private int QueueCore()
        {
            var queued = 0;
            foreach (var b in _bookings.Where(b => BookingReminderRules.NeedsReminder(b.Status, b.Email, b.StartsAtUtc, clock.Now,
                         b.ReminderSentAtUtc, _emails.Any(e => e.ReservationId == b.Id))).ToArray())
            {
                _emails.Add(new EmailRetryTests.SimulatedEmail
                {
                    Id = 1000 + b.Id, ReservationId = b.Id, MessageType = BookingReminderRules.MessageType, Recipient = b.Email!,
                    PayloadJson = Payload(b.Code, b.TableCode, b.StartsAtUtc), NextAttemptAtUtc = clock.Now, CreatedAtUtc = clock.Now
                });
                b.ReminderStatus = "Pending";
                queued++;
            }
            return queued;
        }

        public Task<ClaimedEmail?> ClaimDueReminderAsync(CancellationToken cancellationToken = default)
        {
            lock (_gate) return Task.FromResult(ClaimCore());
        }

        private ClaimedEmail? ClaimCore()
        {
            // Migration 061: email chờ của lượt đã nhắc thành công bị huỷ, không bao giờ gửi lần hai.
            foreach (var extra in _emails.Where(e => e.Status == "Pending" && BookingOf(e).ReminderSentAtUtc is not null).ToArray())
                extra.Status = "Cancelled";
            // Migration 060: bỏ qua email nhắc của lượt không còn hợp lệ trước khi nhận email để gửi.
            foreach (var pending in _emails.Where(e => e.Status == "Pending").ToArray())
                if (BookingReminderRules.SkipReasonFor(BookingOf(pending).Status, BookingOf(pending).StartsAtUtc, clock.Now) is { } reason)
                    Skip(pending, reason);
            var email = _emails.Where(e => e.Status == "Pending" && e.AttemptCount < EmailRetryPolicy.MaxAttempts && e.NextAttemptAtUtc <= clock.Now
                    && BookingOf(e).Status == "Confirmed" && BookingOf(e).StartsAtUtc > clock.Now && BookingOf(e).ReminderSentAtUtc is null)
                .OrderBy(e => e.Id).FirstOrDefault();
            if (email is null) return null;
            email.Status = "Processing";
            email.AttemptCount++;
            email.LastAttemptAtUtc = clock.Now;
            return new ClaimedEmail(email.Id, email.AttemptCount, email.Recipient, "Nhắc lịch đặt bàn",
                email.PayloadJson, email.MessageType, email.ReservationId);
        }

        public Task<ClaimedEmail?> ClaimAsync(long reservationId, string messageType, CancellationToken cancellationToken = default) =>
            Task.FromResult<ClaimedEmail?>(null);

        public Task CompleteAsync(long emailId, int attemptNumber, bool succeeded, string? error, CancellationToken cancellationToken = default)
        {
            lock (_gate) CompleteCore(emailId, attemptNumber, succeeded, error);
            return Task.CompletedTask;
        }

        private void CompleteCore(long emailId, int attemptNumber, bool succeeded, string? error)
        {
            var email = _emails.Single(e => e.Id == emailId);
            // usp_CompleteEmail chỉ ghi kết quả cho đúng lần thử đang Processing.
            if (email.Status != "Processing" || email.AttemptCount != attemptNumber) return;
            var outcome = EmailRetryPolicy.AfterAttempt(attemptNumber, succeeded, clock.Now);
            email.Status = outcome.Status;
            email.LastError = succeeded ? null : error;
            email.NextAttemptAtUtc = outcome.NextAttemptAtUtc ?? clock.Now.AddMinutes(5);
            if (succeeded) email.SentAtUtc = clock.Now;
            var booking = _bookings.Single(b => b.Id == email.ReservationId);
            booking.ReminderStatus = email.Status == "Pending" ? "Retrying" : email.Status;
            if (succeeded && booking.ReminderSentAtUtc is null) booking.ReminderSentAtUtc = clock.Now;
        }
    }

    private sealed class CapturingSender : IEmailSender
    {
        private readonly object _gate = new();
        private readonly List<EmailMessage> _sent = [];
        public bool FailNext { get; set; }
        /// <summary>Giả lập máy chủ email chậm để hai tác vụ chạy chồng lên nhau.</summary>
        public TimeSpan Delay { get; init; }

        public List<EmailMessage> Sent { get { lock (_gate) return [.. _sent]; } }

        public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            if (Delay > TimeSpan.Zero) await Task.Delay(Delay, cancellationToken);
            lock (_gate)
            {
                if (FailNext)
                {
                    FailNext = false;
                    throw new InvalidOperationException("SMTP 421: máy chủ bận");
                }
                _sent.Add(message);
            }
        }
    }
}
