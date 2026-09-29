using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace RestaurantManagement.DbTool;

internal static partial class LoginVerification
{
    internal static async Task Run(string connection, string password)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var webRoot = Path.Combine(DatabaseTool.Root, "src", "RestaurantManagement.Web");
        var dll = Path.Combine(webRoot, "bin", "Debug", "net10.0", "RestaurantManagement.Web.dll");
        if (!File.Exists(dll)) throw new InvalidOperationException("Build the solution (Debug) before verify.");
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = webRoot, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(dll);
        start.ArgumentList.Add("--urls");
        start.ArgumentList.Add($"http://127.0.0.1:{port}");
        start.Environment["RM_CONNECTION_STRING"] = connection;
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        using var process = Process.Start(start)!;
        // Drain logs without printing request details or credentials.
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() };
            using var client = new HttpClient(handler) { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(10) };
            bool ready = false;
            for (var i = 0; i < 60; i++)
            {
                try { using var response = await client.GetAsync("/Account/Login"); ready = response.IsSuccessStatusCode; if (ready) break; }
                catch (HttpRequestException) { }
                if (process.HasExited) throw new Exception("Web process exited before startup.");
                await Task.Delay(250);
            }
            Assert(ready, "Web starts");
            Assert((await client.GetAsync("/Management")).StatusCode == HttpStatusCode.Redirect, "Anonymous management access denied");
            Assert((await client.GetAsync("/admin/employee-accounts")).StatusCode == HttpStatusCode.Redirect, "Anonymous employee account access denied");

            async Task<HttpResponseMessage> Login(string identifier, string secret)
            {
                var html = await client.GetStringAsync("/Account/Login");
                return await client.PostAsync("/Account/Login", Form(("Identifier", identifier), ("Password", secret), ("__RequestVerificationToken", Token(html))));
            }
            const string error = "Tên đăng nhập, số điện thoại hoặc mật khẩu không hợp lệ.";
            foreach (var identifier in new[] { "manager", "0900000001", "missing-user", "0999999999" })
            {
                using var failed = await Login(identifier, "WrongPassword9!");
                var body = WebUtility.HtmlDecode(await failed.Content.ReadAsStringAsync());
                Assert(failed.StatusCode == HttpStatusCode.OK && body.Contains(error), "Same generic error: " + identifier);
                Assert((await client.GetAsync("/Management")).StatusCode == HttpStatusCode.Redirect, "Failed login creates no authenticated session");
            }
            Assert((await client.PostAsync("/Account/Login", Form(("Identifier", "manager"), ("Password", password)))).StatusCode == HttpStatusCode.BadRequest, "Login requires CSRF token");
            foreach (var identifier in new[] { "manager", "0900000001" })
            {
                using var success = await Login(identifier, password);
                Assert(success.StatusCode == HttpStatusCode.Redirect, "Login succeeds: " + identifier);
                var cookies = success.Headers.GetValues("Set-Cookie").Where(c => c.StartsWith("RestaurantManagement.Auth="));
                Assert(cookies.Any(c => c.Contains("httponly", StringComparison.OrdinalIgnoreCase) && !c.Contains("expires=", StringComparison.OrdinalIgnoreCase)), "Protected browser-session cookie");
                var page = await client.GetStringAsync("/Management");
                Assert(WebUtility.HtmlDecode(page).Contains("Đăng nhập thành công."), "Shows login success");
                Assert(Regex.IsMatch(page, @"<strong[^>]*>manager</strong>"), "Session identifies manager");
                Assert((await client.GetAsync("/admin/employee-accounts")).IsSuccessStatusCode, "Manager can open merged employee account list");
                Assert((await client.GetAsync("/admin/employee-accounts/create")).IsSuccessStatusCode, "Manager can open merged employee creation form");
                var token = Token(page);
                Assert((await client.PostAsync("/Management/Price", Form(("id", "60"), ("price", "87654")))).StatusCode == HttpStatusCode.BadRequest, "Menu writes require CSRF token");
                using var price = await client.PostAsync("/Management/Price", Form(("id", "60"), ("price", "87654"), ("ActorUserId", "4"), ("__RequestVerificationToken", token)));
                using var availability = await client.PostAsync("/Management/Availability", Form(("id", "60"), ("soldOut", "true"), ("ActorUserId", "4"), ("__RequestVerificationToken", token)));
                Assert(price.StatusCode == HttpStatusCode.Redirect && availability.StatusCode == HttpStatusCode.Redirect, "Menu writes succeed");
                await Check(connection, "SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.MenuPriceHistory WHERE MenuItemId=60 AND ChangedBy=1 AND NewPrice=87654) AND EXISTS(SELECT 1 FROM dbo.MenuAvailabilityEvents WHERE MenuItemId=60 AND ChangedBy=1) AND NOT EXISTS(SELECT 1 FROM dbo.MenuPriceHistory WHERE MenuItemId=60 AND ChangedBy<>1) AND EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE EntityId='60' AND Action='PriceChanged' AND ActorUserId=1) THEN 1 ELSE 0 END", "Audit actor comes from session, ignoring forged actor");
                using var logout = await client.PostAsync("/Account/Logout", Form(("__RequestVerificationToken", token)));
                Assert(logout.StatusCode == HttpStatusCode.Redirect && (await client.GetAsync("/Management")).StatusCode == HttpStatusCode.Redirect, "Logout clears session");
            }
            // Add a second manager to an already populated database; reruns must not reset credentials.
            await DatabaseTool.SeedLoginDemo(connection, password, "demo-manager", "0000000099");
            await DatabaseTool.SeedLoginDemo(connection, "DifferentPassword9!", "demo-manager", "0000000099");
            await Check(connection, "SELECT CASE WHEN (SELECT COUNT(*) FROM dbo.Users WHERE UserName='demo-manager')=1 AND (SELECT COUNT(*) FROM dbo.MenuItems WHERE CategoryId=(SELECT Id FROM dbo.MenuCategories WHERE Name=N'Thực đơn demo đăng nhập'))=3 THEN 1 ELSE 0 END", "Login demo can be added to existing data and rerun without duplicates");
            foreach (var identifier in new[] { "demo-manager", "0000000099" })
            {
                using var success = await Login(identifier, password);
                Assert(success.StatusCode == HttpStatusCode.Redirect, "Second manager login: " + identifier);
                var page = await client.GetStringAsync("/Management");
                Assert(Regex.IsMatch(page, @"<strong[^>]*>demo-manager</strong>"), "Session switches to the second manager");
                var token = Token(page);
                using var price = await client.PostAsync("/Management/Price", Form(("id", "60"), ("price", "87655"), ("ActorUserId", "1"), ("__RequestVerificationToken", token)));
                using var availability = await client.PostAsync("/Management/Availability", Form(("id", "60"), ("soldOut", "false"), ("ActorUserId", "1"), ("__RequestVerificationToken", token)));
                Assert(price.StatusCode == HttpStatusCode.Redirect && availability.StatusCode == HttpStatusCode.Redirect, "Second manager can change menu");
                await Check(connection, "DECLARE @actor int=(SELECT Id FROM dbo.Users WHERE UserName='demo-manager'); SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.MenuPriceHistory WHERE MenuItemId=60 AND ChangedBy=@actor AND NewPrice=87655) AND EXISTS(SELECT 1 FROM dbo.MenuAvailabilityEvents WHERE MenuItemId=60 AND ChangedBy=@actor AND IsSoldOut=0) AND EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE EntityId='60' AND Action='PriceChanged' AND ActorUserId=@actor) THEN 1 ELSE 0 END", "Audit records second manager rather than a hardcoded user");
                var history = WebUtility.HtmlDecode(await client.GetStringAsync("/Management"));
                Assert(history.Contains("<td>demo-manager</td>"), "History displays the actual actor");
                using var logout = await client.PostAsync("/Account/Logout", Form(("__RequestVerificationToken", token)));
            }
            // Leave the original business-test fixtures intact.
            await DatabaseTool.Execute(connection, "DELETE dbo.MenuItems WHERE CategoryId=(SELECT Id FROM dbo.MenuCategories WHERE Name=N'Thực đơn demo đăng nhập'); DELETE dbo.MenuCategories WHERE Name=N'Thực đơn demo đăng nhập';");
            await DatabaseTool.Execute(connection, "UPDATE dbo.Users SET IsActive=0 WHERE Id=1;");
            using var inactive = await Login("manager", password);
            Assert(WebUtility.HtmlDecode(await inactive.Content.ReadAsStringAsync()).Contains(error), "Inactive account receives same generic error");
            await DatabaseTool.Execute(connection, "UPDATE dbo.Users SET IsActive=1 WHERE Id=1;");
            await using var cn = new SqlConnection(connection);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT PasswordHash FROM dbo.Users WHERE Id=1", cn);
            var hash = (string)(await cmd.ExecuteScalarAsync())!;
            Assert(hash != password && hash.StartsWith("$2") && BCrypt.Net.BCrypt.Verify(password, hash), "Database stores valid bcrypt hash, never plaintext");
            await VerifyLockout(connection, password, client, Login);
            await VerifyIdleSessions(connection, password, client, Login);
            Console.WriteLine("PASS: S1-01 HTTP authentication, session and audit checks.");
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await Task.WhenAll(output, errors);
        }
    }

    private static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private static FormUrlEncodedContent Form(params (string Name, string Value)[] pairs) => new(pairs.Select(p => new KeyValuePair<string, string>(p.Name, p.Value)));
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
