namespace RestaurantManagement.Web.Models.Reservations;

/// <summary>Trang khách xác nhận đặt bàn từ nút trong email (/Reservations/Confirm?token=...).</summary>
/// <param name="Token">Mã bảo mật trong liên kết email (gửi lại khi khách bấm "Xác nhận").</param>
/// <param name="Reservation">Lượt đặt bàn; null khi liên kết sai, hết hiệu lực hoặc lượt đặt bàn đã bị xoá.</param>
public sealed record ReservationConfirmViewModel(string Token, ReservationListItemViewModel? Reservation)
{
    public const string SuccessKey = "BookingConfirmSuccess";
    public const string ErrorKey = "BookingConfirmError";

    public bool CanConfirm => Reservation is { Status: "Pending", TableCode: not null }
        && Reservation.StartsAt > VietnamTime.Now;

    public bool IsConfirmed => Reservation?.Status is "Confirmed" or "Arrived";

    /// <summary>Mã khách đọc khi đến quán: mã bàn đã chọn, lượt cũ chưa có bàn dùng mã nội bộ.</summary>
    public string? DisplayCode => Reservation is null ? null : Reservation.TableCode ?? Reservation.Code;

    /// <summary>Lý do không xác nhận được (khi không còn chờ xác nhận).</summary>
    public string? Notice => Reservation switch
    {
        null => "Liên kết xác nhận không hợp lệ hoặc đã hết hiệu lực. Vui lòng mở lại email đặt bàn hoặc liên hệ nhà hàng.",
        { Status: "Confirmed" or "Arrived" } => null,
        { Status: "Pending", TableCode: null } => "Lượt đặt bàn chưa có bàn. Nhà hàng sẽ liên hệ để sắp xếp và xác nhận giúp bạn.",
        { Status: "Pending" } when !CanConfirm => "Đã quá giờ đặt bàn nên không thể xác nhận nữa. Vui lòng đặt bàn mới.",
        { Status: "Pending" } => null,
        var other => $"Lượt đặt bàn này hiện ở trạng thái “{other.StatusLabel}” nên không thể xác nhận."
    };
}
