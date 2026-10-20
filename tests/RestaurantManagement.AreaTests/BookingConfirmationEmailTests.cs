using Microsoft.Extensions.Logging.Abstractions;
using RestaurantManagement.Web.Controllers;
using RestaurantManagement.Web.Models.Reservations;
using RestaurantManagement.Web.Services;
using RestaurantManagement.Web.Services.EmailVerification;

/// <summary>
/// Email đặt bàn (không cần database). Migration 050: email chỉ gửi khi Quản lý / Phục vụ xác nhận lượt đặt (BookingConfirmed),
/// kèm mã đặt bàn 6 ký tự (đúng mã tra cứu), bàn đã giữ và liên kết tra cứu; tiêu đề "Bếp Nhà - Xác nhận đặt bàn ...".
/// Trang đặt bàn thành công luôn có mã đặt bàn.
/// </summary>
internal static class BookingConfirmationEmailTests
{
    // Đúng dạng JSON mà usp_QueueBookingEmail (migration 026) lưu: thời gian UTC không có hậu tố Z.
    private const string Payload = """
        {"Code":"A1B2C3","CustomerName":"Nguyễn <b>An</b>","StartsAt":"2026-10-05T12:00:00","EndsAt":"2026-10-05T13:30:00",
         "GuestCount":4,"Status":"Pending","AreaName":"Tầng 1","RestaurantName":"Bếp Nhà",
         "RestaurantAddress":"12 Lê Lợi, Phường Bến Nghé, Quận 1, TP.HCM","RestaurantPhone":"0281234567"}
        """;

    internal static async Task Run(Action<bool, string> check)
    {
        // Nội dung email: mã đặt bàn, ngày giờ (giờ Việt Nam), số khách, địa chỉ quán.
        var details = BookingConfirmationEmail.ParsePayload(Payload);
        check(details is { Code: "A1B2C3", GuestCount: 4, AreaName: "Tầng 1", RestaurantName: "Bếp Nhà", RestaurantPhone: "0281234567" }
            && details.StartsAtUtc == new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc) && details.StartsAtVietnam == new DateTime(2026, 10, 5, 19, 0, 0),
            "Booking email: payload read with UTC time converted to Vietnam time");
        var message = BookingConfirmationEmail.Create("khach@example.com", details);
        const string address = "12 Lê Lợi, Phường Bến Nghé, Quận 1, TP.HCM";
        check(message.To == "khach@example.com" && message.Subject == "Bếp Nhà - Xác nhận đặt bàn A1B2C3",
            "Booking email: subject starts with the restaurant name — \"Bếp Nhà - Xác nhận đặt bàn A1B2C3\"");
        check(message.HtmlBody.Contains(EmailBranding.LogoImg) && message.HtmlBody.Contains(">Xác nhận đặt bàn</td>")
              && message.HtmlBody.Contains("#3c0102") && !message.HtmlBody.Contains("#c92d3e"),
            "Booking email: header shows the Bếp Nhà logo (instead of the text) and \"Xác nhận đặt bàn\" in the project red");
        foreach (var (body, name) in new[] { (message.TextBody, "text"), (message.HtmlBody, "HTML") })
            check(body.Contains("A1B2C3") && body.Contains("19:00, Thứ Hai ngày 05/10/2026") && body.Contains("4 người") && body.Contains(address),
                $"Booking email ({name}): booking code, date and time, guest count and restaurant address");
        check(message.HtmlBody.Contains("Nguyễn &lt;b&gt;An&lt;/b&gt;") && !message.HtmlBody.Contains("<b>An</b>"), "Booking email: customer input is HTML-encoded");

        var minimal = BookingConfirmationEmail.ParsePayload("""{"Code":"ZZ9999","StartsAt":"2026-12-31T16:30:00","GuestCount":2}""");
        var minimalMessage = BookingConfirmationEmail.Create("a@b.vn", minimal);
        check(minimal is { RestaurantName: "Bếp Nhà", RestaurantAddress: "", AreaName: null } && minimalMessage.TextBody.Contains("23:30, Thứ Năm ngày 31/12/2026")
            && minimalMessage.TextBody.Contains("ZZ9999"), "Booking email: missing restaurant data still produces a usable email");
        var invalid = false;
        try { BookingConfirmationEmail.ParsePayload("""{"StartsAt":"2026-10-05T12:00:00"}"""); } catch (FormatException) { invalid = true; }
        check(invalid, "Booking email: payload without a booking code is rejected");

        // Gửi ngay khi nhân viên xác nhận lượt đặt: thành công.
        var claimed = new ClaimedEmail(77, 1, "khach@example.com", "Bếp Nhà - Xác nhận đặt bàn A1B2C3", Payload, BookingEmailDispatcher.BookingConfirmed);
        var outbox = new FakeOutbox(claimed);
        var sender = new FakeSender();
        var sent = await Dispatcher(outbox, sender).SendBookingConfirmedAsync(500);
        check(sent is { Outcome: BookingEmailOutcome.Sent, Recipient: "khach@example.com", Error: null }
            && outbox.ClaimedFor == (500, "BookingConfirmed") && outbox.Completed == (77L, 1, true, (string?)null),
            "Booking email: sent right after the staff confirmation and success stored in the outbox");
        check(sender.Sent.Count == 1 && sender.Sent[0].TextBody.Contains("A1B2C3"), "Booking email: the customer receives one email with the booking code");

        // Gửi thất bại: lưu lỗi, trả về Failed — lượt đặt bàn không bị ảnh hưởng.
        outbox = new FakeOutbox(claimed);
        var failed = await Dispatcher(outbox, new FakeSender(new System.Net.Mail.SmtpException("Mailbox unavailable"))).SendBookingConfirmedAsync(500);
        check(failed is { Outcome: BookingEmailOutcome.Failed, Recipient: "khach@example.com" } && failed.Error!.Contains("Mailbox unavailable")
            && outbox.Completed is { Succeeded: false } c && c.Error!.Contains("SmtpException"),
            "Booking email: failure is caught and stored with its error");
        outbox = new FakeOutbox(claimed);
        var timedOut = await Dispatcher(outbox, new FakeSender(new OperationCanceledException())).SendBookingConfirmedAsync(500);
        check(timedOut.Outcome == BookingEmailOutcome.Failed && outbox.Completed?.Error?.Contains("Hết thời gian") == true, "Booking email: timeout is reported as a failure");
        check(BookingEmailDispatcher.SendTimeout < TimeSpan.FromSeconds(60), "Booking email: send timeout keeps within the 60-second requirement");
        outbox = new FakeOutbox(claimed with { PayloadJson = "{}" });
        check((await Dispatcher(outbox, new FakeSender()).SendBookingConfirmedAsync(500)).Outcome == BookingEmailOutcome.Failed && outbox.Completed?.Succeeded == false,
            "Booking email: broken payload is recorded as failed, not thrown");

        // Không có email cần gửi / không đọc được hàng đợi.
        outbox = new FakeOutbox(null);
        var none = await Dispatcher(outbox, sender).SendBookingConfirmedAsync(501);
        check(none.Outcome == BookingEmailOutcome.NotRequested && outbox.Completed is null, "Booking email: a confirmed booking without email sends nothing");
        check(ConfirmationEmailNotice.AfterConfirm(sent).Contains("Đã gửi email kèm mã đặt bàn tới k***h@example.com")
              && ConfirmationEmailNotice.AfterConfirm(failed).Contains("tự gửi lại") && ConfirmationEmailNotice.AfterConfirm(none).Contains("không để lại email"),
            "Staff confirmation: the waiter sees whether the code was emailed, will be retried, or must be phoned");
        var broken = await Dispatcher(new FakeOutbox(null, claimThrows: true), sender).SendBookingConfirmedAsync(502);
        check(broken.Outcome == BookingEmailOutcome.Failed, "Booking email: database error while claiming does not throw");

        // Trang xác nhận: mã đặt bàn luôn hiển thị đầy đủ.
        Dictionary<string, object?> Data(string outcome, string? to) => new()
        {
            [BookingSuccessViewModel.CodeKey] = "A1B2C3", [BookingSuccessViewModel.StartsAtKey] = "2026-10-05T19:00",
            [BookingSuccessViewModel.GuestCountKey] = 4, [BookingSuccessViewModel.EmailOutcomeKey] = outcome, [BookingSuccessViewModel.EmailToKey] = to
        };
        foreach (var outcome in Enum.GetValues<BookingEmailOutcome>())
        {
            var data = Data(outcome.ToString(), "k***h@example.com");
            var model = BookingSuccessViewModel.FromTempData(k => data.GetValueOrDefault(k));
            check(model is { Code: "A1B2C3", GuestCount: 4 } && model.EmailOutcome == outcome && model.StartsAt == new DateTime(2026, 10, 5, 19, 0, 0),
                $"Booking confirmation page: full booking code shown when email outcome is {outcome}");
        }
        var failedPage = BookingSuccessViewModel.FromTempData(k => Data("Failed", "k***h@example.com").GetValueOrDefault(k))!;
        var sentPage = BookingSuccessViewModel.FromTempData(k => Data("Sent", "k***h@example.com").GetValueOrDefault(k))!;
        check(sentPage.EmailMessage.Contains("đã được gửi tới k***h@example.com") && failedPage.EmailMessage.Contains("chưa gửi được")
            && failedPage.EmailMessage.Contains("vẫn được ghi nhận"), "Booking confirmation page: success and failure email messages");
        // Migration 050: lúc đặt bàn không gửi email — trang báo email sẽ đến khi nhà hàng xác nhận.
        var waitingPage = BookingSuccessViewModel.FromTempData(k => Data("NotRequested", "k***h@example.com").GetValueOrDefault(k))!;
        var noEmailPage = BookingSuccessViewModel.FromTempData(k => Data("NotRequested", null).GetValueOrDefault(k))!;
        check(waitingPage.EmailMessage.Contains("Khi nhà hàng xác nhận") && waitingPage.EmailMessage.Contains("k***h@example.com")
              && noEmailPage.EmailMessage.Contains("không nhập email") && noEmailPage.EmailMessage.Contains("tra cứu"),
            "Booking confirmation page: the email arrives only after the restaurant confirms (or the code must be kept)");
        check(BookingSuccessViewModel.FromTempData(_ => null) is null, "Booking confirmation page: opened directly shows no stale code");
        check(BookingSuccessViewModel.MaskEmail("khach@example.com") == "k***h@example.com" && BookingSuccessViewModel.MaskEmail("ab@x.vn") == "a***@x.vn"
            && BookingSuccessViewModel.MaskEmail(null) is null, "Booking confirmation page: email address is masked");

        // Khách tự chọn bàn: mã khách nhận vẫn là mã đặt bàn 6 ký tự (đúng mã tra cứu); bàn hiện ở dòng riêng.
        var withTable = BookingConfirmationEmail.ParsePayload(Payload.Replace("\"Code\":\"A1B2C3\",", "\"Code\":\"A1B2C3\",\"TableCode\":\"A05\","));
        const string lookupUrl = "https://bepnha.vn/ReservationLookup?code=A1B2C3";
        var tableMessage = BookingConfirmationEmail.Create("khach@example.com", withTable, BookingConfirmationEmail.Confirmed, lookupUrl: lookupUrl);
        check(withTable is { TableCode: "A05", DisplayCode: "A1B2C3", Code: "A1B2C3" } && tableMessage.Subject == "Bếp Nhà - Xác nhận đặt bàn A1B2C3",
            "Booking code: the subject carries the 6-character booking code, not the table code");
        check(tableMessage.HtmlBody.Contains("Mã đặt bàn (dùng để tra cứu)") && tableMessage.HtmlBody.Contains(">A1B2C3</div>")
            && tableMessage.TextBody.Contains("Mã đặt bàn:   A1B2C3") && tableMessage.TextBody.Contains("Bàn:          A05")
            && tableMessage.HtmlBody.Contains("giữ bàn A05") && tableMessage.TextBody.Contains("đọc mã đặt bàn A1B2C3"),
            "Booking code: the confirmation email shows the booking code to look up and the held table on its own line");
        check(tableMessage.HtmlBody.Contains(">Tra cứu đặt bàn</a>") && tableMessage.HtmlBody.Contains(lookupUrl) && tableMessage.TextBody.Contains("Tra cứu đặt bàn: " + lookupUrl)
            && !tableMessage.HtmlBody.Contains("Xác nhận đặt bàn</a>"),
            "Booking code: the email links to the lookup page with the code filled in (no confirm button any more)");
        check(details.TableCode is null && details.DisplayCode == "A1B2C3" && !message.TextBody.Contains("Bàn:"), "Booking code: a booking without a table has no table line");

        var tableData = Data("NotRequested", null);
        tableData[BookingSuccessViewModel.TableCodeKey] = "A05";
        var tablePage = BookingSuccessViewModel.FromTempData(k => tableData.GetValueOrDefault(k))!;
        check(tablePage is { TableCode: "A05", DisplayCode: "A1B2C3", Code: "A1B2C3" } && tablePage.EmailMessage.Contains("lưu lại mã đặt bàn"),
            "Booking code: the booking success page shows the booking code (the table is listed separately)");
        check(new RestaurantManagement.Web.Models.Reservations.BookingTableOption { Code = "A05", AreaName = "Tầng 1", MaxCapacity = 4 }.Label == "A05 · Tầng 1 · tối đa 4 khách",
            "Table code: table choices show code, area and capacity");

        // S1-04 Task 2: chỉ Quản lý, Phục vụ xem được danh sách/chi tiết khách đặt trước (Bếp, Thu ngân bị chặn).
        string? Roles(string action) => typeof(RestaurantManagement.Web.Controllers.ReservationsController).GetMethods()
            .Where(m => m.Name == action).SelectMany(m => m.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true))
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>().Select(a => a.Roles).FirstOrDefault();
        // Quản lý huỷ đặt bàn: email báo huỷ có mã đặt bàn và lý do.
        var cancelDetails = withTable with { CancelReason = "Nhà hàng có sự cố điện" };
        var cancelMessage = BookingConfirmationEmail.Create("khach@example.com", cancelDetails, BookingConfirmationEmail.Cancelled);
        check(cancelMessage.Subject == "Bếp Nhà - Đã huỷ đặt bàn A1B2C3" && cancelMessage.TextBody.Contains("Lý do huỷ:    Nhà hàng có sự cố điện")
            && cancelMessage.HtmlBody.Contains("Nhà hàng có sự cố điện") && cancelMessage.TextBody.Contains("đã bị huỷ") && !cancelMessage.TextBody.Contains("Khi đến quán"),
            "Cancel: cancellation email has the booking code, reason and no arrival instructions");
        check(!tableMessage.TextBody.Contains("Lý do huỷ"), "Cancel: confirmation email never shows a cancellation reason");
        outbox = new FakeOutbox(claimed with { MessageType = BookingEmailDispatcher.BookingCancelled });
        var cancelSender = new FakeSender();
        var cancelSent = await Dispatcher(outbox, cancelSender).SendBookingCancelledAsync(500);
        // Nội dung email mẫu không có lý do huỷ = khách tự huỷ → "Huỷ đặt bàn thành công".
        check(cancelSent.Outcome == BookingEmailOutcome.Sent && outbox.ClaimedFor == (500, "BookingCancelled") && cancelSender.Sent.Single().Subject.StartsWith("Bếp Nhà - Huỷ đặt bàn thành công"),
            "Cancel: cancellation email is sent right away");

        // Khách tự huỷ (Tra cứu đặt bàn: không lý do; email nhắc lịch: lý do cố định) → email "Huỷ đặt bàn thành công".
        foreach (var selfCancel in new[] { withTable with { CancelReason = null }, withTable with { CancelReason = BookingConfirmationEmail.OnlineCancelReason },
                                           withTable with { CancelReason = BookingConfirmationEmail.ReminderLinkCancelReason } })
        {
            var selfMessage = BookingConfirmationEmail.Create("khach@example.com", selfCancel, BookingConfirmationEmail.Cancelled);
            check(selfMessage.Subject == "Bếp Nhà - Huỷ đặt bàn thành công A1B2C3"
                  && selfMessage.HtmlBody.Contains(">Huỷ đặt bàn thành công</td>") && selfMessage.TextBody.Contains("Bạn đã huỷ đặt bàn thành công")
                  && !selfMessage.TextBody.Contains("rất tiếc") && !selfMessage.TextBody.Contains("Lý do huỷ") && selfMessage.TextBody.Contains("A1B2C3"),
                "Cancel: a customer's own cancellation gets a \"Huỷ đặt bàn thành công\" email with the booking code");
        }
        check(BookingConfirmationEmail.Create("khach@example.com", cancelDetails, BookingConfirmationEmail.Cancelled).TextBody.Contains("rất tiếc"),
            "Cancel: a staff cancellation (with a reason) keeps the apology wording");

        // Từ chối (migration 065): email kèm lý do, không có lời dặn "đọc mã khi đến quán"; gửi ngay khi nhân viên từ chối.
        var rejectedDetails = BookingConfirmationEmail.ParsePayload(Payload.Replace("\"Code\":\"A1B2C3\",", "\"Code\":\"A1B2C3\",\"RejectionReason\":\"NoTable\","));
        var rejectedMessage = BookingConfirmationEmail.Create("khach@example.com", rejectedDetails, BookingConfirmationEmail.Rejected);
        check(rejectedDetails.RejectionReason == "NoTable" && rejectedMessage.Subject == "Bếp Nhà - Không thể nhận đặt bàn A1B2C3"
              && rejectedMessage.HtmlBody.Contains(">Không thể nhận đặt bàn</td>") && rejectedMessage.HtmlBody.Contains("Hết bàn")
              && rejectedMessage.TextBody.Contains("Lý do:        Hết bàn") && rejectedMessage.TextBody.Contains("hết bàn phù hợp")
              && rejectedMessage.TextBody.Contains("A1B2C3") && !rejectedMessage.TextBody.Contains("Khi đến quán"),
            "Reject: rejection email names the booking code and the reason, without arrival instructions");
        outbox = new FakeOutbox(claimed with { MessageType = BookingEmailDispatcher.BookingRejected });
        var rejectSender = new FakeSender();
        var rejectSent = await Dispatcher(outbox, rejectSender).SendBookingRejectedAsync(500);
        check(rejectSent.Outcome == BookingEmailOutcome.Sent && outbox.ClaimedFor == (500, "BookingRejected")
              && rejectSender.Sent.Single().Subject.StartsWith("Bếp Nhà - Không thể nhận đặt bàn"),
            "Reject: rejection email is sent right away");
        check(ConfirmationEmailNotice.AfterReject(rejectSent).Contains("Đã gửi email báo từ chối"),
            "Reject: staff see that the rejection email was sent");
        check(new ReservationListItemViewModel { Status = "Pending" }.CanCancel && new ReservationListItemViewModel { Status = "Confirmed" }.CanCancel
            && !new ReservationListItemViewModel { Status = "Cancelled" }.CanCancel && !new ReservationListItemViewModel { Status = "Arrived" }.CanCancel,
            "Cancel: only pending or confirmed reservations can be cancelled");
        string? RolesOf(Type controller, string action) => controller.GetMethods().Where(m => m.Name == action)
            .SelectMany(m => m.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true))
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>().Select(a => a.Roles).FirstOrDefault();
        check(RolesOf(typeof(RestaurantManagement.Web.Controllers.ReservationsController), "Cancel") == "Manager"
            && RolesOf(typeof(RestaurantManagement.Web.Controllers.TablesController), "Delete") == "Manager",
            "Manager only: cancel reservation and delete table");
        check(new[] { "Cancelled", "Rejected", "NoShow" }.All(s => new ReservationListItemViewModel { Status = s }.CanDelete)
            && new[] { "Pending", "Confirmed", "Arrived" }.All(s => !new ReservationListItemViewModel { Status = s }.CanDelete),
            "Delete reservation: only cancelled, rejected or no-show reservations can be deleted");
        check(RolesOf(typeof(RestaurantManagement.Web.Controllers.ReservationsController), "Delete") == "Manager",
            "Manager only: delete reservation");

        foreach (var action in new[] { "Index", "Details", "EmailStatus" })
        {
            var roles = Roles(action)?.Split(',') ?? Array.Empty<string>();
            // S1-04 Task 2: chỉ Quản lý và Phục vụ; Bếp, Thu ngân bị chặn ở máy chủ.
            check(roles.Order().SequenceEqual(new[] { "Manager", "Waiter" }), $"Reservation view: {action} open to manager and waiter only");
        }
    }

    private static BookingEmailDispatcher Dispatcher(IBookingEmailOutbox outbox, IEmailSender sender) =>
        new(outbox, sender, NullLogger<BookingEmailDispatcher>.Instance);

    private sealed class FakeOutbox(ClaimedEmail? claimed, bool claimThrows = false) : IBookingEmailOutbox
    {
        public (long ReservationId, string MessageType)? ClaimedFor { get; private set; }
        public (long EmailId, int Attempt, bool Succeeded, string? Error)? Completed { get; private set; }

        public Task<ClaimedEmail?> ClaimAsync(long reservationId, string messageType, CancellationToken cancellationToken = default)
        {
            if (claimThrows) throw new InvalidOperationException("database down");
            ClaimedFor = (reservationId, messageType);
            return Task.FromResult(claimed);
        }

        public Task CompleteAsync(long emailId, int attemptNumber, bool succeeded, string? error, CancellationToken cancellationToken = default)
        {
            Completed = (emailId, attemptNumber, succeeded, error);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSender(Exception? failure = null) : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            if (failure is not null) throw failure;
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }
}
