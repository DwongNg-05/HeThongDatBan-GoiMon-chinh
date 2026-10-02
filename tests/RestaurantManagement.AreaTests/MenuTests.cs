using RestaurantManagement.Data.Models;
using RestaurantManagement.Web.Services;

internal static class MenuTests
{
    internal static void Run(Action<bool, string> check)
    {
        var store = new InMemoryMenuStore();
        string[] expected = ["Khai vị", "Món chính", "Lẩu", "Tráng miệng", "Đồ uống"];
        var menu = store.GetMenuByCategory();
        check(menu.Select(n => n.Name).SequenceEqual(expected), "Menu: exactly five default groups in required order");
        check(menu.All(n => n.Dishes.Count > 0 && n.Dishes.All(m => m.CategoryId == n.Id)), "Menu: sample dishes belong to their displayed group");
        check(menu.All(n => store.GetDishesByCategory(n.Id).All(m => m.CategoryId == n.Id)), "Menu: query dishes by group");
        var publicMenu = new RestaurantManagement.Web.Controllers.MenuController(store);
        int[] PublicIds() => ((IReadOnlyList<PublicMenuCategory>)((Microsoft.AspNetCore.Mvc.ViewResult)publicMenu.Index()).Model!).Select(n => n.Id).ToArray();
        var orderPage = new RestaurantManagement.Web.Pages.Ordering.IndexModel(store);
        orderPage.OnGet();
        check(PublicIds().SequenceEqual(orderPage.Categories.Select(n => n.Id)), "Menu: public and ordering pages have identical group order");
        var first = store.GetAllCategories().First();
        first.SortOrder = 100;
        check(store.GetMenuByCategory().Last().Id == first.Id, "Menu: explicit display order takes precedence over ID");
        foreach (var dish in store.GetDishesByCategory(first.Id)) dish.Status = DishStatus.Discontinued;
        check(store.GetMenuByCategory().Single(n => n.Id == first.Id).Dishes.Count == 0, "Menu: group with no available dishes remains visible");
        var empty = store.AddCategory(new DishCategory { Name = "Nhóm rỗng", SortOrder = 6 });
        check(store.GetMenuByCategory().Any(n => n.Id == empty.Id && n.Dishes.Count == 0), "Menu: group with no dishes remains visible");
        var inactive = store.GetAllCategories().First();
        inactive.IsActive = false;
        check(store.GetMenuByCategory().All(n => n.Id != inactive.Id), "Menu: inactive group and its dishes are excluded");
        orderPage.OnGet();
        check(PublicIds().SequenceEqual(orderPage.Categories.Select(n => n.Id)), "Menu: both pages remain consistent after status/order changes");
        var invalidRejected = false;
        try { store.AddDish(new Dish { CategoryId = int.MaxValue }); }
        catch (ArgumentException) { invalidRejected = true; }
        check(invalidRejected, "Menu: dish cannot reference a missing group");
        var dishToEdit = store.GetAllDishes().First();
        invalidRejected = false;
        try { store.UpdateDish(new Dish { Id = dishToEdit.Id, CategoryId = int.MaxValue }); }
        catch (ArgumentException) { invalidRejected = true; }
        check(invalidRejected, "Menu: edit cannot move a dish to a missing group");
        foreach (var group in store.GetAllCategories()) group.IsActive = false;
        check(store.GetMenuByCategory().Count == 0, "Menu: no active groups returns an empty menu");
    }
}
