using System.ComponentModel.DataAnnotations;

namespace RestaurantManagement.Web.Models.Reservations;

public sealed class TableReservationScheduleViewModel
{
    public DateOnly Date { get; init; }
    public List<TableReservationScheduleRowViewModel> Tables { get; } = [];
}

public sealed class TableReservationScheduleRowViewModel
{
    public int TableId { get; init; }
    public string TableCode { get; init; } = string.Empty;
    public string AreaName { get; init; } = string.Empty;
    public int MaxCapacity { get; init; }
    public List<TableReservationScheduleItemViewModel> Reservations { get; } = [];
}

public sealed class TableReservationScheduleItemViewModel
{
    public long Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public DateTime StartsAt { get; init; }
    public DateTime EndsAt { get; init; }
    public DateTime? HoldExtendedUntil { get; init; }
    public DateTime? ArrivedAt { get; init; }
    public string Status { get; init; } = string.Empty;
}

public sealed class ManagedTableReservationCreateViewModel : NoShowWarningInput
{
    [Required(ErrorMessage = "Vui lòng chọn bàn.")]
    [Display(Name = "Bàn")]
    public int? TableId { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn ngày.")]
    [Display(Name = "Ngày đặt bàn")]
    public DateOnly? ReservationDate { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn giờ bắt đầu.")]
    [Display(Name = "Giờ bắt đầu")]
    public TimeOnly? StartTime { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên khách hàng.")]
    [StringLength(100, ErrorMessage = "Họ tên không được vượt quá 100 ký tự.")]
    [Display(Name = "Họ tên khách")]
    public string CustomerName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập số điện thoại.")]
    [RegularExpression(@"^0\d{9}$", ErrorMessage = "Số điện thoại phải gồm đúng 10 chữ số và bắt đầu bằng 0.")]
    [Display(Name = "Số điện thoại")]
    private string phone = string.Empty; public string Phone { get => phone; set => phone = RestaurantManagement.Web.Services.ReservationPhoneNormalizer.Normalize(value) ?? value ?? string.Empty; }

    [Required]
    [RegularExpression("Pending|Confirmed", ErrorMessage = "Trạng thái ban đầu không hợp lệ.")]
    [Display(Name = "Trạng thái ban đầu")]
    public string InitialStatus { get; set; } = "Pending";

    public List<ManagedTableOptionViewModel> Tables { get; } = [];

    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public bool HasConflict { get; set; }

    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public List<string> SuggestedStartTimes { get; } = [];
}

public sealed class ManagedTableOptionViewModel
{
    public int Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string AreaName { get; init; } = string.Empty;
    public int MaxCapacity { get; init; }
}
