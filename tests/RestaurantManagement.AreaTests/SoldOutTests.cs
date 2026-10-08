using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Data.Models;
using RestaurantManagement.Web.Controllers;
using RestaurantManagement.Web.Models;
using RestaurantManagement.Web.Services;

/// <summary>
/// S2-01 Task 3: món hết trong ngày vẫn hiển thị trên thực đơn và trong kết quả tìm kiếm,
/// có nhãn "Tạm hết" và được đánh dấu để làm mờ; mở bán lại thì hiển thị bình thường.
/// </summary>
internal static class SoldOutTests
{
    internal static void Run(Action<bool, string> check)
    {
        var store = new InMemoryMenuStore();
        var mainCourse = store.GetAllCategories().Single(c => c.Name == "Món chính");
        var friedRice = store.AddDish(new Dish
        {
            Name = "Cơm rang dưa bò", CategoryId = mainCourse.Id, PriceVnd = 75000, Unit = "Đĩa", PrepMinutes = 15,
            ShortDescription = "Cơm rang giòn hạt với dưa cải chua và thịt bò xào tỏi.", ImagePath = "/images/thuc-don/mon-chinh.svg"
        });
        PublicMenuDish Find(IReadOnlyList<PublicMenuCategory> menu, int id) => menu.SelectMany(c => c.Dishes).Single(d => d.Id == id);

        // Món còn bán.
        var available = Find(store.GetPublicMenu(), friedRice.Id);
        check(available is { SoldOutToday: false, IsTemporarilyUnavailable: false, AvailabilityLabel: "" },
            "Sold out: dish on sale has no label");

        // Món tạm hết vẫn xuất hiện, giữ nguyên nhóm, vị trí, ảnh, mô tả và giá.
        var orderBefore = store.GetPublicMenu().SelectMany(c => c.Dishes).Select(d => d.Id).ToArray();
        check(store.SetSoldOutToday(friedRice.Id, true) && store.IsSoldOutToday(friedRice.Id), "Sold out: status saved");
        var menu = store.GetPublicMenu();
        var soldOut = Find(menu, friedRice.Id);
        check(soldOut is { SoldOutToday: true, IsTemporarilyUnavailable: true, AvailabilityLabel: "Tạm hết" }, "Sold out: dish shows \"Tạm hết\"");
        check(menu.Single(c => c.Id == mainCourse.Id).Dishes.Any(d => d.Id == friedRice.Id)
            && menu.SelectMany(c => c.Dishes).Select(d => d.Id).SequenceEqual(orderBefore),
            "Sold out: dish stays in its group at the same position");
        check(soldOut with { SoldOutToday = false } == available, "Sold out: image, name, description, price and unit unchanged");
        check(menu.SelectMany(c => c.Dishes).Count(d => d.SoldOutToday) == 1, "Sold out: only the marked dish is sold out");

        // Tìm kiếm theo tên vẫn trả về món tạm hết kèm trạng thái.
        foreach (var keyword in new[] { "com rang", "Cơm rang dưa bò", "COM RANG DUA BO" })
        {
            var found = MenuSearch.Filter(menu, keyword).SelectMany(c => c.Dishes).ToArray();
            check(found.Length == 1 && found[0] is { SoldOutToday: true, AvailabilityLabel: "Tạm hết" } && found[0].Id == friedRice.Id,
                $"Sold out: search \"{keyword}\" still finds the sold-out dish with its label");
        }
        var page = (new MenuController(store).Index("com rang") as ViewResult)?.Model as PublicMenuViewModel;
        check(page is { DishCount: 1, NoResults: false } && page.Categories.Single().Dishes.Single().SoldOutToday,
            "Sold out: /Menu?q= shows the sold-out dish, not a not-found message");
        var api = (new MenuApiController(store).List("com rang").Result as OkObjectResult)?.Value as IReadOnlyList<PublicMenuCategory>;
        check(api is not null && api.Single().Dishes.Single().SoldOutToday, "Sold out: API returns soldOutToday = true");

        // Mở bán lại: hiển thị bình thường.
        check(store.SetSoldOutToday(friedRice.Id, false) && !store.IsSoldOutToday(friedRice.Id), "Sold out: status cleared");
        check(Find(store.GetPublicMenu(), friedRice.Id) == available, "Sold out: dish back on sale looks normal again");

        // Món ngừng bán khác món tạm hết: ngừng bán thì ẩn hẳn.
        store.SetSoldOutToday(friedRice.Id, true);
        friedRice.Status = DishStatus.Discontinued;
        check(store.GetPublicMenu().SelectMany(c => c.Dishes).All(d => d.Id != friedRice.Id), "Sold out: discontinued dish is hidden, unlike sold-out");
        check(!store.SetSoldOutToday(99999, true), "Sold out: unknown dish rejected");

        // Dữ liệu SQL: cờ hết trong ngày đi qua builder.
        var built = PublicMenuBuilder.Build(new[] { (1, "Nhóm", (string?)null) }, new[]
        {
            new OnSaleDishRow(1, 1, "A", "a", 10000, "Ly", null, 1, true),
            new OnSaleDishRow(2, 1, "B", "b", 20000, "Ly", null, 2, false)
        });
        check(built[0].Dishes.Select(d => d.AvailabilityLabel).SequenceEqual(new[] { "Tạm hết", "" }), "Sold out: builder keeps per-dish status");
    }
}
