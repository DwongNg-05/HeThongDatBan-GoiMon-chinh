using Microsoft.AspNetCore.Mvc.RazorPages;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Pages.Ordering;

public class IndexModel(IMenuStore store) : PageModel
{
    public IReadOnlyList<CategoryWithDishes> Categories { get; private set; } = [];

    public void OnGet() => Categories = store.GetMenuByCategory();
}
