using System.ComponentModel.DataAnnotations;

namespace RestaurantManagement.Web.Models.Reservations;

public sealed class PendingReservation
{
    public long Id { get; init; }
    public string Code { get; init; } = "";
    public string CustomerName { get; init; } = "";
    public int GuestCount { get; init; }
    public DateTime StartsAt { get; init; }
    public DateTime EndsAt { get; init; }
    public string? AreaName { get; init; }
}
public sealed record SuggestedTable(int Id, string Code, int MaxCapacity, string AreaName);
public sealed record ReservedSlot(long Id, string ReservationCode, string TableCode, int MaxCapacity,
    string AreaName, DateTime StartsAt, DateTime EndsAt, int GuestCount);
public sealed class ReservationConfirmation
{
    public long Id { get; init; }
    public string Code { get; init; } = "";
    public string CustomerName { get; init; } = "";
    public string Phone { get; init; } = "";
    public string? Email { get; init; }
    public int GuestCount { get; init; }
    public DateTime StartsAt { get; init; }
    public DateTime EndsAt { get; init; }
    public string Status { get; init; } = "";
    public string? AreaName { get; init; }
    public string? TableCode { get; init; }
    public string? EmailStatus { get; init; }
    public string? EmailError { get; init; }
    public int AttemptCount { get; init; }
    public List<SuggestedTable> Tables { get; } = [];
    public bool CanConfirm => Status == "Pending" && StartsAt > VietnamTime.Now;
    [Required(ErrorMessage = "Vui lòng chọn một bàn trong danh sách gợi ý.")]
    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn bàn hợp lệ.")]
    public int? SelectedTableId { get; set; }
    public string StatusLabel => Status switch
    {
        "Pending" => "Chờ xác nhận", "Confirmed" => "Đã xác nhận", "Arrived" => "Khách đã đến",
        "Cancelled" => "Đã hủy", "Rejected" => "Đã từ chối", "NoShow" => "Khách không đến", _ => Status
    };
    public string NotificationLabel => EmailStatus switch
    {
        "Sent" => "Đã chuyển email đến máy chủ gửi thư",
        "Processing" => "Đang gửi email",
        "Failed" => "Gửi email thất bại",
        "Pending" when EmailError != null => "Gửi email lỗi; hệ thống sẽ thử lại",
        "Pending" => "Email đang chờ gửi", "Cancelled" => "Đã dừng gửi", _ => "Chưa có thông báo xác nhận"
    };
}
