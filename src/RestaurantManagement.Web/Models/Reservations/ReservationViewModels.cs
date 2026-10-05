using System.ComponentModel.DataAnnotations;

namespace RestaurantManagement.Web.Models.Reservations;

public class ReservationCreateViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập tên khách hàng.")]
    [StringLength(100)]
    [Display(Name = "Tên khách hàng")]
    public string CustomerName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập số điện thoại.")]
    [RegularExpression(
        @"^0[0-9]{9}$",
        ErrorMessage = "Số điện thoại phải gồm 10 chữ số.")]
    [Display(Name = "Số điện thoại")]
    public string Phone { get; set; } = string.Empty;

    [Range(
        1,
        20,
        ErrorMessage = "Số khách phải từ 1 đến 20.")]
    [Display(Name = "Số khách")]
    public int GuestCount { get; set; } = 2;

    [Required(ErrorMessage = "Vui lòng chọn thời gian đặt bàn.")]
    [Display(Name = "Thời gian")]
    [Microsoft.AspNetCore.Mvc.ModelBinder(BinderType = typeof(VietnamBookingTimeBinder))]
    public DateTime StartsAt { get; set; }

    [Display(Name = "Khu vực")]
    public int? PreferredAreaId { get; set; }

    [EmailAddress(
        ErrorMessage = "Email không hợp lệ.")]
    [Display(Name = "Email")]
    public string? Email { get; set; }

    [StringLength(500)]
    [Display(Name = "Ghi chú")]
    public string? Notes { get; set; }

    /// <summary>Bàn khách chọn. Mã bàn (ví dụ A05) là mã khách nhận trên trang xác nhận và trong email.</summary>
    [Display(Name = "Chọn bàn")]
    public int? TableId { get; set; }

    public List<BookingAreaOption> Areas { get; set; } = new();

    /// <summary>Bàn còn trống cho giờ, số khách và khu vực đang chọn.</summary>
    public List<BookingTableOption> Tables { get; set; } = new();
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public DateOnly MinimumReservationDate { get; set; }

    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public DateOnly MaximumReservationDate { get; set; }
}

public class BookingTableOption
{
    public int Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string AreaName { get; set; } = string.Empty;

    public int MaxCapacity { get; set; }

    /// <summary>Ví dụ: "A05 · Tầng 1 · tối đa 4 khách".</summary>
    public string Label => $"{Code} · {AreaName} · tối đa {MaxCapacity} khách";
}

public class BookingAreaOption
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; }
}

public class ReservationListItemViewModel
{
    public long Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string CustomerName { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    /// <summary>Email khách để lại khi đặt bàn (có thể trống).</summary>
    public string? Email { get; set; }

    public int GuestCount { get; set; }

    public string? AreaName { get; set; }

    public DateTime StartsAt { get; set; }

    public DateTime EndsAt { get; set; }

    public string Status { get; set; } = string.Empty;

    /// <summary>Mã bàn khách đã chọn hoặc nhân viên đã xếp (ví dụ A05); trống khi chưa có bàn.</summary>
    public string? TableCode { get; set; }

    /// <summary>Khu vực của bàn (theo bàn hiện tại).</summary>
    public string? TableAreaName { get; set; }

    public string? Notes { get; set; }

    /// <summary>Nhãn tiếng Việt của trạng thái đặt bàn (khác với trạng thái email).</summary>
    public string StatusLabel => ReservationStatusDisplay.Label(Status);

    /// <summary>Lý do huỷ (khi lượt đặt bàn đã bị huỷ).</summary>
    public string? CancelReason { get; set; }

    /// <summary>Quản lý chỉ huỷ được lượt đang chờ xác nhận hoặc đã xác nhận.</summary>
    public bool CanCancel => Status is "Pending" or "Confirmed";

    /// <summary>
    /// S2-09 Task 2: trạng thái email xác nhận, cập nhật sau mỗi lần thử gửi
    /// (Pending, Sending, Retrying, Sent, Failed, Cancelled; trống khi khách không nhập email).
    /// </summary>
    public string? ConfirmationEmailStatus { get; set; }

    /// <summary>Số lần đã thử gửi email xác nhận (lần gửi đầu + các lần gửi lại).</summary>
    public int ConfirmationEmailAttempts { get; set; }

    /// <summary>Nhãn ngắn cho danh sách / chi tiết đặt bàn.</summary>
    public string? ConfirmationEmailLabel => ConfirmationEmailStatus switch
    {
        "Pending" => "Email: đang chờ gửi",
        "Sending" => $"Email: đang gửi (lần thử {ConfirmationEmailAttempts}/{ReservationEmailStatus.MaxAttempts})",
        "Retrying" => $"Email: chưa gửi được, sẽ tự gửi lại (đã thử {ConfirmationEmailAttempts}/{ReservationEmailStatus.MaxAttempts})",
        "Sent" => ConfirmationEmailAttempts > 1 ? $"Email: đã gửi (ở lần thử thứ {ConfirmationEmailAttempts})" : "Email: đã gửi",
        "Failed" => $"Email: gửi thất bại sau {ConfirmationEmailAttempts} lần thử",
        "Cancelled" => "Email: không gửi lại (lượt đặt bàn đã kết thúc)",
        _ => null
    };

    /// <summary>Quản lý chỉ xoá hẳn được lượt đã kết thúc: đã huỷ, bị từ chối, khách không đến.</summary>
    public bool CanDelete => Status is "Cancelled" or "Rejected" or "NoShow";
}
