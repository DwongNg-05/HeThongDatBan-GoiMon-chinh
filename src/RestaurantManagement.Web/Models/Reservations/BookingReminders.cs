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
    DateTime CreatedAtUtc);

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
    string? ErrorMessage);

/// <summary>Màn hình danh sách email nhắc lịch.</summary>
public sealed record BookingReminderListViewModel(
    IReadOnlyList<BookingReminderItem> Items,
    bool SentOnly,
    int LeadMinutes)
{
    public int SentCount => Items.Count(i => i.State == EmailDeliveryState.Sent);
}

/// <summary>Kết quả một lượt chạy tác vụ nhắc lịch.</summary>
public sealed record BookingReminderRunResult(int Queued, int Sent, int Failed)
{
    public int Attempted => Sent + Failed;

    public string Summary => Queued == 0 && Attempted == 0
        ? "Không có lượt đặt bàn nào đến giờ nhắc."
        : $"Đã xếp {Queued} email nhắc mới, gửi thành công {Sent}" + (Failed > 0 ? $", lỗi {Failed} (sẽ tự gửi lại sau 5 phút)." : ".");
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

    /// <summary>Nhãn trạng thái gửi nhắc trên chi tiết lượt đặt bàn (cột dbo.Reservations.ReminderEmailStatus).</summary>
    public static string DetailLabel(string? reminderStatus, string reservationStatus, string? email, DateTime startsAt, DateTime now)
    {
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
        if (reservationStatus != "Confirmed") return "Không gửi nhắc (chỉ nhắc lượt đã xác nhận)";
        if (startsAt <= now) return "Không gửi nhắc (đã qua giờ hẹn)";
        return startsAt > now.AddMinutes(DefaultLeadMinutes) ? "Chưa tới giờ nhắc" : "Đến giờ nhắc, đang chờ tác vụ gửi";
    }

    public static BookingReminderItem ToItem(BookingReminderRecord r)
    {
        var state = ReservationEmailStatus.StateOf(r.Status, r.AttemptCount);
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
            error);
    }

    public static BookingReminderListViewModel BuildList(IEnumerable<BookingReminderRecord> records, bool sentOnly, int leadMinutes) =>
        new(records
                .Where(r => !sentOnly || r.Status == "Sent")
                .Select(ToItem)
                .OrderByDescending(i => i.SentAt ?? i.QueuedAt).ThenByDescending(i => i.EmailId)
                .ToArray(),
            sentOnly, leadMinutes);

    private static string? Shorten(string? error)
    {
        var value = error?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        return value.Length <= ReservationEmailStatus.MaxErrorLength ? value : value[..ReservationEmailStatus.MaxErrorLength] + "…";
    }
}
