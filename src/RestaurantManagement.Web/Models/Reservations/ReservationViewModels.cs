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

    public List<BookingAreaOption> Areas { get; set; } = new();
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
}
