using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace RestaurantManagement.Web.Models;

public class OpeningHoursViewModel : IValidatableObject
{
    [Display(Name = "Thời lượng giữ bàn (phút)")]
    [Required(ErrorMessage = "Vui lòng nhập thời lượng giữ bàn.")]
    [Range(30, 360, ErrorMessage = "Thời lượng giữ bàn phải từ 30 đến 360 phút.")]
    public int? DefaultBookingMinutes { get; set; } = 90;

    public List<OpeningDayViewModel> Days { get; set; } = [];
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public List<SpecialHolidayViewModel> Holidays { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Days.Count != 7 || !Days.Select(d => d.DayOfWeek).Order().SequenceEqual(Enumerable.Range(1, 7)))
            yield return new ValidationResult("Cấu hình phải có đủ 7 ngày, mỗi ngày xuất hiện một lần.");
        for (var i = 0; i < Days.Count; i++)
        {
            var day = Days[i];
            if (day.IsClosed) continue;
            var validOpen = OpeningDayViewModel.TryTime(day.OpensAt, out var open);
            var validClose = OpeningDayViewModel.TryTime(day.ClosesAt, out var close);
            if (!validOpen)
                yield return new ValidationResult("Vui lòng nhập giờ mở cửa hợp lệ (HH:mm).", [$"Days[{i}].OpensAt"]);
            if (!validClose)
                yield return new ValidationResult("Vui lòng nhập giờ đóng cửa hợp lệ (HH:mm).", [$"Days[{i}].ClosesAt"]);
            if (validOpen && validClose && close <= open)
                yield return new ValidationResult("Giờ đóng cửa phải lớn hơn giờ mở cửa.", [$"Days[{i}].ClosesAt"]);
        }
    }
}

public class OpeningDayViewModel
{
    public int DayOfWeek { get; set; }
    public bool IsClosed { get; set; }
    public string? OpensAt { get; set; }
    public string? ClosesAt { get; set; }
    public string DayName => DayOfWeek == 7 ? "Chủ nhật" : $"Thứ {DayOfWeek + 1}";
    public IReadOnlyList<string> BookingSlots
    {
        get
        {
            if (IsClosed || !TryTime(OpensAt, out var open) || !TryTime(ClosesAt, out var close) || close <= open)
                return [];
            var slots = new List<string>();
            // Use elapsed minutes to avoid wrapping TimeOnly back to midnight.
            for (var minute = open.Hour * 60 + open.Minute; minute < close.Hour * 60 + close.Minute; minute += 30)
                slots.Add($"{minute / 60:00}:{minute % 60:00}");
            return slots;
        }
    }
    public static bool TryTime(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
}
