using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace RestaurantManagement.DbTool;

/// <summary>
/// S2-01 Task 4 (AC4): với 200 món kiểm thử, trang /Menu (HTML + CSS/JS/ảnh mà trang dùng) tải dưới 2 giây,
/// tìm kiếm trong 200 món vẫn đúng và nhanh, dữ liệu gửi xuống được giảm (nén, bỏ JS không cần, rút gọn mô tả).
/// Chạy với client chưa đăng nhập; sau khi đo, 200 món kiểm thử được ẩn lại.
/// </summary>
internal static class MenuPerformanceVerification
{
    private const int BudgetMs = 2000;
    private const int Runs = 5;

    internal static async Task Run(string connection, HttpClient anonymous)
    {
        await DatabaseTool.SeedMenu200(connection);
        await DatabaseTool.SeedMenu200(connection);
        await Check(connection, $"""
            SELECT CASE WHEN (SELECT COUNT(*) FROM dbo.vw_PublicMenu WHERE Name LIKE {DatabaseTool.LoadTestNamePattern})=200
             AND (SELECT COUNT(DISTINCT CategoryId) FROM dbo.vw_PublicMenu WHERE Name LIKE {DatabaseTool.LoadTestNamePattern})=8
             AND (SELECT COUNT(*) FROM dbo.vw_PublicMenu WHERE Name LIKE {DatabaseTool.LoadTestNamePattern} AND IsSoldOut=1)=20
             THEN 1 ELSE 0 END
            """, "Load test data: 200 dishes in 8 groups (20 sold out today), seeded twice without duplicates");

        var baseAddress = anonymous.BaseAddress!;
        using var browserLike = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All })
            { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(10) };
        using var raw = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None })
            { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(10) };
        try
        {
            // Lần đầu: biên dịch view/JIT — không tính, giống trang đã được mở trước khi khách vào.
            await PageLoad(browserLike, "/Menu");

            var timings = new List<long>();
            string html = string.Empty;
            for (var i = 0; i < Runs; i++)
            {
                var (ms, page) = await PageLoad(browserLike, "/Menu");
                timings.Add(ms);
                html = page;
            }
            timings.Sort();
            Console.WriteLine($"INFO: /Menu with 200+ dishes, full page load (HTML + assets) in ms: {string.Join(", ", timings)} (median {timings[Runs / 2]})");
            Assert(timings[^1] < BudgetMs, $"Menu with 200 dishes loads in under {BudgetMs} ms (slowest of {Runs}: {timings[^1]} ms)");

            var dishes = Regex.Matches(html, "<li class=\"public-dish\"").Count;
            Assert(dishes >= 200 && html.Contains("(mẫu 001)") && html.Contains("(mẫu 200)"), $"All 200 sample dishes are rendered ({dishes} dishes on the page)");
            Assert(Regex.Matches(html, "data-sold-out=\"true\"").Count >= 20, "Sold-out sample dishes keep their label");
            Assert(Regex.Matches(html, "loading=\"eager\"").Count <= 4 && Regex.Matches(html, "loading=\"lazy\"").Count >= dishes - 4,
                "Only the first images load immediately; the rest are lazy-loaded");

            // Giảm dữ liệu không cần thiết.
            Assert(!html.Contains("jquery.min.js") && !html.Contains("bootstrap.bundle.min.js"), "Guest menu page does not download jQuery/Bootstrap JS");
            var longDish = Regex.Matches(html, "<li class=\"public-dish\".*?</li>", RegexOptions.Singleline).Select(m => m.Value)
                .First(li => li.Contains("(mẫu 097)"));
            Assert(longDish.Contains("…") && !longDish.Contains("có thể yêu cầu ít cay hoặc không hành."), "Long descriptions are shortened in the page HTML");

            using (var request = new HttpRequestMessage(HttpMethod.Get, "/Menu"))
            {
                request.Headers.TryAddWithoutValidation("Accept-Encoding", "br, gzip");
                using var response = await raw.SendAsync(request);
                var compressed = (await response.Content.ReadAsByteArrayAsync()).Length;
                var plain = System.Text.Encoding.UTF8.GetByteCount(html);
                Console.WriteLine($"INFO: /Menu HTML {plain / 1024} KB uncompressed, {compressed / 1024} KB sent ({string.Join(",", response.Content.Headers.ContentEncoding)})");
                Assert(response.Content.Headers.ContentEncoding.Count > 0 && compressed * 3 < plain, "Menu HTML is compressed for guests (at least 3x smaller)");
            }

            // Tìm kiếm trong 200 món: kết quả đúng và trang kết quả tải lại dưới 2 giây.
            foreach (var (keyword, expected) in new[] { ("com rang", "Cơm rang (mẫu 002)"), ("BO LUC LAC", "Bò lúc lắc (mẫu 010)"), ("mau 150", "(mẫu 150)") })
            {
                var path = "/Menu?q=" + Uri.EscapeDataString(keyword);
                await PageLoad(browserLike, path);
                var (ms, page) = await PageLoad(browserLike, path);
                var names = Regex.Matches(page, "<h3 class=\"public-dish-name\">(.*?)</h3>").Select(m => m.Groups[1].Value).ToArray();
                Assert(ms < BudgetMs && names.Length > 0 && names.Length < dishes && names.Any(n => n.Contains(expected)),
                    $"Search \"{keyword}\" in 200 dishes: {names.Length} results in {ms} ms");
            }
            var soldOutSearch = (await PageLoad(browserLike, "/Menu?q=" + Uri.EscapeDataString("bo luc lac mau 010"))).Html;
            Assert(soldOutSearch.Contains("data-sold-out=\"true\"") && soldOutSearch.Contains("Tạm hết"), "Searching a sold-out dish among 200 still shows its label");
            Console.WriteLine("PASS: S2-01 Task 4 performance checks.");
        }
        finally
        {
            await DatabaseTool.HideMenu200(connection);
        }
        await Check(connection, $"SELECT CASE WHEN NOT EXISTS(SELECT 1 FROM dbo.vw_PublicMenu WHERE Name LIKE {DatabaseTool.LoadTestNamePattern}) THEN 1 ELSE 0 END",
            "hide-menu-200 removes the sample dishes from the public menu");
    }

    /// <summary>
    /// Tải trang như trình duyệt: HTML, rồi song song mọi CSS/JS/ảnh khác nhau mà trang tham chiếu.
    /// Trả về tổng thời gian (ms) tới khi tải xong tất cả.
    /// </summary>
    private static async Task<(long Ms, string Html)> PageLoad(HttpClient client, string path)
    {
        var watch = Stopwatch.StartNew();
        using var response = await client.GetAsync(path);
        Assert(response.StatusCode == HttpStatusCode.OK, $"GET {path} returns 200");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        var assets = Regex.Matches(html, "(?:href|src)=\"(/(?:css|js|lib|images|uploads|RestaurantManagement)[^\"]*)\"")
            .Select(m => m.Groups[1].Value).Distinct().ToArray();
        var results = await Task.WhenAll(assets.Select(async asset =>
        {
            using var r = await client.GetAsync(asset);
            await r.Content.ReadAsByteArrayAsync();
            return (asset, r.StatusCode);
        }));
        watch.Stop();
        var failed = results.Where(r => r.StatusCode != HttpStatusCode.OK).Select(r => r.asset).ToArray();
        Assert(failed.Length == 0, $"All assets of {path} load ({assets.Length} files)" + (failed.Length > 0 ? ": " + string.Join(", ", failed) : ""));
        return (watch.ElapsedMilliseconds, html);
    }

    private static async Task Check(string connection, string sql, string name)
    {
        await using var cn = new SqlConnection(connection);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        Assert(Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1, name);
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
    }
}
