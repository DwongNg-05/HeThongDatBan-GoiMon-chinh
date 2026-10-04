using Microsoft.Extensions.Logging.Abstractions;
using RestaurantManagement.Web.Models.Reservations;
using RestaurantManagement.Web.Services;
using RestaurantManagement.Web.Services.EmailVerification;

/// <summary>S2-09 Task 1: email xác nhận đặt bàn, gửi ngay sau khi đặt bàn, trang xác nhận luôn có mã đặt bàn (không cần database).</summary>
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
        check(message.To == "khach@example.com" && message.Subject.Contains("A1B2C3"), "Booking email: sent to the customer with the code in the subject");
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

        // Gửi ngay sau khi đặt bàn: thành công.
        var claimed = new ClaimedEmail(77, 1, "khach@example.com", "Xác nhận đặt bàn A1B2C3", Payload, BookingEmailDispatcher.BookingReceived);
        var outbox = new FakeOutbox(claimed);
        var sender = new FakeSender();
        var sent = await Dispatcher(outbox, sender).SendBookingReceivedAsync(500);
        check(sent is { Outcome: BookingEmailOutcome.Sent, Recipient: "khach@example.com", Error: null }
            && outbox.ClaimedFor == (500, "BookingReceived") && outbox.Completed == (77L, 1, true, (string?)null),
            "Booking email: sent right after booking and success stored in the outbox");
        check(sender.Sent.Count == 1 && sender.Sent[0].TextBody.Contains("A1B2C3"), "Booking email: the customer receives one email with the booking code");

        // Gửi thất bại: lưu lỗi, trả về Failed — lượt đặt bàn không bị ảnh hưởng.
        outbox = new FakeOutbox(claimed);
        var failed = await Dispatcher(outbox, new FakeSender(new System.Net.Mail.SmtpException("Mailbox unavailable"))).SendBookingReceivedAsync(500);
        check(failed is { Outcome: BookingEmailOutcome.Failed, Recipient: "khach@example.com" } && failed.Error!.Contains("Mailbox unavailable")
            && outbox.Completed is { Succeeded: false } c && c.Error!.Contains("SmtpException"),
            "Booking email: failure is caught and stored with its error");
        outbox = new FakeOutbox(claimed);
        var timedOut = await Dispatcher(outbox, new FakeSender(new OperationCanceledException())).SendBookingReceivedAsync(500);
        check(timedOut.Outcome == BookingEmailOutcome.Failed && outbox.Completed?.Error?.Contains("Hết thời gian") == true, "Booking email: timeout is reported as a failure");
        check(BookingEmailDispatcher.SendTimeout < TimeSpan.FromSeconds(60), "Booking email: send timeout keeps within the 60-second requirement");
        outbox = new FakeOutbox(claimed with { PayloadJson = "{}" });
        check((await Dispatcher(outbox, new FakeSender()).SendBookingReceivedAsync(500)).Outcome == BookingEmailOutcome.Failed && outbox.Completed?.Succeeded == false,
            "Booking email: broken payload is recorded as failed, not thrown");

        // Không có email cần gửi / không đọc được hàng đợi.
        outbox = new FakeOutbox(null);
        var none = await Dispatcher(outbox, sender).SendBookingReceivedAsync(501);
        check(none.Outcome == BookingEmailOutcome.NotRequested && outbox.Completed is null, "Booking email: booking without email sends nothing");
        var broken = await Dispatcher(new FakeOutbox(null, claimThrows: true), sender).SendBookingReceivedAsync(502);
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
        check(BookingSuccessViewModel.FromTempData(_ => null) is null, "Booking confirmation page: opened directly shows no stale code");
        check(BookingSuccessViewModel.MaskEmail("khach@example.com") == "k***h@example.com" && BookingSuccessViewModel.MaskEmail("ab@x.vn") == "a***@x.vn"
            && BookingSuccessViewModel.MaskEmail(null) is null, "Booking confirmation page: email address is masked");

        // Khách tự chọn bàn: mã bàn (ví dụ A05) là mã khách nhận trên trang xác nhận và trong email.
        var withTable = BookingConfirmationEmail.ParsePayload(Payload.Replace("\"Code\":\"A1B2C3\",", "\"Code\":\"A1B2C3\",\"TableCode\":\"A05\","));
        var tableMessage = BookingConfirmationEmail.Create("khach@example.com", withTable);
        check(withTable is { TableCode: "A05", DisplayCode: "A05", Code: "A1B2C3" } && tableMessage.Subject.StartsWith("Xác nhận đặt bàn A05"),
            "Table code: email subject uses the table code as the booking code");
        check(tableMessage.HtmlBody.Contains(">Mã đặt bàn</div>") && tableMessage.HtmlBody.Contains(">A05</div>")
            && tableMessage.TextBody.Contains("Mã đặt bàn:   A05") && tableMessage.TextBody.Contains("đọc mã đặt bàn A05")
            && !tableMessage.HtmlBody.Contains("A1B2C3") && !tableMessage.TextBody.Contains("A1B2C3"),
            "Table code: the booking code in the email is the table code, no second code");
        check(details.TableCode is null && details.DisplayCode == "A1B2C3" && !message.TextBody.Contains("Mã bàn:"), "Table code: booking without a table still uses the booking code");

        var tableData = Data("Failed", null);
        tableData[BookingSuccessViewModel.TableCodeKey] = "A05";
        var tablePage = BookingSuccessViewModel.FromTempData(k => tableData.GetValueOrDefault(k))!;
        check(tablePage is { TableCode: "A05", DisplayCode: "A05", Code: "A1B2C3" } && tablePage.EmailMessage.Contains("lưu lại mã đặt bàn"),
            "Table code: confirmation page shows the table code");
        check(new RestaurantManagement.Web.Models.Reservations.BookingTableOption { Code = "A05", AreaName = "Tầng 1", MaxCapacity = 4 }.Label == "A05 · Tầng 1 · tối đa 4 khách",
            "Table code: table choices show code, area and capacity");

        // Quản lý, phục vụ, bếp, thu ngân xem được danh sách/chi tiết khách đặt trước.
        string? Roles(string action) => typeof(RestaurantManagement.Web.Controllers.ReservationsController).GetMethods()
            .Where(m => m.Name == action).SelectMany(m => m.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true))
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>().Select(a => a.Roles).FirstOrDefault();
        // Quản lý huỷ đặt bàn: email báo huỷ có mã đặt bàn (mã bàn) và lý do.
        var cancelDetails = withTable with { CancelReason = "Nhà hàng có sự cố điện" };
        var cancelMessage = BookingConfirmationEmail.Create("khach@example.com", cancelDetails, BookingConfirmationEmail.Cancelled);
        check(cancelMessage.Subject.StartsWith("Đã huỷ đặt bàn A05") && cancelMessage.TextBody.Contains("Lý do huỷ:    Nhà hàng có sự cố điện")
            && cancelMessage.HtmlBody.Contains("Nhà hàng có sự cố điện") && cancelMessage.TextBody.Contains("đã bị huỷ") && !cancelMessage.TextBody.Contains("Khi đến quán"),
            "Cancel: cancellation email has the booking code, reason and no arrival instructions");
        check(!tableMessage.TextBody.Contains("Lý do huỷ"), "Cancel: confirmation email never shows a cancellation reason");
        outbox = new FakeOutbox(claimed with { MessageType = BookingEmailDispatcher.BookingCancelled });
        var cancelSender = new FakeSender();
        var cancelSent = await Dispatcher(outbox, cancelSender).SendBookingCancelledAsync(500);
        check(cancelSent.Outcome == BookingEmailOutcome.Sent && outbox.ClaimedFor == (500, "BookingCancelled") && cancelSender.Sent.Single().Subject.StartsWith("Đã huỷ đặt bàn"),
            "Cancel: cancellation email is sent right away");
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
            var roles = Roles(action)?.Split(',') ?? [];
            check(new[] { "Manager", "Waiter", "Kitchen", "Cashier" }.All(roles.Contains), $"Reservation view: {action} open to manager, waiter, kitchen and cashier");
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
