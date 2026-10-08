using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;

namespace RestaurantManagement.Web.ViewModels;

public sealed class CreateEmployeeAccountViewModel
{
    [Required(ErrorMessage = "Nhập họ tên nhân viên.")]
    [StringLength(100)]
    [Display(Name = "Họ tên")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nhập số điện thoại.")]
    [RegularExpression(@"^0\d{9}$", ErrorMessage = "Số điện thoại phải gồm 10 chữ số và bắt đầu bằng số 0.")]
    [Remote(action: "CheckPhoneNumber", controller: "EmployeeAccounts")]
    [Display(Name = "Số điện thoại")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nhập tên đăng nhập.")]
    [StringLength(50, MinimumLength = 3, ErrorMessage = "Tên đăng nhập dài từ 3 đến 50 ký tự.")]
    [Remote(action: "CheckUserName", controller: "EmployeeAccounts")]
    [Display(Name = "Tên đăng nhập")]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Chọn một vai trò.")]
    [Display(Name = "Vai trò")]
    public int? RoleId { get; set; }

    [ValidateNever]
    public IReadOnlyList<SelectListItem> Roles { get; set; } = Array.Empty<SelectListItem>();
}
