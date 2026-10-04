using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Data.Models;
using RestaurantManagement.Web.Controllers;
using RestaurantManagement.Web.Models;
using RestaurantManagement.Web.Services;

/// <summary>S2-01 Task 4: thực đơn 200 món (không cần database) — rút gọn mô tả, hiển thị và tìm kiếm trong 200 món.</summary>
internal static class MenuPerformanceTests
{
    internal static void Run(Action<bool, string> check)
    {
        // Rút gọn mô tả: giữ nguyên mô tả ngắn, cắt mô tả dài ở ranh giới từ và thêm "…".
        check(MenuText.Preview("Gỏi cuốn tôm thịt.", 140) == "Gỏi cuốn tôm thịt.", "Menu performance: short description unchanged");
        var longText = string.Join(' ', Enumerable.Repeat("nguyên liệu tươi mỗi ngày,", 20));
        var preview = MenuText.Preview(longText, MenuText.DescriptionPreviewLength);
        check(preview.Length <= MenuText.DescriptionPreviewLength + 1 && preview.EndsWith('…') && !preview.EndsWith(",…") && longText.StartsWith(preview.TrimEnd('…')),
            "Menu performance: long description cut at a word boundary with an ellipsis");
        check(MenuText.Preview(new string('x', 300), 140) == new string('x', 140) + "…", "Menu performance: text without spaces is still cut");
        check(MenuText.Preview(null, 140) == "" && MenuText.Preview("  a  ", 140) == "a", "Menu performance: empty and padded text");

        // 200 món thuộc 8 nhóm, mỗi 10 món có 1 món hết trong ngày.
        var store = new InMemoryMenuStore();
        var sortOrder = 10;
        foreach (var name in new[] { "Món nướng", "Hải sản", "Món chay" })
            store.AddCategory(new DishCategory { Name = name, SortOrder = sortOrder++ });
        var groups = store.GetAllCategories().ToArray();
        for (var n = 1; n <= 200; n++)
        {
            var dish = store.AddDish(new Dish
            {
                Name = (n % 25 == 2 ? "Cơm rang" : "Món thử") + $" (mẫu {n:000})", CategoryId = groups[(n - 1) % groups.Length].Id,
                PriceVnd = 20000 + n * 1000, Unit = "Phần", PrepMinutes = 10,
                ShortDescription = n % 50 == 1 ? longText : "Mô tả ngắn."
            });
            if (n % 10 == 0) store.SetSoldOutToday(dish.Id, true);
        }

        var watch = Stopwatch.StartNew();
        var page = (new MenuController(store).Index() as ViewResult)?.Model as PublicMenuViewModel;
        watch.Stop();
        check(page is not null && page.DishCount >= 200 && page.Categories.Count == 8, $"Menu performance: 200 dishes in 8 groups ready for the page ({page?.DishCount} dishes)");
        check(page!.Categories.SelectMany(c => c.Dishes).Count(d => d.SoldOutToday) == 20, "Menu performance: 20 sold-out dishes keep their status");
        check(watch.ElapsedMilliseconds < 500, $"Menu performance: building the 200-dish menu takes {watch.ElapsedMilliseconds} ms");
        var longDish = page.Categories.SelectMany(c => c.Dishes).First(d => d.ShortDescription.Length > MenuText.DescriptionPreviewLength);
        check(longDish.DescriptionPreview.Length < longDish.ShortDescription.Length, "Menu performance: page receives shortened description, API keeps full text");

        watch.Restart();
        var results = (new MenuController(store).Index("com rang") as ViewResult)?.Model as PublicMenuViewModel;
        watch.Stop();
        check(results is { DishCount: 8, NoResults: false } && results.Categories.SelectMany(c => c.Dishes).All(d => d.Name.StartsWith("Cơm rang")),
            $"Menu performance: search \"com rang\" among 200 dishes finds exactly the 8 matching dishes");
        check(watch.ElapsedMilliseconds < 200, $"Menu performance: search among 200 dishes takes {watch.ElapsedMilliseconds} ms");
        var one = MenuSearch.Filter(store.GetPublicMenu(), "MAU 150").SelectMany(c => c.Dishes).ToArray();
        check(one.Length == 1 && one[0].Name.EndsWith("(mẫu 150)") && one[0].SoldOutToday, "Menu performance: search one sold-out dish among 200");
    }
}
