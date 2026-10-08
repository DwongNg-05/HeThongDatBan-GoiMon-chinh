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
public class DeleteModel(IMenuStore store) : PageModel
{
    public DishCategory Category { get; private set; } = new();
    public IReadOnlyList<Dish> Dishes { get; private set; } = [];
    [TempData] public string? StatusMessage { get; set; }

    public IActionResult OnGet(int id) => Load(id);

    public IActionResult OnPost(int id)
    {
        if (!ModelState.IsValid) return Load(id);
        try
        {
            if (!store.DeleteCategory(id)) return NotFound();
        }
        catch (ValidationException ex)
        {
            ModelState.AddModelError("", ex.Message);
            return Load(id);
        }
        StatusMessage = "Xóa nhóm món thành công.";
        return RedirectToPage("Index");
    }

    private IActionResult Load(int id)
    {
        var category = store.GetCategory(id);
        if (category is null) return NotFound();
        Category = category;
        Dishes = store.GetDishesByCategory(id).ToArray();
        return Page();
    }
}
