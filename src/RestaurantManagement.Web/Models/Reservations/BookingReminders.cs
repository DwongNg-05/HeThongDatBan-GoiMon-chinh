namespace RestaurantManagement.Web.Models.Reservations;

/// <summary>
/// S4-08 Task 1: một email nhắc lịch (dbo.EmailOutbox, MessageType = BookingReminder) kèm thông tin lượt đặt bàn.
/// Thời gian ở dạng UTC. TableCode là bàn đã ghi trong email (PayloadJson), nếu thiếu thì lấy bàn hiện tại của lượt đặt.
/// </summary>
public sealed record BookingReminderRecord(
    long EmailId,
    long ReservationId,
    string ReservationCode,
    string CustomerName,
    DateTime StartsAtUtc,
    string? TableCode,
    string Recipient,
    string Status,
    int AttemptCount,
    DateTime? SentAtUtc,
    string? LastError,
    DateTime CreatedAtUtc,
    string? SkipReason = null,
    string? ReservationStatus = null);

/// <summary>Một dòng của màn hình "Email nhắc lịch" (giờ Việt Nam).</summary>
public sealed record BookingReminderItem(
    long EmailId,
    long ReservationId,
    string ReservationCode,
    string CustomerName,
    DateTime StartsAt,
    string TableLabel,
    string Recipient,
    EmailDeliveryState State,
    string StateLabel,
    string StateCssClass,
    int AttemptCount,
    DateTime? SentAt,
    DateTime QueuedAt,
    string? ErrorMessage,
    string? SkipReason = null,
    string? SkipReasonLabel = null,
    string? ReservationStatus = null,
    string? ReservationStatusLabel = null)
{
    /// <summary>S4-08 Task 2: email nhắc bị bỏ qua vì lượt đặt bàn không còn hợp lệ.</summary>
    public bool IsSkipped => SkipReason is not null;
}

/// <summary>Bộ lọc của màn hình "Email nhắc lịch".</summary>
public static class BookingReminderFilter
{
    public const string All = "all";
    public const string Sent = "sent";
    /// <summary>S4-08 Task 2: chỉ email nhắc bị bỏ qua (lượt đặt bàn không còn hợp lệ).</summary>
    public const string Skipped = "skipped";

    public static string Normalize(string? value) => value switch { Sent => Sent, Skipped => Skipped, _ => All };
}

/// <summary>Màn hình danh sách email nhắc lịch.</summary>
public sealed record BookingReminderListViewModel(
    IReadOnlyList<BookingReminderItem> Items,
    string Filter,
    int LeadMinutes)
{
    public bool SentOnly => Filter == BookingReminderFilter.Sent;
    public int SentCount => Items.Count(i => i.State == EmailDeliveryState.Sent);
    public int SkippedCount => Items.Count(i => i.IsSkipped);
}

/// <summary>Kết quả một lượt chạy tác vụ nhắc lịch.</summary>
public sealed record BookingReminderRunResult(int Queued, int Sent, int Failed, int Skipped = 0)
{
    public int Attempted => Sent + Failed;

    public string Summary => Queued == 0 && Attempted == 0 && Skipped == 0
        ? "Không có lượt đặt bàn nào đến giờ nhắc."
        : $"Đã xếp {Queued} email nhắc mới, gửi thành công {Sent}"
          + (Failed > 0 ? $", lỗi {Failed} (sẽ tự gửi lại sau 5 phút)" : "")
          + (Skipped > 0 ? $", bỏ qua {Skipped} (lượt đặt bàn không còn ở trạng thái đã xác nhận)" : "")
          + ".";
}

/// <summary>
/// S4-08 Task 1: quy tắc nhắc lịch. Database là nơi quyết định (059_S408BookingReminderEmail.sql:
/// usp_QueueDueBookingReminders); lớp này mô tả cùng quy tắc cho màn hình và kiểm thử.
/// </summary>
public static class BookingReminderRules
{
    public const string MessageType = "BookingReminder";

    /// <summary>Gửi nhắc trước giờ hẹn 120 phút (khớp mặc định dbo.RestaurantSettings.ReminderLeadMinutes).</summary>
    public const int DefaultLeadMinutes = 120;

    /// <summary>Thời điểm bắt đầu được gửi nhắc: giờ hẹn - 2 tiếng.</summary>
    public static DateTime RemindAt(DateTime startsAt, int leadMinutes = DefaultLeadMinutes) => startsAt.AddMinutes(-leadMinutes);

    /// <summary>
    /// Lượt đặt bàn đến hạn nhắc: đã xác nhận, có email, giờ hẹn còn ở tương lai và còn không quá 2 tiếng nữa.
    /// Lượt chưa tới mốc (còn hơn 2 tiếng) chưa được gửi.
    /// </summary>
    public static bool IsDue(string reservationStatus, string? email, DateTime startsAtUtc, DateTime nowUtc, int leadMinutes = DefaultLeadMinutes) =>
        reservationStatus == "Confirmed"
        && !string.IsNullOrWhiteSpace(email)
        && startsAtUtc > nowUtc
        && startsAtUtc <= nowUtc.AddMinutes(leadMinutes);

    /// <summary>
    /// S4-08 Task 2: lý do bỏ qua gửi nhắc theo trạng thái lượt đặt bàn tại thời điểm gửi; null = vẫn gửi
    /// (khớp dbo.fn_BookingReminderSkipReason, migration 060).
    /// </summary>
    public static string? SkipReasonFor(string? reservationStatus, DateTime startsAtUtc, DateTime nowUtc) => reservationStatus switch
    {
        null or "Cancelled" => "Cancelled",
        "Arrived" => "Arrived",
        "Rejected" => "Rejected",
        "NoShow" => "NoShow",
        not "Confirmed" => "NotConfirmed",
        _ => startsAtUtc <= nowUtc ? "StartPassed" : null
    };

    /// <summary>Lý do bỏ qua hiển thị cho nhân viên.</summary>
    public static string? SkipReasonLabel(string? skipReason) => skipReason switch
    {
        null => null,
        "Cancelled" => "Lượt đặt bàn đã huỷ",
        "Arrived" => "Khách đã tới",
        "Rejected" => "Lượt đặt bàn bị từ chối",
        "NoShow" => "Khách không tới",
        "NotConfirmed" => "Lượt đặt bàn không ở trạng thái đã xác nhận",
        "StartPassed" => "Đã qua giờ hẹn",
        _ => skipReason
    };

    /// <summary>CSS cho nhãn trạng thái lượt đặt bàn (đã xác nhận, đã huỷ, khách đã tới…).</summary>
    public static string ReservationStatusCss(string status) => status switch
    {
        "Confirmed" => "reservation-state-confirmed",
        "Arrived" => "reservation-state-arrived",
        "Cancelled" or "Rejected" or "NoShow" => "reservation-state-cancelled",
        _ => "reservation-state-pending"
    };

    /// <summary>Nhãn trạng thái gửi nhắc trên chi tiết lượt đặt bàn (cột dbo.Reservations.ReminderEmailStatus).</summary>
    public static string DetailLabel(string? reminderStatus, string reservationStatus, string? email, DateTime startsAt, DateTime now,
        string? skipReason = null)
    {
        if (reminderStatus == "Cancelled" && skipReason is not null)
            return $"Đã bỏ qua gửi nhắc: {SkipReasonLabel(skipReason)}";
        if (reminderStatus is null && SkipReasonFor(reservationStatus, DateTime.MaxValue, DateTime.MinValue) is { } reason)
            return $"Không gửi nhắc: {SkipReasonLabel(reason)}";
        if (reminderStatus is not null)
            return reminderStatus switch
            {
                "Pending" => "Đang chờ gửi",
                "Sending" => "Đang gửi",
                "Retrying" => "Gửi lỗi, đang thử gửi lại",
                "Sent" => "Đã gửi thành công",
                "Failed" => "Gửi thất bại",
                "Cancelled" => "Đã huỷ gửi",
                _ => reminderStatus
            };
        if (string.IsNullOrWhiteSpace(email)) return "Không gửi nhắc (khách không để lại email)";
        if (startsAt <= now) return "Không gửi nhắc (đã qua giờ hẹn)";
        return startsAt > now.AddMinutes(DefaultLeadMinutes) ? "Chưa tới giờ nhắc" : "Đến giờ nhắc, đang chờ tác vụ gửi";
    }

    public static BookingReminderItem ToItem(BookingReminderRecord r)
    {
        var state = ReservationEmailStatus.StateOf(r.Status, r.AttemptCount);
        var skipped = state == EmailDeliveryState.Cancelled ? r.SkipReason : null;
        var showError = state is EmailDeliveryState.Failed or EmailDeliveryState.Retrying;
        var error = showError ? Shorten(r.LastError) : null;
        if (state == EmailDeliveryState.Failed && error is null) error = "Không có thông tin lỗi chi tiết.";
        return new BookingReminderItem(
            r.EmailId, r.ReservationId, r.ReservationCode, r.CustomerName,
            VietnamTime.FromUtc(r.StartsAtUtc),
            string.IsNullOrWhiteSpace(r.TableCode) ? "Chưa xếp bàn" : r.TableCode.Trim(),
            r.Recipient,
            state, ReservationEmailStatus.StateLabel(state), ReservationEmailStatus.StateCssClass(state),
            r.AttemptCount,
            state == EmailDeliveryState.Sent && r.SentAtUtc is not null ? VietnamTime.FromUtc(r.SentAtUtc.Value) : null,
            VietnamTime.FromUtc(r.CreatedAtUtc),
            error,
            skipped,
            SkipReasonLabel(skipped),
            r.ReservationStatus,
            r.ReservationStatus is null ? null : ReservationStatusDisplay.Label(r.ReservationStatus));
    }

    public static BookingReminderListViewModel BuildList(IEnumerable<BookingReminderRecord> records, string? filter, int leadMinutes)
    {
        var f = BookingReminderFilter.Normalize(filter);
        return new(records
                .Select(ToItem)
                .Where(i => f switch
                {
                    BookingReminderFilter.Sent => i.State == EmailDeliveryState.Sent,
                    BookingReminderFilter.Skipped => i.IsSkipped,
                    _ => true
                })
                .OrderByDescending(i => i.SentAt ?? i.QueuedAt).ThenByDescending(i => i.EmailId)
                .ToArray(),
            f, leadMinutes);
    }

    /// <summary>Giữ cho mã cũ: true = chỉ email đã gửi.</summary>
    public static BookingReminderListViewModel BuildList(IEnumerable<BookingReminderRecord> records, bool sentOnly, int leadMinutes) =>
        BuildList(records, sentOnly ? BookingReminderFilter.Sent : BookingReminderFilter.All, leadMinutes);

    private static string? Shorten(string? error)
    {
        var value = error?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        return value.Length <= ReservationEmailStatus.MaxErrorLength ? value : value[..ReservationEmailStatus.MaxErrorLength] + "…";
    }
}
