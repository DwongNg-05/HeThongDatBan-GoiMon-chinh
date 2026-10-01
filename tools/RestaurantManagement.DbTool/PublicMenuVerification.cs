using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace RestaurantManagement.DbTool;

/// <summary>
/// S2-01 Task 1 (AC1): khách mở /ThucDon khi chưa đăng nhập và thấy món theo nhóm với ảnh, tên, mô tả ngắn, giá VND.
/// Chạy trên database kiểm thử riêng của lệnh verify, với client chưa có cookie đăng nhập.
/// </summary>
internal static class PublicMenuVerification
{
    internal static async Task Run(string connection, HttpClient anonymous)
    {
        // Dữ liệu mẫu chạy lại không tạo trùng.
        await DatabaseTool.SeedMenuDemo(connection);
        await DatabaseTool.SeedMenuDemo(connection);
        await Check(connection, """
            SELECT CASE WHEN (SELECT COUNT(*) FROM dbo.MenuItems WHERE ImagePath LIKE N'/images/thuc-don/%')=16
             AND (SELECT COUNT(*) FROM dbo.MenuCategories WHERE Name IN (N'Khai vị',N'Món chính',N'Lẩu',N'Tráng miệng',N'Đồ uống'))=5
             AND NOT EXISTS(SELECT 1 FROM dbo.MenuCategories WHERE Name IN (N'Khai vị',N'Món chính',N'Lẩu',N'Tráng miệng',N'Đồ uống') AND DefaultImagePath IS NULL)
             AND (SELECT COUNT(*) FROM dbo.MenuItems WHERE Name=N'Cua rang me' AND IsActive=0)=1
             THEN 1 ELSE 0 END
            """, "Menu demo: groups, images, descriptions, VND prices and sale status seeded once");

        // Nhóm ngừng sử dụng phải ẩn cả nhóm lẫn món bên trong.
        await DatabaseTool.Execute(connection, """
            INSERT dbo.MenuCategories(Name,SortOrder,IsActive) VALUES(N'S2 nhóm ẩn',99,0);
            INSERT dbo.MenuItems(CategoryId,Name,Price,Unit,Description,EstimatedPrepMinutes)
            VALUES(SCOPE_IDENTITY(),N'Món thuộc nhóm ẩn',10000,N'Phần',N'Không được hiển thị',5);
            """);
        try
        {
            using var page = await anonymous.GetAsync("/ThucDon");
            Assert(page.StatusCode == HttpStatusCode.OK, "Public menu opens without login (200, no redirect)");
            var html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
            Assert(html.Contains("name=\"viewport\"") && html.Contains("public-menu.css"), "Public menu has mobile viewport and menu styles");
            Assert(!html.Contains("href=\"/Areas\"") && !html.Contains("Đăng xuất"), "Public menu hides staff navigation");

            string[] groups = ["Khai vị", "Món chính", "Lẩu", "Tráng miệng", "Đồ uống"];
            var headings = Regex.Matches(html, "<h2 id=\"tieu-de-nhom-[0-9]+\">(.*?)</h2>").Select(m => m.Groups[1].Value).ToArray();
            Assert(headings.SequenceEqual(groups), "Public menu lists active groups in display order");
            Assert(groups.All(g => Regex.IsMatch(html, $"<a href=\"#nhom-[0-9]+\">{Regex.Escape(g)}</a>")), "Group navigation links to every group");

            var sections = Regex.Matches(html, "<section .*?</section>", RegexOptions.Singleline).Select(m => m.Value).ToArray();
            string Section(string group) => sections.Single(s => s.Contains($">{group}</h2>"));
            string Dish(string group, string name) =>
                Regex.Matches(Section(group), "<li class=\"public-dish\".*?</li>", RegexOptions.Singleline)
                    .Select(m => m.Value).Single(li => li.Contains($">{name}</h3>"));

            var goiCuon = Dish("Khai vị", "Gỏi cuốn tôm thịt");
            Assert(goiCuon.Contains("src=\"/images/thuc-don/khai-vi.svg\"") && goiCuon.Contains("alt=\"Ảnh món Gỏi cuốn tôm thịt\""), "Dish shows its image with alt text");
            Assert(goiCuon.Contains("2 cuốn tôm, thịt, bún và rau sống, chấm tương đậu phộng."), "Dish shows short description");
            Assert(goiCuon.Contains("<data value=\"45000\">45.000 ₫</data>") && goiCuon.Contains("/ Phần"), "Price shows VND grouping and ₫ unit");
            Assert(Dish("Lẩu", "Lẩu Thái hải sản").Contains("280.000 ₫") && Dish("Món chính", "Bò lúc lắc").Contains("145.000 ₫"), "Six-digit VND prices formatted correctly");
            Assert(Dish("Đồ uống", "Cà phê sữa đá").Contains("29.000 ₫"), "Drinks are listed in their own group");

            // Món mẫu cũ (seed-demo) không có ảnh riêng: dùng ảnh mặc định của nhóm.
            var demoDish = Dish("Khai vị", "Gỏi khai vị 1");
            Assert(demoDish.Contains("src=\"/images/thuc-don/khai-vi.svg\"") && demoDish.Contains("25.000 ₫"), "Dish without image falls back to group image");

            Assert(!html.Contains("Cua rang me"), "Stopped dish is hidden");
            Assert(!html.Contains("S2 nhóm ẩn") && !html.Contains("Món thuộc nhóm ẩn"), "Inactive group and its dishes are hidden");
            Assert(groups.All(g => !Section(g).Contains("Gỏi cuốn tôm thịt") || g == "Khai vị"), "Each dish appears only in its own group");

            using var image = await anonymous.GetAsync("/images/thuc-don/khai-vi.svg");
            Assert(image.StatusCode == HttpStatusCode.OK, "Menu images load without login");

            using var api = await anonymous.GetAsync("/api/thuc-don");
            Assert(api.StatusCode == HttpStatusCode.OK && api.Content.Headers.ContentType?.MediaType == "application/json", "Public menu API works without login");
            using var json = JsonDocument.Parse(await api.Content.ReadAsStringAsync());
            var apiGroups = json.RootElement.EnumerateArray().ToArray();
            Assert(apiGroups.Select(g => g.GetProperty("ten").GetString()).SequenceEqual(groups), "API returns groups in display order");
            var apiDish = apiGroups[0].GetProperty("monAn").EnumerateArray().Single(m => m.GetProperty("ten").GetString() == "Gỏi cuốn tôm thịt");
            Assert(apiDish.GetProperty("giaVnd").GetInt32() == 45000 && apiDish.GetProperty("giaHienThi").GetString() == "45.000 ₫"
                && apiDish.GetProperty("anhUrl").GetString() == "/images/thuc-don/khai-vi.svg"
                && apiDish.GetProperty("moTaNgan").GetString()!.Length > 0, "API dish has image, description and VND price");
            Assert(apiGroups.All(g => g.GetProperty("monAn").EnumerateArray().All(m => m.GetProperty("ten").GetString() != "Cua rang me")), "API hides stopped dish");

            // Không gia hạn hay tạo phiên đăng nhập cho khách.
            Assert(!page.Headers.TryGetValues("Set-Cookie", out var cookies) || cookies.All(c => !c.StartsWith("RestaurantManagement.Auth=")), "Public menu does not create a login cookie");
            Console.WriteLine("PASS: S2-01 Task 1 public menu checks.");
        }
        finally
        {
            await DatabaseTool.Execute(connection, "DELETE dbo.MenuItems WHERE Name=N'Món thuộc nhóm ẩn'; DELETE dbo.MenuCategories WHERE Name=N'S2 nhóm ẩn';");
        }
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
    }

    private static async Task Check(string connection, string sql, string name)
    {
        await using var cn = new SqlConnection(connection);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        Assert(Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1, name);
    }
}
