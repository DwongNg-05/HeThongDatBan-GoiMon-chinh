using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace RestaurantManagement.Web.Models.Tables;

public sealed class DiningTableFormViewModel : IValidatableObject
{
    public int Id { get; set; }

    [Display(Name = "Tên / mã bàn")]
    [Required(ErrorMessage = "Vui lòng nhập tên bàn, ví dụ A01.")]
    [StringLength(20, ErrorMessage = "Mã bàn tối đa 20 ký tự.")]
    [RegularExpression("^[A-Za-z0-9_-]+$", ErrorMessage = "Mã bàn chỉ gồm chữ, số, dấu gạch ngang hoặc gạch dưới.")]
    public string Code { get; set; } = string.Empty;

    [Display(Name = "Khu vực")]
    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn khu vực đặt bàn này.")]
    public int AreaId { get; set; }

    [Display(Name = "Số khách ít nhất")]
    [Range(1, 60, ErrorMessage = "Số khách ít nhất phải từ 1 đến 60.")]
    public int MinCapacity { get; set; } = 1;

    [Display(Name = "Số khách nhiều nhất")]
    [Range(1, 60, ErrorMessage = "Số khách nhiều nhất phải từ 1 đến 60.")]
    public int MaxCapacity { get; set; } = 4;

    [Display(Name = "Loại bàn")]
    [Required]
    public string TableType { get; set; } = "Standard";

    [Display(Name = "Tình trạng bàn lúc này")]
    [Required]
    public string Status { get; set; } = "Available";

    public IReadOnlyList<SelectListItem> Areas { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (MinCapacity > MaxCapacity)
            yield return new ValidationResult("Số khách ít nhất không được lớn hơn số khách nhiều nhất.", [nameof(MinCapacity), nameof(MaxCapacity)]);

        if (TableType is not ("Standard" or "PrivateRoom"))
            yield return new ValidationResult("Loại bàn không hợp lệ.", [nameof(TableType)]);

        if (Status is not ("Available" or "Reserved" or "Serving" or "Cleaning"))
            yield return new ValidationResult("Trạng thái bàn không hợp lệ.", [nameof(Status)]);
    }
}
