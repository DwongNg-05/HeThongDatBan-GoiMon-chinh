using System.ComponentModel.DataAnnotations;

namespace RestaurantManagement.Web.Models.Areas;

public class AreaViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên khu vực.")]
    [StringLength(80, ErrorMessage = "Tên khu vực không được vượt quá 80 ký tự.")]
    [Display(Name = "Tên khu vực")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Thứ tự hiển thị")]
    public int SortOrder { get; set; }

    [StringLength(500, ErrorMessage = "Ghi chú không được vượt quá 500 ký tự.")]
    [Display(Name = "Ghi chú")]
    public string? Notes { get; set; }

    [Display(Name = "Trạng thái")]
    public bool IsActive { get; set; } = true;
}

public class AreaFormViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên khu vực.")]
    [StringLength(80, ErrorMessage = "Tên khu vực không được vượt quá 80 ký tự.")]
    [Display(Name = "Tên khu vực")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập thứ tự hiển thị.")]
    [Range(0, int.MaxValue, ErrorMessage = "Thứ tự hiển thị phải là số nguyên không âm.")]
    [Display(Name = "Thứ tự hiển thị")]
    public int? SortOrder { get; set; }

    [StringLength(500, ErrorMessage = "Ghi chú không được vượt quá 500 ký tự.")]
    [Display(Name = "Ghi chú")]
    public string? Notes { get; set; }
}

public class AreaListViewModel
{
    public List<AreaViewModel> Areas { get; set; } = new();
}
