using Microsoft.AspNetCore.Authorization;
using RestaurantManagement.Web.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RestaurantManagement.Data.Models;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Pages.DishCategories;

// S1-04 Task 4: kiểm tra quyền ở máy chủ theo vai trò (docs/S1-04-Task4.md).
[Authorize(Roles = AppRoles.Manager)]
public class IndexModel(IMenuStore store) : PageModel
{
    public IReadOnlyList<DishCategory> Categories { get; private set; } = [];
    [TempData] public string? StatusMessage { get; set; }
    public void OnGet() => Categories = store.GetAllCategories().ToArray();
}
