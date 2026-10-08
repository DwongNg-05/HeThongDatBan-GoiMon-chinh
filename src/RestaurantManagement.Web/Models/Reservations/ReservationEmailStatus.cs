using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Models.Reservations;

/// <summary>Trạng thái gửi email nhìn từ phía nhân viên (S2-09 Task 3). Độc lập với trạng thái đặt bàn.</summary>
public enum EmailDeliveryState
{
    /// <summary>Đã xếp hàng, chưa thử gửi lần nào.</summary>
    Waiting,
    /// <summary>Worker đang gửi (một lần thử đang diễn ra).</summary>
    Sending,
    /// <summary>Đã thử nhưng lỗi, sẽ thử gửi lại.</summary>
    Retrying,
    /// <summary>Đã gửi thành công.</summary>
    Sent,
    /// <summary>Thất bại sau tất cả lần thử.</summary>
    Failed,
    /// <summary>Đã huỷ gửi (ví dụ email nhắc lịch của lượt đặt bàn đã huỷ).</summary>
    Cancelled
}

/// <summary>S2-09 Task 2: một lần thử gửi (dbo.EmailAttempts). Status: Sending / Succeeded / Failed. Thời gian UTC.</summary>
public sealed record EmailAttemptRecord(int AttemptNumber, string Status, DateTime StartedAtUtc, DateTime? CompletedAtUtc, string? Error);

/// <summary>Một lần thử gửi hiển thị cho nhân viên (giờ Việt Nam).</summary>
public sealed record EmailAttemptItem(
    int Number,
    string Label,
    DateTime StartedAt,
    DateTime? CompletedAt,
    string ResultLabel,
    string ResultCssClass,
    string? ErrorMessage);

/// <summary>Một dòng của dbo.EmailOutbox. Thời gian ở dạng UTC.</summary>
public sealed record ReservationEmailRecord(
    long Id,
    long ReservationId,
    string MessageType,
    string Recipient,
    string Status,
    int AttemptCount,
    DateTime? LastAttemptAtUtc,
    DateTime NextAttemptAtUtc,
    DateTime? SentAtUtc,
    string? LastError,
    DateTime CreatedAtUtc,
    IReadOnlyList<EmailAttemptRecord>? Attempts = null);

/// <summary>Thông tin hiển thị của một email trong khu vực "Email xác nhận" ở màn hình chi tiết đặt bàn.</summary>
public sealed record ReservationEmailItem(
    long Id,
    long ReservationId,
    string MessageType,
    string TypeLabel,
    string Recipient,
    EmailDeliveryState State,
    string StateLabel,
    string StateCssClass,
    int AttemptCount,
    int MaxAttempts,
    int RetryCount,
    DateTime? LastAttemptAt,
    DateTime? NextRetryAt,
    DateTime? SentAt,
    string FinalResult,
    string? ErrorMessage,
    DateTime CreatedAt,
    IReadOnlyList<EmailAttemptItem>? AttemptHistory = null)
{
    /// <summary>Từng lần thử (lần gửi đầu, lần gửi lại 1…3), cũ trước mới sau.</summary>
    public IReadOnlyList<EmailAttemptItem> Attempts => AttemptHistory ?? Array.Empty<EmailAttemptItem>();

    /// <summary>Số lần gửi lại tối đa (3).</summary>
    public int MaxRetries => MaxAttempts - 1;

    /// <summary>Đã có kết quả cuối cùng (thành công, thất bại hoặc huỷ), không cần cập nhật nữa.</summary>
    public bool IsFinal => State is EmailDeliveryState.Sent or EmailDeliveryState.Failed or EmailDeliveryState.Cancelled;
}

/// <summary>Khu vực "Email xác nhận" của một lượt đặt bàn.</summary>
public sealed record ReservationEmailPanelViewModel(
    long ReservationId,
    string ReservationCode,
    string? CustomerEmail,
    IReadOnlyList<ReservationEmailItem> Emails)
{
    public bool HasCustomerEmail => !string.IsNullOrWhiteSpace(CustomerEmail);

    /// <summary>Mọi email đều đã có kết quả cuối cùng: trang không cần tự cập nhật nữa.</summary>
    public bool IsFinal => Emails.All(e => e.IsFinal);
}

/// <summary>Màn hình chi tiết đặt bàn của nhân viên: thông tin đặt bàn và trạng thái email tách riêng.</summary>
public sealed record ReservationDetailsViewModel(ReservationListItemViewModel Reservation, ReservationEmailPanelViewModel Email);

public static class ReservationEmailStatus
{
    /// <summary>Số lần thử tối đa = lần gửi đầu + 3 lần gửi lại (S2-09 Task 2), khớp AttemptCount BETWEEN 0 AND 4.</summary>
    public const int MaxAttempts = EmailRetryPolicy.MaxAttempts;

    /// <summary>Độ dài tối đa của thông tin lỗi hiển thị cho nhân viên.</summary>
    public const int MaxErrorLength = 500;

    public static EmailDeliveryState StateOf(string status, int attemptCount) => status switch
    {
        "Sent" => EmailDeliveryState.Sent,
        "Failed" => EmailDeliveryState.Failed,
        "Cancelled" => EmailDeliveryState.Cancelled,
        "Processing" => EmailDeliveryState.Sending,
        _ => attemptCount > 0 ? EmailDeliveryState.Retrying : EmailDeliveryState.Waiting
    };

    public static string StateLabel(EmailDeliveryState state) => state switch
    {
        EmailDeliveryState.Waiting => "Đang chờ gửi",
        EmailDeliveryState.Sending => "Đang gửi",
        EmailDeliveryState.Retrying => "Đang thử gửi lại",
        EmailDeliveryState.Sent => "Đã gửi thành công",
        EmailDeliveryState.Failed => "Gửi thất bại",
        _ => "Đã huỷ gửi"
    };

    public static string StateCssClass(EmailDeliveryState state) => state switch
    {
        EmailDeliveryState.Waiting => "email-state-waiting",
        EmailDeliveryState.Sending or EmailDeliveryState.Retrying => "email-state-retrying",
        EmailDeliveryState.Sent => "email-state-sent",
        EmailDeliveryState.Failed => "email-state-failed",
        _ => "email-state-cancelled"
    };

    public static string TypeLabel(string messageType) => messageType switch
    {
        "BookingReceived" => "Xác nhận đã nhận yêu cầu đặt bàn",
        "BookingConfirmed" => "Xác nhận đặt bàn thành công",
        "BookingRejected" => "Thông báo từ chối đặt bàn",
        "BookingCancelled" => "Thông báo huỷ đặt bàn",
        "BookingReminder" => "Nhắc lịch đặt bàn",
        _ => messageType
    };

    /// <summary>Chuyển một dòng outbox thành thông tin hiển thị (giờ Việt Nam).</summary>
    public static ReservationEmailItem ToItem(ReservationEmailRecord r)
    {
        var state = StateOf(r.Status, r.AttemptCount);
        var attempts = Math.Clamp(r.AttemptCount, 0, MaxAttempts);
        DateTime? Local(DateTime? utc) => utc is null ? null : VietnamTime.FromUtc(utc.Value);

        var lastAttempt = Local(r.LastAttemptAtUtc ?? (state == EmailDeliveryState.Sent ? r.SentAtUtc : null));
        var sentAt = state == EmailDeliveryState.Sent ? Local(r.SentAtUtc) : null;
        var finalResult = state switch
        {
            EmailDeliveryState.Sent => (sentAt is null ? "Thành công" : $"Thành công lúc {sentAt.Value:dd/MM/yyyy HH:mm}")
                + (attempts > 1 ? $" (ở lần thử thứ {attempts})" : ""),
            EmailDeliveryState.Failed => attempts > 1
                ? $"Thất bại sau {attempts} lần thử (lần gửi đầu và {attempts - 1} lần gửi lại)"
                : $"Thất bại sau {attempts} lần thử",
            EmailDeliveryState.Cancelled => "Đã huỷ, không gửi nữa",
            _ => "Chưa có kết quả cuối cùng"
        };

        // Lỗi chỉ hiện khi email thất bại hoặc đang thử lại sau lỗi; không bao giờ hiện cho email đã gửi thành công.
        var showError = state is EmailDeliveryState.Failed or EmailDeliveryState.Retrying
            || (state == EmailDeliveryState.Sending && attempts > 1);
        var error = showError ? Shorten(r.LastError) : null;
        if (state == EmailDeliveryState.Failed && error is null) error = "Không có thông tin lỗi chi tiết.";

        return new ReservationEmailItem(
            r.Id, r.ReservationId, r.MessageType, TypeLabel(r.MessageType), r.Recipient,
            state, StateLabel(state), StateCssClass(state),
            attempts, MaxAttempts, Math.Max(0, attempts - 1),
            lastAttempt,
            state == EmailDeliveryState.Retrying ? Local(r.NextAttemptAtUtc) : null,
            sentAt, finalResult, error, VietnamTime.FromUtc(r.CreatedAtUtc),
            (r.Attempts ?? Array.Empty<EmailAttemptRecord>()).OrderBy(a => a.AttemptNumber).Select(ToAttemptItem).ToArray());
    }

    /// <summary>Một lần thử: "Lần gửi đầu"/"Lần gửi lại n", thời điểm và kết quả của riêng lần đó.</summary>
    public static EmailAttemptItem ToAttemptItem(EmailAttemptRecord a)
    {
        var (label, css) = a.Status switch
        {
            "Succeeded" => ("Thành công", "email-state-sent"),
            "Failed" => ("Thất bại", "email-state-failed"),
            _ => ("Đang gửi", "email-state-retrying")
        };
        return new EmailAttemptItem(a.AttemptNumber, EmailRetryPolicy.AttemptLabel(a.AttemptNumber),
            VietnamTime.FromUtc(a.StartedAtUtc), a.CompletedAtUtc is null ? null : VietnamTime.FromUtc(a.CompletedAtUtc.Value),
            label, css, a.Status == "Failed" ? Shorten(a.Error) ?? "Không có thông tin lỗi chi tiết." : null);
    }

    /// <summary>
    /// Dựng khu vực email cho đúng một lượt đặt bàn. Bỏ mọi dòng của lượt đặt bàn khác
    /// để thông tin lỗi không bao giờ hiển thị nhầm sang mã đặt bàn khác. Email mới nhất đứng trước.
    /// </summary>
    public static ReservationEmailPanelViewModel BuildPanel(
        long reservationId, string reservationCode, string? customerEmail, IEnumerable<ReservationEmailRecord> records) =>
        new(reservationId, reservationCode, customerEmail,
            records.Where(r => r.ReservationId == reservationId)
                .OrderByDescending(r => r.CreatedAtUtc).ThenByDescending(r => r.Id)
                .Select(ToItem)
                .ToArray());

    private static string? Shorten(string? error)
    {
        var value = error?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        return value.Length <= MaxErrorLength ? value : value[..MaxErrorLength] + "…";
    }
}

/// <summary>Nhãn tiếng Việt cho trạng thái đặt bàn (dbo.Reservations.Status), tách biệt với trạng thái email.</summary>
public static class ReservationStatusDisplay
{
    public static string Label(string status) => status switch
    {
        "Pending" => "Chờ xác nhận",
        "Confirmed" => "Đã xác nhận",
        "Rejected" => "Bị từ chối",
        "Cancelled" => "Đã huỷ",
        "Arrived" => "Khách đã đến",
        "NoShow" => "Khách không đến",
        _ => status
    };
}
