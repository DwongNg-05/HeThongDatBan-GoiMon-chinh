using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Data.Models;
using RestaurantManagement.Web.Controllers;
using RestaurantManagement.Web.Models;
using RestaurantManagement.Web.Services;

/// <summary>
/// S2-01 Task 2: tìm món theo tên trên thực đơn công khai,
/// không phân biệt chữ hoa/chữ thường và dấu tiếng Việt.
/// </summary>
internal static class MenuSearchTests
{
    internal static void Run(Action<bool, string> check)
    {
        // Chuẩn hoá từ khoá.
        check(MenuSearch.Normalize("Cơm Rang") == "com rang", "Menu search: accents and upper case removed");
        check(MenuSearch.Normalize("ĐẬU HŨ đậu hũ") == "dau hu dau hu", "Menu search: đ/Đ become d");
        check(MenuSearch.Normalize("  Lẩu \t gà   lá é ") == "lau ga la e", "Menu search: spaces trimmed and collapsed");
        check(MenuSearch.Normalize("Cơm".Normalize(System.Text.NormalizationForm.FormD)) == "com", "Menu search: decomposed (NFD) input handled");
        check(MenuSearch.Normalize(null) == "" && MenuSearch.Normalize("   ") == "", "Menu search: empty keyword normalizes to empty");
        check(MenuSearch.CleanKeyword("  com   rang ") == "com rang", "Menu search: keyword shown without extra spaces");
        check(MenuSearch.CleanKeyword(new string('a', 300)).Length == MenuSearch.MaxKeywordLength, "Menu search: keyword length limited");

        var store = new InMemoryMenuStore();
        var mainCourse = store.GetAllCategories().Single(c => c.Name == "Món chính");
        var friedRice = store.AddDish(new Dish
        {
            Name = "Cơm rang dưa bò", CategoryId = mainCourse.Id, PriceVnd = 75000, Unit = "Đĩa", PrepMinutes = 15,
            ShortDescription = "Cơm rang giòn hạt với dưa cải chua và thịt bò xào tỏi.", ImagePath = "/images/thuc-don/mon-chinh.svg"
        });
        var stopped = store.AddDish(new Dish
        {
            Name = "Cơm rang thập cẩm", CategoryId = mainCourse.Id, PriceVnd = 70000, Unit = "Đĩa", PrepMinutes = 15
        });
        stopped.Status = DishStatus.Discontinued;
        var menu = store.GetPublicMenu();

        static string[] Names(IReadOnlyList<PublicMenuCategory> categories) => categories.SelectMany(c => c.Dishes).Select(d => d.Name).ToArray();

        // Test chính của task: "com rang" phải trả về "cơm rang", giữ nguyên nhóm, ảnh, tên, mô tả, giá.
        var result = MenuSearch.Filter(menu, "com rang");
        check(Names(result).SequenceEqual(new[] { "Cơm rang dưa bò" }), "Menu search: \"com rang\" returns \"Cơm rang dưa bò\"");
        var shown = result.Single().Dishes.Single();
        var original = menu.Single(c => c.Id == mainCourse.Id).Dishes.Single(d => d.Id == friedRice.Id);
        check(result.Single() is { Name: "Món chính" } && result.Single().Id == mainCourse.Id && shown == original
                && shown is { PriceVnd: 75000, DisplayPrice: "75.000 ₫", Unit: "Đĩa", ImageUrl: "/images/thuc-don/mon-chinh.svg" }
                && shown.ShortDescription.StartsWith("Cơm rang giòn hạt"),
            "Menu search: result keeps group, image, name, description and price");

        // Có dấu, không dấu, chữ hoa/chữ thường.
        foreach (var keyword in new[] { "Cơm rang", "cơm rang", "CƠM RANG", "COM RANG", "cOm RaNg", "  com   rang  ", "rang dua bo" })
            check(Names(MenuSearch.Filter(menu, keyword)).SequenceEqual(new[] { "Cơm rang dưa bò" }), $"Menu search: \"{keyword}\" finds Cơm rang dưa bò");
        check(Names(MenuSearch.Filter(menu, "goi cuon")).SequenceEqual(new[] { "Gỏi cuốn" })
            && Names(MenuSearch.Filter(menu, "Gỏi Cuốn")).SequenceEqual(new[] { "Gỏi cuốn" }), "Menu search: accented and plain keywords match the same dish");
        check(Names(MenuSearch.Filter(menu, "com")).SequenceEqual(new[] { "Cơm chiên hải sản", "Cơm rang dưa bò" }),
            "Menu search: partial keyword returns every matching dish in menu order");
        check(MenuSearch.Matches("Đậu hũ chiên", "dau hu") && MenuSearch.Matches("Trà đào", "TRA DAO") && !MenuSearch.Matches("Trà đào", "tra sua"),
            "Menu search: match helper handles đ and case");

        // Chỉ tìm theo tên món, không tìm theo mô tả hay tên nhóm; món ngừng bán không bao giờ xuất hiện.
        check(MenuSearch.Filter(menu, "dua cai chua").Count == 0, "Menu search: description is not searched");
        check(MenuSearch.Filter(menu, "mon chinh").Count == 0, "Menu search: group name is not searched");
        check(!Names(MenuSearch.Filter(menu, "thap cam")).Any(), "Menu search: stopped dish never returned");

        // Không tìm thấy; xoá từ khoá thì hiện lại toàn bộ thực đơn.
        check(MenuSearch.Filter(menu, "pizza xyz").Count == 0, "Menu search: unknown keyword returns no groups");
        check(ReferenceEquals(MenuSearch.Filter(menu, ""), menu) && ReferenceEquals(MenuSearch.Filter(menu, null), menu)
            && ReferenceEquals(MenuSearch.Filter(menu, "   "), menu), "Menu search: empty keyword returns the full menu");

        // Controller: /Menu?q=...
        var controller = new MenuController(store);
        var found = (controller.Index("COM RANG") as ViewResult)?.Model as PublicMenuViewModel;
        check(found is { IsSearching: true, NoResults: false, DishCount: 1, Keyword: "COM RANG", MenuIsEmpty: false }
            && found.Categories.Single().Dishes.Single().Id == friedRice.Id, "Menu search: /Menu?q=COM RANG shows one result");
        var none = (controller.Index("  khong   co  ") as ViewResult)?.Model as PublicMenuViewModel;
        check(none is { IsSearching: true, NoResults: true, DishCount: 0, Keyword: "khong co" } && none.Categories.Count == 0,
            "Menu search: unknown keyword shows not-found state");
        var all = (controller.Index("") as ViewResult)?.Model as PublicMenuViewModel;
        check(all is { IsSearching: false, NoResults: false } && all.Categories.Count == menu.Count && all.DishCount == menu.Sum(c => c.Dishes.Count),
            "Menu search: cleared keyword shows every group and dish again");

        // API: /api/menu?q=...
        var api = (new MenuApiController(store).List("cơm RANG").Result as OkObjectResult)?.Value as IReadOnlyList<PublicMenuCategory>;
        check(api is not null && Names(api).SequenceEqual(new[] { "Cơm rang dưa bò" }), "Menu search: API ?q= filters the same way");
    }
}
