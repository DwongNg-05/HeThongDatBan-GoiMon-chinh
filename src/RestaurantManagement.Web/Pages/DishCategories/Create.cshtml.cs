using Microsoft.AspNetCore.Authorization;
using RestaurantManagement.Web.Security;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RestaurantManagement.Data.Models;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Pages.DishCategories;

// S1-04 Task 4: kiểm tra quyền ở máy chủ theo vai trò (docs/S1-04-Task4.md).
[Authorize(Roles = AppRoles.Manager)]
public class CreateModel(IMenuStore store) : PageModel
{
    [BindProperty] public string? Name { get; set; }
    [BindProperty] public bool IsActive { get; set; } = true;
    [BindProperty, Required(ErrorMessage = "Vui lòng nhập thứ tự hiển thị.")]
    [Range(0, int.MaxValue, ErrorMessage = "Thứ tự hiển thị phải là số nguyên không âm.")]
    public int? SortOrder { get; set; }
    [TempData] public string? StatusMessage { get; set; }

    public void OnGet()
    {
        var max = store.GetAllCategories().Select(n => n.SortOrder).DefaultIfEmpty(0).Max();
        SortOrder = max == int.MaxValue ? max : max + 1;
    }

    public IActionResult OnPost()
    {
        if (!ModelState.IsValid) return Page();
        try
        {
            store.AddCategory(new DishCategory { Name = Name ?? "", IsActive = IsActive, SortOrder = SortOrder!.Value });
        }
        catch (ValidationException ex)
        {
            ModelState.AddModelError(nameof(Name), ex.Message);
            return Page();
        }
        StatusMessage = "Thêm nhóm món thành công.";
        return RedirectToPage("Index");
    }
}
