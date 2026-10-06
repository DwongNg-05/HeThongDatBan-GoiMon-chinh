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
        check(DishImage.Resolve(null, "/images/thuc-don/lau.svg") == "/images/thuc-don/lau.svg",
            "Dish image: missing dish image uses its category default");
        check(DishImage.Resolve("/uploads/mon-an/own.jpg", "/images/thuc-don/lau.svg") == "/uploads/mon-an/own.jpg",
            "Dish image: own image takes precedence over category default");
        check(DishImage.Resolve(null, "javascript:alert(1)") == DishImage.DefaultImage,
            "Dish image: missing or unsafe category default falls back to generic image");

        var store = new InMemoryMenuStore();
        string[] expected = ["Khai vị", "Món chính", "Lẩu", "Tráng miệng", "Đồ uống"];
        var menu = store.GetPublicMenu();
        var defaultImages = new[]
        {
            "/images/thuc-don/khai-vi.svg", "/images/thuc-don/mon-chinh.svg", "/images/thuc-don/lau.svg",
            "/images/thuc-don/trang-mieng.svg", "/images/thuc-don/do-uong.svg"
        };
        check(defaultImages.Append(DishImage.DefaultImage).All(path => File.Exists(Path.Combine(
                Directory.GetCurrentDirectory(), "src", "RestaurantManagement.Web", "wwwroot",
                path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)))),
            "Public menu: category and generic fallback image assets are present");
        check(menu.SelectMany(category => category.Dishes).All(dish => !string.IsNullOrWhiteSpace(dish.ImageUrl))
            && menu.Select((category, index) => category.Dishes.All(dish => dish.ImageUrl == defaultImages[index])).All(result => result),
            "Public menu: dishes without own photos use the matching default for all five groups");
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
        check(newDishShown.ImageUrl == "/images/thuc-don/khai-vi.svg" && newDishShown.ShortDescription == "Giòn rụm", "Public menu: new dish without image uses its category default; description trimmed");
        check(store.GetPublicMenu()[0].Dishes.Select(m => m.Id).SequenceEqual(new[] { springRoll.Id, newDish.Id }), "Public menu: dishes in stable order within a group");

        newDish.Status = DishStatus.Discontinued;
        check(store.GetPublicMenu().SelectMany(n => n.Dishes).All(m => m.Id != newDish.Id), "Public menu: stopped dish hidden");

        var hiddenCategory = store.GetAllCategories().Last();
        store.SetCategoryActive(hiddenCategory.Id, false);
        var afterHiding = store.GetPublicMenu();
        check(afterHiding.All(n => n.Id != hiddenCategory.Id) && afterHiding.Count == 4, "Public menu: inactive group hidden with its dishes");

        var emptyCategory = store.AddCategory(new DishCategory { Name = "Món theo mùa", SortOrder = 10 });
        check(store.GetPublicMenu().Any(n => n.Id == emptyCategory.Id && n.Dishes.Count == 0), "Public menu: active empty group still listed");

        var categoryWithoutDefault = store.AddCategory(new DishCategory { Name = "Nhóm chưa cấu hình ảnh", SortOrder = 11 });
        var dishWithoutDefault = store.AddDish(new Dish
        {
            Name = "Món chưa cấu hình ảnh", CategoryId = categoryWithoutDefault.Id, PriceVnd = 25000,
            Unit = "Phần", PrepMinutes = 10
        });
        var unknownGroupDish = store.GetPublicMenu().Single(n => n.Id == categoryWithoutDefault.Id).Dishes.Single();
        check(unknownGroupDish.ImageUrl == DishImage.DefaultImage && !string.IsNullOrWhiteSpace(unknownGroupDish.ImageUrl),
            "Public menu: group without a configured default falls back to the generic image");
        dishWithoutDefault.ImagePath = "/uploads/mon-an/rieng.jpg";
        check(store.GetPublicMenu().Single(n => n.Id == categoryWithoutDefault.Id).Dishes.Single().ImageUrl == dishWithoutDefault.ImagePath,
            "Public menu: a dish-specific image overrides its category default");
        check(store.GetMenuByCategory().Single(n => n.Id == appetizerCategory.Id).DefaultImagePath == defaultImages[0],
            "Ordering menu: category carries its default image to dish cards");

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
        var built = PublicMenuBuilder.Build(new[] { (1, "Nhóm 1", "/images/thuc-don/khai-vi.svg"), (5, "Nhóm trống", (string?)null) }, tho);
        check(built.Select(n => n.Id).SequenceEqual(new[] { 1, 5 }) && built[0].Dishes.Select(m => m.Id).SequenceEqual(new[] { 3, 1, 2 }) && built[1].Dishes.Count == 0,
            "Public menu: builder orders by sort order then Id and drops dishes of hidden groups");
        check(built[0].Dishes.Single(m => m.Id == 2) is { SoldOutToday: true, ShortDescription: "" }, "Public menu: sold-out-today flag and empty description carried");
        check(built[0].Dishes.Single(m => m.Id == 2).ImageUrl == "/images/thuc-don/khai-vi.svg"
            && built[0].Dishes.Single(m => m.Id == 1).ImageUrl == "/a.png"
            && !built.SelectMany(n => n.Dishes).Any(m => m.Id == 4),
            "Public menu: category default fills missing photos, own image wins, and orphaned dishes are omitted");
    }
}
