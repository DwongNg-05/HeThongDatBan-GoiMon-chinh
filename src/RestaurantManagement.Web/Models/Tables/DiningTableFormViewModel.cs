using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace RestaurantManagement.Web.Models.Tables;

public sealed class DiningTableFormViewModel : IValidatableObject
{
    public int Id { get; set; }

    [Display(Name = "Mã bàn")]
    [Required(ErrorMessage = "Nhập mã bàn.")]
    [StringLength(20, ErrorMessage = "Mã bàn tối đa 20 ký tự.")]
    [RegularExpression("^[A-Za-z0-9_-]+$", ErrorMessage = "Mã bàn chỉ gồm chữ, số, dấu gạch ngang hoặc gạch dưới.")]
    public string Code { get; set; } = string.Empty;

    [Display(Name = "Khu vực")]
    [Range(1, int.MaxValue, ErrorMessage = "Chọn khu vực cho bàn.")]
    public int AreaId { get; set; }

    [Display(Name = "Sức chứa tối thiểu")]
    [Range(1, 60, ErrorMessage = "Sức chứa tối thiểu từ 1 đến 60.")]
    public int MinCapacity { get; set; } = 1;

    [Display(Name = "Sức chứa tối đa")]
    [Range(1, 60, ErrorMessage = "Sức chứa tối đa từ 1 đến 60.")]
    public int MaxCapacity { get; set; } = 4;

    [Display(Name = "Loại bàn")]
    [Required]
    public string TableType { get; set; } = "Standard";

    [Display(Name = "Trạng thái hiện tại")]
    [Required]
    public string Status { get; set; } = "Available";

    public IReadOnlyList<SelectListItem> Areas { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (MinCapacity > MaxCapacity)
            yield return new ValidationResult("Sức chứa tối thiểu không được lớn hơn sức chứa tối đa.", [nameof(MinCapacity), nameof(MaxCapacity)]);

        if (TableType is not ("Standard" or "PrivateRoom"))
            yield return new ValidationResult("Loại bàn không hợp lệ.", [nameof(TableType)]);

        if (Status is not ("Available" or "Reserved" or "Serving" or "Cleaning"))
            yield return new ValidationResult("Trạng thái bàn không hợp lệ.", [nameof(Status)]);
    }
}
