using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Pages.DishCategories;

public class EditModel(IMenuStore store) : PageModel
{
    [BindProperty] public string? Name { get; set; }
    [TempData] public string? StatusMessage { get; set; }

    public IActionResult OnGet(int id)
    {
        var category = store.GetCategory(id);
        if (category is null) return NotFound();
        Name = category.Name;
        return Page();
    }

    public IActionResult OnPost(int id)
    {
        if (store.GetCategory(id) is null) return NotFound();
        if (!ModelState.IsValid) return Page();
        try
        {
            if (!store.RenameCategory(id, Name)) return NotFound();
        }
        catch (ValidationException ex)
        {
            ModelState.AddModelError(nameof(Name), ex.Message);
            return Page();
        }
        StatusMessage = "Sửa tên nhóm món thành công.";
        return RedirectToPage("Index");
    }
}
