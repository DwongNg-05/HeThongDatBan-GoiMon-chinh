using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RestaurantManagement.Data.Models;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Pages.DishCategories;

public class StatusModel(IMenuStore store) : PageModel
{
    public DishCategory Category { get; private set; } = new();
    public int DishCount { get; private set; }
    [TempData] public string? StatusMessage { get; set; }

    public IActionResult OnGet(int id)
    {
        var category = store.GetCategory(id);
        if (category is null) return NotFound();
        Category = category;
        DishCount = store.GetDishesByCategory(id).Count();
        return Page();
    }

    public IActionResult OnPostDeactivate(int id) => Save(id, false);
    public IActionResult OnPostReactivate(int id) => Save(id, true);

    private IActionResult Save(int id, bool active)
    {
        if (!ModelState.IsValid) return BadRequest();
        if (!store.SetCategoryActive(id, active)) return NotFound();
        StatusMessage = active ? "Đã bật lại nhóm món." : "Đã ngừng sử dụng nhóm món. Dữ liệu nhóm và món ăn được giữ nguyên.";
        return RedirectToPage("Index");
    }
}
