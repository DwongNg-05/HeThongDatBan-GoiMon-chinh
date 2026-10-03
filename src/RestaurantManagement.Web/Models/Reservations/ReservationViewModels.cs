using System.ComponentModel.DataAnnotations;

namespace RestaurantManagement.Web.Models.Reservations;

public class ReservationCreateViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập tên khách hàng.")]
    [StringLength(100)]
    [Display(Name = "Tên khách hàng")]
    public string CustomerName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập số điện thoại.")]
    [Display(Name = "Số điện thoại")]
    public string Phone { get; set; } = string.Empty;

    [Display(Name = "Số khách")]
    public int? GuestCount { get; set; } = 2;

    [Required(ErrorMessage = "Vui lòng chọn ngày đặt bàn.")]
    [Display(Name = "Ngày đặt bàn")]
    public DateOnly? ReservationDate { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn khung giờ.")]
    [Display(Name = "Khung giờ")]
    public TimeOnly? ReservationTime { get; set; }

    [Display(Name = "Khu vực")]
    public int? PreferredAreaId { get; set; }

    [StringLength(500)]
    [Display(Name = "Ghi chú")]
    public string? Notes { get; set; }

    public List<BookingAreaOption> Areas { get; set; } = new();
}

public class ReservationConfirmationViewModel
{
    public string Code { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public int GuestCount { get; init; }
    public DateOnly ReservationDate { get; init; }
    public TimeOnly ReservationTime { get; init; }
    public string AreaName { get; init; } = "Không yêu cầu";
    public string? Notes { get; init; }
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

    public int GuestCount { get; set; }

    public string? AreaName { get; set; }

    public DateTime StartsAt { get; set; }

    public DateTime EndsAt { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? Notes { get; set; }
}
