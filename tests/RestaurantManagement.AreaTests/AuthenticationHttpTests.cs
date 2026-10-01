using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using RestaurantManagement.DbTool;
using RestaurantManagement.Web.Services;

internal static class AuthenticationHttpTests
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
        start.ArgumentList.Add(typeof(InMemoryQuanLyMonStore).Assembly.Location);
        start.ArgumentList.Add("--urls");
        start.ArgumentList.Add($"http://127.0.0.1:{port}");
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["RM_CONNECTION_STRING"] = "Server=127.0.0.1,1;Database=AuthenticationTest;Integrated Security=True;Connect Timeout=1;Encrypt=False";
        start.Environment["DevelopmentUser__Enabled"] = "true";
        start.Environment["DevelopmentUser__UserName"] = "tester";
        using var web = new Process { StartInfo = start };
        var log = new System.Collections.Concurrent.ConcurrentQueue<string>();
        web.OutputDataReceived += (_, e) => { if (e.Data is not null) log.Enqueue(e.Data); };
        web.ErrorDataReceived += (_, e) => { if (e.Data is not null) log.Enqueue(e.Data); };
        web.Start(); web.BeginOutputReadLine(); web.BeginErrorReadLine();
        try
        {
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
                { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(10) };
            var ready = false;
            for (var i = 0; i < 60 && !web.HasExited; i++)
            {
                try
                {
                    using var response = await client.GetAsync("/Account/Login");
                    if (response.IsSuccessStatusCode) { ready = true; break; }
                }
                catch (HttpRequestException) { }
                await Task.Delay(250);
            }
            if (!ready) throw new Exception("Login page did not start: " + string.Join(Environment.NewLine, log.TakeLast(20)));
            foreach (var path in new[] { "/", "/Home/Index", "/Areas", "/Tables", "/Reservations", "/OpeningHours", "/SpecialHolidays", "/QuanLyMon", "/QuanLyNhomMon", "/GoiMon", "/admin/employee-accounts", "/AuditLogs", "/AuditLogs/Index", "/QuanLyMon/NhatKyGia/1", "/api/table-status", "/api/table-map/A01" })
            {
                using var response = await client.GetAsync(path);
                Check(response.StatusCode == HttpStatusCode.Unauthorized ||
                    (response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location?.OriginalString.Contains("/Account/Login?ReturnUrl=") == true),
                    "Anonymous access blocked: " + path);
            }
            // /ThucDon và /api/thuc-don là thực đơn công khai cho khách (S2-01); được kiểm thử trong DbTool verify với SQL Server.
            using var post = await client.PostAsync("/Tables/Create", new FormUrlEncodedContent(new Dictionary<string, string>()));
            Check(post.StatusCode == HttpStatusCode.Redirect && post.Headers.Location?.OriginalString.Contains("/Account/Login") == true,
                "Anonymous writes require login before running the action");
            var loginHtml = await client.GetStringAsync("/Account/Login?ReturnUrl=%2FTables");
            Check(loginHtml.Contains("name=\"returnUrl\" value=\"/Tables\"") && loginHtml.Contains("__RequestVerificationToken"), "Login preserves destination and includes anti-forgery token");
            Check(!loginHtml.Contains("href=\"/Areas\""), "Login page hides management navigation");
            using var css = await client.GetAsync("/css/site.css");
            Check(css.IsSuccessStatusCode, "Login styles available without authentication");
            using var invalidLogin = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string> { ["username"] = "test", ["password"] = "test" }));
            Check(invalidLogin.StatusCode == HttpStatusCode.BadRequest, "Login rejects requests missing anti-forgery token");
        }
        finally
        {
            if (!web.HasExited) { web.Kill(entireProcessTree: true); await web.WaitForExitAsync(); }
        }
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception("FAIL: " + label);
        Console.WriteLine("PASS: " + label);
    }
}
