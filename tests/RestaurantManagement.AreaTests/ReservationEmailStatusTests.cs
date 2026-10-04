using RestaurantManagement.Web.Models.Reservations;

/// <summary>S2-09 Task 3: trạng thái email xác nhận trong chi tiết đặt bàn của nhân viên (không cần database).</summary>
internal static class ReservationEmailStatusTests
{
    private static readonly DateTime Utc = new(2026, 10, 3, 2, 0, 0, DateTimeKind.Utc); // 09:00 giờ Việt Nam

    private static ReservationEmailRecord Row(long id, long reservationId, string status, int attempts,
        string? error = null, DateTime? lastAttempt = null, DateTime? sentAt = null, string type = "BookingReceived", int createdMinutes = 0) =>
        new(id, reservationId, type, $"khach{reservationId}@example.com", status, attempts,
            lastAttempt, Utc.AddMinutes(5), sentAt, error, Utc.AddMinutes(createdMinutes - 30));

    internal static void Run(Action<bool, string> check)
    {
        // Đã gửi thành công.
        var sent = ReservationEmailStatus.ToItem(Row(1, 10, "Sent", 2, error: "lỗi cũ", lastAttempt: Utc, sentAt: Utc));
        check(sent is { State: EmailDeliveryState.Sent, StateLabel: "Đã gửi thành công", AttemptCount: 2, RetryCount: 1, ErrorMessage: null, IsFinal: true, NextRetryAt: null }
            && sent.FinalResult == "Thành công lúc 03/10/2026 09:00 (ở lần thử thứ 2)" && sent.LastAttemptAt == new DateTime(2026, 10, 3, 9, 0, 0),
            "Email status: sent email shows success, attempts, last attempt in Vietnam time and no error");

        // Đang chờ gửi.
        var waiting = ReservationEmailStatus.ToItem(Row(2, 10, "Pending", 0));
        check(waiting is { State: EmailDeliveryState.Waiting, StateLabel: "Đang chờ gửi", AttemptCount: 0, RetryCount: 0, LastAttemptAt: null, NextRetryAt: null, ErrorMessage: null, IsFinal: false }
            && waiting.FinalResult == "Chưa có kết quả cuối cùng", "Email status: queued email shows waiting without attempts");

        // Đang thử gửi lại sau lỗi.
        var retrying = ReservationEmailStatus.ToItem(Row(3, 10, "Pending", 2, error: "SMTP 421: máy chủ bận", lastAttempt: Utc));
        check(retrying is { State: EmailDeliveryState.Retrying, StateLabel: "Đang thử gửi lại", AttemptCount: 2, MaxAttempts: 4, RetryCount: 1, ErrorMessage: "SMTP 421: máy chủ bận", IsFinal: false }
            && retrying.NextRetryAt == new DateTime(2026, 10, 3, 9, 5, 0) && retrying.FinalResult == "Chưa có kết quả cuối cùng",
            "Email status: retrying email shows retry count, next retry time and the previous error");

        // Đang gửi: lần đầu không có lỗi; lần sau vẫn cho thấy lỗi của lần trước.
        var sendingFirst = ReservationEmailStatus.ToItem(Row(4, 10, "Processing", 1, error: null, lastAttempt: Utc));
        var sendingAgain = ReservationEmailStatus.ToItem(Row(5, 10, "Processing", 3, error: "timeout", lastAttempt: Utc));
        check(sendingFirst is { State: EmailDeliveryState.Sending, StateLabel: "Đang gửi", ErrorMessage: null, IsFinal: false }
            && sendingAgain is { State: EmailDeliveryState.Sending, ErrorMessage: "timeout", RetryCount: 2 },
            "Email status: an attempt in progress is shown as sending");

        // Thất bại sau tất cả lần thử.
        var failed = ReservationEmailStatus.ToItem(Row(6, 10, "Failed", 4, error: "550 5.1.1 Mailbox không tồn tại", lastAttempt: Utc));
        check(failed is { State: EmailDeliveryState.Failed, StateLabel: "Gửi thất bại", AttemptCount: 4, RetryCount: 3, ErrorMessage: "550 5.1.1 Mailbox không tồn tại", IsFinal: true, NextRetryAt: null, SentAt: null }
            && failed.FinalResult == "Thất bại sau 4 lần thử (lần gửi đầu và 3 lần gửi lại)", "Email status: failed email shows final failure after all attempts and the error");
        check(ReservationEmailStatus.ToItem(Row(7, 10, "Failed", 4, error: "  ")).ErrorMessage == "Không có thông tin lỗi chi tiết.",
            "Email status: failed email without error text still explains the failure");
        check(ReservationEmailStatus.ToItem(Row(8, 10, "Failed", 4, error: new string('x', 900))).ErrorMessage!.Length == ReservationEmailStatus.MaxErrorLength + 1,
            "Email status: long error text is shortened");

        var cancelled = ReservationEmailStatus.ToItem(Row(9, 10, "Cancelled", 0, error: "x", type: "BookingReminder"));
        check(cancelled is { State: EmailDeliveryState.Cancelled, StateLabel: "Đã huỷ gửi", ErrorMessage: null, IsFinal: true, TypeLabel: "Nhắc lịch đặt bàn" },
            "Email status: cancelled reminder shows cancelled without error");

        // Mỗi trạng thái có nhãn và màu riêng.
        var states = Enum.GetValues<EmailDeliveryState>();
        check(states.Select(ReservationEmailStatus.StateLabel).Distinct().Count() == states.Length
            && states.Select(ReservationEmailStatus.StateCssClass).Distinct().Count() == states.Length - 1,
            "Email status: every state has its own label (sending and retrying share the amber color)");
        check(ReservationEmailStatus.TypeLabel("BookingConfirmed") == "Xác nhận đặt bàn thành công"
            && ReservationEmailStatus.TypeLabel("BookingReceived") == "Xác nhận đã nhận yêu cầu đặt bàn", "Email status: message types have Vietnamese labels");

        // Trạng thái đúng với từng mã đặt bàn; lỗi không hiển thị nhầm giữa các lượt đặt bàn.
        var rows = new[]
        {
            Row(20, 100, "Sent", 1, lastAttempt: Utc, sentAt: Utc, createdMinutes: 0),
            Row(21, 200, "Failed", 4, error: "LỖI-CỦA-200", lastAttempt: Utc, createdMinutes: 1),
            Row(22, 300, "Pending", 1, error: "LỖI-CỦA-300", lastAttempt: Utc, createdMinutes: 2),
            Row(23, 100, "Pending", 0, type: "BookingConfirmed", createdMinutes: 5)
        };
        var panel100 = ReservationEmailStatus.BuildPanel(100, "AB1234", "khach100@example.com", rows);
        var panel200 = ReservationEmailStatus.BuildPanel(200, "CD5678", "khach200@example.com", rows);
        var panel300 = ReservationEmailStatus.BuildPanel(300, "EF9012", "khach300@example.com", rows);
        check(panel100.Emails.Select(e => e.Id).SequenceEqual(new long[] { 23, 20 }) && panel100.Emails.All(e => e.ReservationId == 100),
            "Email status: reservation shows only its own emails, newest first");
        check(panel100.Emails.All(e => e.ErrorMessage is null) && panel200.Emails.Single().ErrorMessage == "LỖI-CỦA-200"
            && panel300.Emails.Single().ErrorMessage == "LỖI-CỦA-300", "Email status: error text never appears on another reservation");
        check(panel100.Emails.Select(e => e.State).SequenceEqual(new[] { EmailDeliveryState.Waiting, EmailDeliveryState.Sent })
            && panel200.Emails.Single().State == EmailDeliveryState.Failed && panel300.Emails.Single().State == EmailDeliveryState.Retrying,
            "Email status: each reservation code gets the right state");
        check(!panel100.IsFinal && panel200.IsFinal && !panel300.IsFinal, "Email status: auto-refresh stops only when every email is final");

        var noEmail = ReservationEmailStatus.BuildPanel(400, "GH3456", null, rows);
        check(!noEmail.HasCustomerEmail && noEmail.Emails.Count == 0 && noEmail.IsFinal, "Email status: reservation without customer email has nothing to send");

        // Kết quả gửi mới: cùng một email chuyển từ đang thử lại sang đã gửi.
        var updated = ReservationEmailStatus.BuildPanel(300, "EF9012", "khach300@example.com",
            new[] { rows[2] with { Status = "Sent", AttemptCount = 2, SentAtUtc = Utc.AddMinutes(6), LastError = null } });
        check(updated.Emails.Single() is { State: EmailDeliveryState.Sent, ErrorMessage: null, RetryCount: 1 } && updated.IsFinal,
            "Email status: new send result replaces retrying state");

        // Trạng thái đặt bàn khác trạng thái email.
        check(ReservationStatusDisplay.Label("Confirmed") == "Đã xác nhận" && ReservationStatusDisplay.Label("Pending") == "Chờ xác nhận"
            && new ReservationListItemViewModel { Status = "Cancelled" }.StatusLabel == "Đã huỷ",
            "Email status: reservation status has its own labels");
        check(Enum.GetValues<EmailDeliveryState>().Select(ReservationEmailStatus.StateLabel)
                .Intersect(new[] { "Pending", "Confirmed", "Rejected", "Cancelled", "Arrived", "NoShow" }.Select(ReservationStatusDisplay.Label))
                .Any() == false,
            "Email status: no email label can be mistaken for a reservation status label");
    }
}
