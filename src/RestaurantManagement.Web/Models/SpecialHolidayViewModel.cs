using System.ComponentModel.DataAnnotations;

namespace RestaurantManagement.Web.Models;

public class SpecialHolidayViewModel
{
    public int? Id { get; set; }
    [Required(ErrorMessage = "Vui lòng chọn ngày nghỉ.")]
    [DataType(DataType.Date)]
    [Display(Name = "Ngày nghỉ")]
    public DateOnly? HolidayDate { get; set; }
    [Required(ErrorMessage = "Vui lòng nhập tên ngày nghỉ.")]
    [StringLength(150, ErrorMessage = "Tên ngày nghỉ tối đa 150 ký tự.")]
    [Display(Name = "Tên ngày nghỉ")]
    public string Name { get; set; } = "";
    [Display(Name = "Áp dụng")]
    public bool IsActive { get; set; } = true;
}

public class DailyBookingCalendarViewModel
{
    [DataType(DataType.Date)]
    [Display(Name = "Ngày xem lịch")]
    public DateOnly? Date { get; set; }
    public string? HolidayName { get; set; }
    public OpeningDayViewModel? WeeklyDay { get; set; }
    public IReadOnlyList<string> Slots => HolidayName is not null ? [] : WeeklyDay?.BookingSlots ?? [];
}
