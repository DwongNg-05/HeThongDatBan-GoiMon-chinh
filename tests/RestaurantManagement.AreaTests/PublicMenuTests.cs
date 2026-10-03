using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Controllers;
using RestaurantManagement.Data.Models;
using RestaurantManagement.Web.Services;

/// <summary>S2-01 Task 1: thực đơn công khai theo nhóm, ảnh, mô tả, giá VND, không cần đăng nhập.</summary>
internal static class PublicMenuTests
{
    internal static void Run(Action<bool, string> check)
    {
        check(MoneyFormat.Vnd(45000) == "45.000 ₫", "Public menu: VND uses dot grouping and ₫");
        check(MoneyFormat.Vnd(250000) == "250.000 ₫" && MoneyFormat.Vnd(1500000) == "1.500.000 ₫", "Public menu: six- and seven-digit VND prices");
        check(MoneyFormat.Vnd(900) == "900 ₫", "Public menu: price under one thousand has no separator");

        check(DishImage.Resolve("/images/thuc-don/lau.svg") == "/images/thuc-don/lau.svg", "Public menu: local image path kept");
        check(DishImage.Resolve(null) == DishImage.DefaultImage && DishImage.Resolve("  ") == DishImage.DefaultImage, "Public menu: missing image uses default");
        check(new[] { "javascript:alert(1)", "//evil.example/a.png", "http://example.com/a.png", "uploads\\a.png" }
            .All(p => DishImage.Resolve(p) == DishImage.DefaultImage), "Public menu: unsafe image URLs replaced by default");
        check(DishImage.Resolve("https://cdn.example.com/mon/1.jpg") == "https://cdn.example.com/mon/1.jpg", "Public menu: https image allowed");

        var store = new InMemoryMenuStore();
        string[] expected = ["Khai vị", "Món chính", "Lẩu", "Tráng miệng", "Đồ uống"];
        var menu = store.GetPublicMenu();
        check(menu.Select(n => n.Name).SequenceEqual(expected), "Public menu: groups in display order");
        check(menu.All(n => n.Dishes.Count > 0 && n.Dishes.All(m =>
                m.Name.Length > 0 && m.ShortDescription.Length > 0 && m.PriceVnd > 0 && m.ImageUrl.StartsWith("/images/thuc-don/")
                && m.DisplayPrice.EndsWith(" ₫") && m.Unit.Length > 0)),
            "Public menu: every dish has image, name, description, VND price and unit");
        var springRoll = menu[0].Dishes.Single();
        check(springRoll is { Name: "Gỏi cuốn", PriceVnd: 45000, DisplayPrice: "45.000 ₫", SoldOutToday: false }, "Public menu: dish data matches store");

        var appetizerCategory = store.GetAllCategories().First();
        var newDish = store.AddDish(new Dish
        {
            Name = "Chả giò", CategoryId = appetizerCategory.Id, PriceVnd = 55000, Unit = "Phần",
            ShortDescription = "  Giòn rụm  ", PrepMinutes = 10
        });
        var newDishShown = store.GetPublicMenu()[0].Dishes.Single(m => m.Id == newDish.Id);
        check(newDishShown.ImageUrl == DishImage.DefaultImage && newDishShown.ShortDescription == "Giòn rụm", "Public menu: new dish without image uses default; description trimmed");
        check(store.GetPublicMenu()[0].Dishes.Select(m => m.Id).SequenceEqual(new[] { springRoll.Id, newDish.Id }), "Public menu: dishes in stable order within a group");

        newDish.Status = DishStatus.Discontinued;
        check(store.GetPublicMenu().SelectMany(n => n.Dishes).All(m => m.Id != newDish.Id), "Public menu: stopped dish hidden");

        var hiddenCategory = store.GetAllCategories().Last();
        store.SetCategoryActive(hiddenCategory.Id, false);
        var afterHiding = store.GetPublicMenu();
        check(afterHiding.All(n => n.Id != hiddenCategory.Id) && afterHiding.Count == 4, "Public menu: inactive group hidden with its dishes");

        var emptyCategory = store.AddCategory(new DishCategory { Name = "Món theo mùa", SortOrder = 10 });
        check(store.GetPublicMenu().Any(n => n.Id == emptyCategory.Id && n.Dishes.Count == 0), "Public menu: active empty group still listed");

        var expectedIds = store.GetPublicMenu().Select(n => n.Id).ToArray();
        var viewModel = (new MenuController(store).Index() as ViewResult)?.Model as RestaurantManagement.Web.Models.PublicMenuViewModel;
        check(viewModel is { IsSearching: false, MenuIsEmpty: false } && viewModel.Categories.Select(n => n.Id).SequenceEqual(expectedIds),
            "Public menu: MenuController.Index returns public menu view");
        var apiModel = (new MenuApiController(store).List().Result as OkObjectResult)?.Value as IReadOnlyList<PublicMenuCategory>;
        check(apiModel is not null && apiModel.Select(n => n.Id).SequenceEqual(expectedIds), "Public menu: API controller returns the same groups");
        check(typeof(MenuController).IsDefined(typeof(AllowAnonymousAttribute), inherit: true)
            && typeof(MenuApiController).IsDefined(typeof(AllowAnonymousAttribute), inherit: true),
            "Public menu: /Menu and /api/menu allow anonymous access");
        check(!typeof(RestaurantManagement.Web.Pages.Ordering.IndexModel).IsDefined(typeof(AllowAnonymousAttribute), inherit: true),
            "Public menu: staff ordering page /Ordering still requires login");

        var tho = new[]
        {
            new OnSaleDishRow(2, 1, "B", null, 10000, "Ly", null, 2, true),
            new OnSaleDishRow(1, 1, "A", "x", 20000, "Ly", "/a.png", 2, false),
            new OnSaleDishRow(3, 1, "C", "y", 30000, "Ly", null, 1, false),
            new OnSaleDishRow(4, 9, "Mồ côi", "z", 30000, "Ly", null, 1, false)
        };
        var built = PublicMenuBuilder.Build(new[] { (1, "Nhóm 1"), (5, "Nhóm trống") }, tho);
        check(built.Select(n => n.Id).SequenceEqual(new[] { 1, 5 }) && built[0].Dishes.Select(m => m.Id).SequenceEqual(new[] { 3, 1, 2 }) && built[1].Dishes.Count == 0,
            "Public menu: builder orders by sort order then Id and drops dishes of hidden groups");
        check(built[0].Dishes.Single(m => m.Id == 2) is { SoldOutToday: true, ShortDescription: "" }, "Public menu: sold-out-today flag and empty description carried");
    }
}
