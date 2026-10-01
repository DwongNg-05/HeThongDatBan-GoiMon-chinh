using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using RestaurantManagement.DbTool;
using RestaurantManagement.Web.Services;

internal static class MenuHttpTests
{
    internal static async Task Run()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.Combine(DatabaseTool.Root, "src", "RestaurantManagement.Web"),
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(typeof(InMemoryMenuStore).Assembly.Location);
        start.ArgumentList.Add("--urls");
        start.ArgumentList.Add($"http://127.0.0.1:{port}");
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        // Isolate from the developer database; menu pages currently use an in-memory store.
        start.Environment["RM_CONNECTION_STRING"] = "Server=127.0.0.1,1;Database=MenuHttpTest;Integrated Security=True;Connect Timeout=1;Encrypt=False";
        using var web = new Process { StartInfo = start };
        var log = new System.Collections.Concurrent.ConcurrentQueue<string>();
        web.OutputDataReceived += (_, e) => { if (e.Data is not null) log.Enqueue(e.Data); };
        web.ErrorDataReceived += (_, e) => { if (e.Data is not null) log.Enqueue(e.Data); };
        web.Start(); web.BeginOutputReadLine(); web.BeginErrorReadLine();
        try
        {
            using var client = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false })
                { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(10) };
            var ready = false;
            for (var i = 0; i < 60 && !web.HasExited; i++)
            {
                try { if ((await client.GetAsync("/Menu")).IsSuccessStatusCode) { ready = true; break; } }
                catch (HttpRequestException) { }
                await Task.Delay(250);
            }
            if (!ready) throw new Exception("Menu web did not start: " + string.Join(Environment.NewLine, log.TakeLast(20)));

            async Task<string> Get(string path)
            {
                using var response = await client.GetAsync(path);
                response.EnsureSuccessStatusCode();
                return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
            }
            static void Check(bool condition, string label)
            {
                if (!condition) throw new Exception("FAIL: " + label);
                Console.WriteLine("PASS: " + label);
            }
            static string[] Headings(string html) => Regex.Matches(html, "<h2 id=\"tieu-de-nhom-[0-9]+\">(.*?)</h2>")
                .Select(m => m.Groups[1].Value).ToArray();
            string[] expected = ["Khai vị", "Món chính", "Lẩu", "Tráng miệng", "Đồ uống"];
            var publicHtml = await Get("/Menu");
            var orderHtml = await Get("/Ordering");
            Check(Headings(publicHtml).SequenceEqual(expected), "HTTP: public menu has five headings in required order");
            Check(Headings(orderHtml).SequenceEqual(expected), "HTTP: ordering menu has the same five headings in required order");
            foreach (var html in new[] { publicHtml, orderHtml })
            {
                var sections = Regex.Matches(html, "<section .*?</section>", RegexOptions.Singleline);
                string[] dishes = ["Gỏi cuốn", "Cơm chiên hải sản", "Lẩu Thái", "Chè hạt sen", "Trà đào"];
                Check(sections.Count == 5 && sections.Select((s, i) => s.Value.Contains(dishes[i])).All(v => v), "HTTP: each dish renders in its own group");
                Check(html.Contains("action=\"/Ordering/Checkout\"") && html.Contains("name=\"__RequestVerificationToken\""), "HTTP: shared cart has working checkout form and anti-forgery token");
                Check(html.Contains("<html lang=\"vi\">") && !html.Contains("<<<<<<<"), "HTTP: menu uses application layout without conflict markers");
            }

            // Stop the only appetizer through the existing edit form; both pages must retain its empty group.
            var edit = await Get("/Dishes/Edit/1");
            var token = Regex.Match(edit, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
            Check(token.Length > 0, "HTTP: dish edit form has an anti-forgery token");
            using var saved = await client.PostAsync("/Dishes/Edit/1", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token, ["Dish.Id"] = "1", ["Dish.Name"] = "Gỏi cuốn",
                ["Dish.CategoryId"] = "1", ["Dish.PriceVnd"] = "45000", ["Dish.Unit"] = "Đĩa",
                ["Dish.ShortDescription"] = "Món kiểm thử", ["Dish.PrepMinutes"] = "15", ["Dish.Status"] = "Discontinued"
            }));
            Check(saved.StatusCode == HttpStatusCode.Redirect, "HTTP: existing dish editor can stop a dish");
            foreach (var path in new[] { "/Menu", "/Ordering" })
            {
                var html = await Get(path);
                var firstSection = Regex.Match(html, "<section .*?</section>", RegexOptions.Singleline).Value;
                Check(Headings(html).SequenceEqual(expected) && firstSection.Contains("Chưa có món ăn") && !firstSection.Contains("Gỏi cuốn"), "HTTP: empty group remains visible in correct order on " + path);
            }
            await CategoryHttpTests.Run(client);
        }
        finally
        {
            if (!web.HasExited) web.Kill(entireProcessTree: true);
            await web.WaitForExitAsync();
        }
    }
}
