using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RestaurantManagement.Data.Models;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Pages.DishCategories;

public class ReorderModel(IMenuStore store) : PageModel
{
    [BindProperty] public List<int> OrderedIds { get; set; } = [];
    [BindProperty] public List<int> OriginalIds { get; set; } = [];
    [TempData] public string? StatusMessage { get; set; }
    public IReadOnlyList<DishCategory> DishCategory { get; private set; } = [];

    public void OnGet() => LoadCategories();

    public IActionResult OnPost()
    {
        if (ModelState.IsValid)
        {
            try
            {
                store.SaveCategoryOrder(OrderedIds, OriginalIds);
                StatusMessage = "Lưu thứ tự nhóm món thành công.";
                return RedirectToPage("Index");
            }
            catch (ValidationException ex) { ModelState.AddModelError("", ex.Message); }
        }
        LoadCategories();
        return Page();
    }

    private void LoadCategories()
    {
        Categories = store.GetAllCategories().ToArray();
        OriginalIds = Categories.Select(n => n.Id).ToList();
    }
}
