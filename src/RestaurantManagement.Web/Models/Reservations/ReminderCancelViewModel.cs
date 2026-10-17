namespace RestaurantManagement.Web.Models.Reservations;

/// <summary>
/// S4-08 Task 4: trang khách huỷ lượt đặt bàn từ đường dẫn trong email nhắc (/Reservations/CancelFromEmail?token=...).
/// </summary>
/// <param name="Token">Mã bảo mật trong đường dẫn (gửi lại khi khách bấm "Xác nhận huỷ").</param>
/// <param name="Reservation">Lượt đặt bàn đúng với mã bảo mật; null khi đường dẫn sai hoặc lượt không còn tồn tại.</param>
/// <param name="NowVietnam">Thời điểm mở trang (giờ Việt Nam), dùng để xét điều kiện huỷ.</param>
public sealed record ReminderCancelViewModel(string Token, ReservationListItemViewModel? Reservation, DateTime NowVietnam)
{
    public const string ResultKey = "ReminderCancelResult";

    /// <summary>Lý do không huỷ được (NotFound, AlreadyCancelled, Arrived, TooLate, NotCancellable); null = huỷ được.</summary>
    public string? BlockReason => BookingReminderRules.CancelBlockReason(Reservation?.Status,
        Reservation?.StartsAt ?? DateTime.MinValue, NowVietnam);

    public bool CanCancel => BlockReason is null;

    /// <summary>Thông báo khi không huỷ được.</summary>
    public string? Notice => BlockReason is { } reason ? BookingReminderRules.CancelResultMessage(reason) : null;
}
