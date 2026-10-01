using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RestaurantManagement.Data.Models;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Pages.DishCategories;

public class IndexModel(IMenuStore store) : PageModel
{
    public IReadOnlyList<DishCategory> DishCategory { get; private set; } = [];
    [TempData] public string? StatusMessage { get; set; }
    public void OnGet() => Categories = store.GetAllCategories().ToArray();
}
