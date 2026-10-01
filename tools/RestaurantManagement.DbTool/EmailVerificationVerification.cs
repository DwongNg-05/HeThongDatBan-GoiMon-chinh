using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace RestaurantManagement.DbTool;

/// <summary>
/// Xác minh đăng nhập bằng email (HTTP thật, SQL Server thật). Web chạy với Email:Host trống nên email được ghi thành tệp
/// trong thư mục tạm; kiểm thử đọc mã từ tệp .txt giống như người dùng mở hộp thư.
/// </summary>
internal static class EmailVerificationVerification
{
    internal static async Task Run(string connection, string password)
    {
        var mailDir = Path.Combine(Path.GetTempPath(), "rm-email-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(mailDir);
        await DatabaseTool.Execute(connection, "UPDATE dbo.Users SET Email=NULL, MustChangePassword=0, FailedLoginCount=0, LockedUntil=NULL WHERE UserName IN (N'waiter',N'kitchen'); UPDATE dbo.Users SET MustChangePassword=0, FailedLoginCount=0, LockedUntil=NULL WHERE UserName=N'demo-manager';");
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var webRoot = Path.Combine(DatabaseTool.Root, "src", "RestaurantManagement.Web");
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = webRoot, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(webRoot, "bin", "Debug", "net10.0", "RestaurantManagement.Web.dll"));
        start.ArgumentList.Add("--urls"); start.ArgumentList.Add($"http://127.0.0.1:{port}");
        start.Environment["RM_CONNECTION_STRING"] = connection;
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["EmailVerification__Enabled"] = "true";
        start.Environment["Email__Host"] = "";
        start.Environment["Email__PickupDirectory"] = mailDir;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() };
            using var client = new HttpClient(handler) { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(15) };
            for (var i = 0; i < 60; i++)
            {
                try { using var r = await client.GetAsync("/Account/Login"); if (r.IsSuccessStatusCode) break; } catch (HttpRequestException) { }
                if (process.HasExited) throw new Exception("Web process exited before startup.");
                await Task.Delay(250);
            }

            // 1. Phục vụ đăng nhập → bị chuyển tới màn hình xác minh; mọi trang khác (kể cả đổi mật khẩu) đều bị chặn.
            using (var login = await Login(client, "waiter", password))
                Assert(login.StatusCode == HttpStatusCode.Redirect && Location(login).StartsWith("/Account/XacMinhEmail"), "Waiter login goes to email verification");
            foreach (var path in new[] { "/", "/Areas", "/QuanLyMon", "/Account/DoiMatKhau" })
            {
                using var blocked = await client.GetAsync(path);
                Assert(blocked.StatusCode == HttpStatusCode.Redirect && Location(blocked).StartsWith("/Account/XacMinhEmail"), $"Unverified waiter cannot open {path}");
            }

            // 2. Tài khoản chưa có email → nhập email để nhận mã.
            var page = await Html(client, "/Account/XacMinhEmail");
            Assert(page.Contains("name=\"email\"") && page.Contains("Gửi mã xác minh"), "Account without email is asked for an email address");
            using (var bad = await client.PostAsync("/Account/DatEmailXacMinh", Form(("email", "khong-hop-le"), ("__RequestVerificationToken", Token(page)))))
                Assert(bad.StatusCode == HttpStatusCode.Redirect, "Invalid email is rejected");
            page = await Html(client, "/Account/XacMinhEmail");
            Assert(page.Contains("Email không hợp lệ") && Mails(mailDir).Length == 0, "Invalid email shows an error and sends nothing");
            using (var set = await client.PostAsync("/Account/DatEmailXacMinh", Form(("email", "waiter.test@example.com"), ("__RequestVerificationToken", Token(page)))))
                Assert(set.StatusCode == HttpStatusCode.Redirect, "Email accepted");
            Assert(Mails(mailDir).Length == 1, "One verification email sent");
            var first = ReadCode(mailDir, 0);
            Assert(Regex.IsMatch(first, "^[A-Z0-9]{6}$") && first.Any(char.IsLetter) && first.Any(char.IsDigit), "Emailed code has 6 uppercase letters and digits");
            var html = await File.ReadAllTextAsync(Mails(mailDir)[0].Replace(".txt", ".html"));
            Assert(first.All(c => html.Contains($">{c}</div>")) && html.Contains("10 phút"), "HTML email shows the code in large boxes with validity");
            page = await Html(client, "/Account/XacMinhEmail");
            Assert(page.Contains("w*********t@example.com") && page.Contains("Gửi lại mã") && page.Contains("disabled=\"disabled\""), "Page shows masked email and a disabled resend button during cooldown");
            await Check(connection, "SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.Users WHERE UserName=N'waiter' AND Email IS NULL) THEN 1 ELSE 0 END", "Email is saved only after it is verified");

            // 3. Gửi lại quá sớm bị từ chối.
            using (var early = await client.PostAsync("/Account/GuiLaiMaXacMinh", Form(("__RequestVerificationToken", Token(page)))))
                Assert(early.StatusCode == HttpStatusCode.Redirect, "Early resend redirects");
            page = await Html(client, "/Account/XacMinhEmail");
            Assert(page.Contains("Vui lòng chờ") && Mails(mailDir).Length == 1, "Resend within cooldown is refused");

            // 4. Nhập sai mã, sai định dạng.
            page = await PostCode(client, page, "ABC");
            Assert(page.Contains("đúng 6 ký tự"), "Malformed code rejected with a clear message");
            var wrong = first == "ZZZZ22" ? "YYYY33" : "ZZZZ22";
            page = await PostCode(client, page, wrong);
            Assert(page.Contains("Mã xác minh không đúng") && page.Contains("còn 4 lần"), "Wrong code rejected with remaining attempts");

            // 5. Hết thời gian chờ → "Gửi lại mã" gửi mã mới, mã cũ hết hiệu lực.
            await DatabaseTool.Execute(connection, "UPDATE dbo.EmailVerificationCodes SET CreatedAt=DATEADD(minute,-2,CreatedAt) WHERE Email=N'waiter.test@example.com';");
            using (var resend = await client.PostAsync("/Account/GuiLaiMaXacMinh", Form(("__RequestVerificationToken", Token(page)))))
                Assert(resend.StatusCode == HttpStatusCode.Redirect, "Resend after cooldown accepted");
            Assert(Mails(mailDir).Length == 2, "Resend sends a second email");
            var second = ReadCode(mailDir, 1);
            page = await Html(client, "/Account/XacMinhEmail");
            Assert(page.Contains("Đã gửi mã mới"), "Resend confirmation shown");
            if (second != first)
            {
                page = await PostCode(client, page, first);
                Assert(page.Contains("không đúng"), "Old code no longer works after resend");
            }

            // 6. Mã đúng (gõ chữ thường, có khoảng trắng vẫn nhận) → vào được hệ thống.
            using (var ok = await client.PostAsync("/Account/XacMinhEmail", Form(("code", " " + second.ToLowerInvariant()[..3] + " " + second.ToLowerInvariant()[3..]), ("__RequestVerificationToken", Token(page)))))
                Assert(ok.StatusCode == HttpStatusCode.Redirect && !Location(ok).Contains("XacMinhEmail"), "Correct code completes verification");
            using (var home = await client.GetAsync("/"))
                Assert(home.StatusCode == HttpStatusCode.OK, "Verified waiter can use the system");
            await Check(connection, "SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.Users WHERE UserName=N'waiter' AND Email=N'waiter.test@example.com') AND NOT EXISTS(SELECT 1 FROM dbo.EmailVerificationCodes c JOIN dbo.LoginSessions s ON s.Id=c.SessionId WHERE c.Email=N'waiter.test@example.com' AND s.EmailVerifiedAt IS NULL AND s.RevokedAt IS NULL) THEN 1 ELSE 0 END",
                "Verified email saved to the account and session marked verified");
            await Logout(client);

            await DatabaseTool.Execute(connection, "UPDATE dbo.EmailVerificationCodes SET CreatedAt=DATEADD(minute,-2,CreatedAt) WHERE Email=N'waiter.test@example.com';");
            // 7. Lần đăng nhập sau: mã tự gửi tới email đã lưu; phải xác minh lại cho phiên mới.
            using (var again = await Login(client, "waiter", password))
                Assert(Location(again).StartsWith("/Account/XacMinhEmail"), "Every new login session must be verified again");
            Assert(Mails(mailDir).Length == 3, "Code sent automatically to the saved email on login");
            page = await Html(client, "/Account/XacMinhEmail");
            Assert(!page.Contains("name=\"email\" type=\"email\" class=\"form-control form-control-lg\"") && page.Contains("Đã gửi mã xác minh tới email của bạn"), "Saved email is used without asking again");
            await Logout(client);

            // 8. Bắt buộc đổi mật khẩu: xác minh email trước, đổi mật khẩu sau.
            await DatabaseTool.Execute(connection, "UPDATE dbo.Users SET MustChangePassword=1 WHERE UserName=N'kitchen';");
            using (var kitchen = await Login(client, "kitchen", password))
                Assert(Location(kitchen).StartsWith("/Account/XacMinhEmail"), "Kitchen with required password change verifies email first");
            page = await Html(client, "/Account/XacMinhEmail");
            using (var set = await client.PostAsync("/Account/DatEmailXacMinh", Form(("email", "kitchen.test@example.com"), ("__RequestVerificationToken", Token(page)))))
                Assert(set.StatusCode == HttpStatusCode.Redirect, "Kitchen email accepted");
            page = await Html(client, "/Account/XacMinhEmail");
            using (var ok = await client.PostAsync("/Account/XacMinhEmail", Form(("code", ReadCode(mailDir, 3)), ("__RequestVerificationToken", Token(page)))))
                Assert(Location(ok).StartsWith("/Account/DoiMatKhau"), "After email verification the user goes to change password");
            using (var change = await client.GetAsync("/Account/DoiMatKhau"))
                Assert(change.StatusCode == HttpStatusCode.OK, "Password change page reachable after verification");
            await Logout(client);
            await DatabaseTool.Execute(connection, "UPDATE dbo.Users SET MustChangePassword=0 WHERE UserName=N'kitchen';");

            // 9. Quản lý không phải xác minh (demo-manager: mật khẩu "manager" đã được đổi ở bước kiểm thử S1-03).
            var before = Mails(mailDir).Length;
            using (var manager = await Login(client, "demo-manager", password))
                Assert(manager.StatusCode == HttpStatusCode.Redirect && !Location(manager).Contains("XacMinhEmail"), "Manager is not asked to verify email");
            using (var home = await client.GetAsync("/"))
                Assert(home.StatusCode == HttpStatusCode.OK && Mails(mailDir).Length == before, "Manager uses the system directly, no email sent");
            await Logout(client);

            // 10. Database chỉ lưu giá trị băm; tài khoản ứng dụng không đọc trực tiếp bảng mã.
            await Check(connection, "SELECT CASE WHEN COL_LENGTH('dbo.EmailVerificationCodes','Code') IS NULL AND COL_LENGTH('dbo.EmailVerificationCodes','CodeHash')=32 THEN 1 ELSE 0 END", "Only a SHA-256 hash of the code is stored");
            Console.WriteLine("PASS: Email login verification checks.");
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await Task.WhenAll(output, errors);
            await DatabaseTool.Execute(connection, "UPDATE dbo.Users SET Email=NULL WHERE UserName IN (N'waiter',N'kitchen');");
            try { Directory.Delete(mailDir, true); } catch (IOException) { }
        }
    }

    private static string[] Mails(string dir) => Directory.GetFiles(dir, "*.txt").Order(StringComparer.Ordinal).ToArray();

    private static string ReadCode(string dir, int index)
    {
        var text = File.ReadAllText(Mails(dir)[index]);
        return Regex.Match(text, @"Subject: .*: ([A-Z0-9]{6})").Groups[1].Value;
    }

    private static async Task<string> PostCode(HttpClient client, string page, string code)
    {
        using var response = await client.PostAsync("/Account/XacMinhEmail", Form(("code", code), ("__RequestVerificationToken", Token(page))));
        Assert(response.StatusCode == HttpStatusCode.OK, "Code form re-displayed: " + code);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private static async Task<string> Html(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert(response.StatusCode == HttpStatusCode.OK, "Opens " + path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private static async Task<HttpResponseMessage> Login(HttpClient client, string identifier, string secret)
    {
        var html = await client.GetStringAsync("/Account/Login");
        return await client.PostAsync("/Account/Login", Form(("Identifier", identifier), ("Password", secret), ("__RequestVerificationToken", Token(html))));
    }

    private static async Task Logout(HttpClient client)
    {
        using var page = await client.GetAsync("/Account/XacMinhEmail");
        var html = await page.Content.ReadAsStringAsync();
        foreach (var fallback in new[] { "/", "/Account/DoiMatKhau" })
        {
            if (html.Contains("__RequestVerificationToken")) break;
            using var other = await client.GetAsync(fallback);
            html = await other.Content.ReadAsStringAsync();
        }
        using var _ = await client.PostAsync("/Account/Logout", Form(("__RequestVerificationToken", Token(html))));
    }

    private static string Location(HttpResponseMessage response) => response.Headers.Location?.OriginalString ?? "";
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
