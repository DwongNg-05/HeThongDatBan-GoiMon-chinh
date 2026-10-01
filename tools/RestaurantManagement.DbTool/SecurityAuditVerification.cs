using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace RestaurantManagement.DbTool;

/// <summary>
/// S1-05 Task 1: nhật ký đăng nhập thành công/thất bại và sửa giá món (thời điểm, tài khoản, vai trò, hành động, IP),
/// lưu ở dbo.SecurityAuditLogs; màn hình /AuditLogs chỉ cho vai trò Quản lý.
/// Bắt đầu và kết thúc khi client chưa đăng nhập.
/// </summary>
internal static class SecurityAuditVerification
{
    private const string Ip = "127.0.0.1";

    internal static async Task Run(string connection, string password, HttpClient client)
    {
        var baseId = await Scalar<long>(connection, "SELECT ISNULL(MAX(Id),CAST(0 AS bigint)) FROM dbo.SecurityAuditLogs");
        string New(string condition) =>
            $"SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.SecurityAuditLogs WHERE Id>{baseId} AND {condition}) THEN 1 ELSE 0 END";

        // Đăng nhập thất bại: tài khoản có thật (theo tên và theo số điện thoại) và định danh không tồn tại.
        using (var failed = await Login(client, "manager", "WrongPassword9!"))
            Assert(failed.StatusCode == HttpStatusCode.OK, "Wrong password is rejected");
        await Check(connection, New($"Action='LoginFailed' AND UserId=1 AND UserName=N'manager' AND RoleCode='Manager' AND IpAddress='{Ip}' AND OccurredAt>DATEADD(minute,-1,SYSUTCDATETIME())"),
            "Failed login records time, account, role and IP");
        using (var byPhone = await Login(client, "0900000001", "WrongPassword9!"))
            Assert(byPhone.StatusCode == HttpStatusCode.OK, "Wrong password by phone is rejected");
        await Check(connection, $"SELECT CASE WHEN (SELECT COUNT(*) FROM dbo.SecurityAuditLogs WHERE Id>{baseId} AND Action='LoginFailed' AND UserId=1 AND UserName=N'manager')=2 THEN 1 ELSE 0 END",
            "Failed login by phone is recorded under the real account name");
        using (var unknown = await Login(client, "s105-khong-ton-tai", "WrongPassword9!"))
            Assert(unknown.StatusCode == HttpStatusCode.OK, "Unknown account is rejected");
        await Check(connection, New($"Action='LoginFailed' AND UserId IS NULL AND UserName=N's1*** (không tồn tại)' AND RoleCode IS NULL AND IpAddress='{Ip}'"),
            "Failed login for unknown account is recorded with masked identifier and no role");
        await Check(connection, $"SELECT CASE WHEN NOT EXISTS(SELECT 1 FROM dbo.SecurityAuditLogs WHERE UserName LIKE N'%khong-ton-tai%' OR UserName LIKE N'%WrongPassword%') THEN 1 ELSE 0 END",
            "Raw unknown identifier and passwords are never stored");

        // Đăng nhập thành công.
        using (var success = await Login(client, "manager", password))
            Assert(success.StatusCode == HttpStatusCode.Redirect, "Manager signs in");
        await Check(connection, New($"Action='LoginSucceeded' AND UserId=1 AND UserName=N'manager' AND RoleCode='Manager' AND IpAddress='{Ip}'"),
            "Successful login records time, account, role and IP");

        // Sửa giá món ở màn hình Quản lý thực đơn.
        var oldPrice = await Scalar<decimal>(connection, "SELECT Price FROM dbo.MenuItems WHERE Id=59");
        var newPrice = oldPrice + 1000;
        var management = await client.GetStringAsync("/Management");
        using (var price = await client.PostAsync("/Management/Price", Form(("id", "59"), ("price", newPrice.ToString("0")), ("__RequestVerificationToken", Token(management)))))
            Assert(price.StatusCode == HttpStatusCode.Redirect, "Manager changes a dish price");
        await Check(connection, New($"Action='PriceChanged' AND UserId=1 AND UserName=N'manager' AND RoleCode='Manager' AND IpAddress='{Ip}' AND Detail LIKE N'%(#59): {Vnd(oldPrice)} ₫ → {Vnd(newPrice)} ₫'"),
            "Price change records time, account, role, IP and old/new price");
        management = await client.GetStringAsync("/Management");
        using (var same = await client.PostAsync("/Management/Price", Form(("id", "59"), ("price", newPrice.ToString("0")), ("__RequestVerificationToken", Token(management)))))
            Assert(same.StatusCode == HttpStatusCode.Redirect, "Saving the same price is accepted");
        await Check(connection, $"SELECT CASE WHEN (SELECT COUNT(*) FROM dbo.SecurityAuditLogs WHERE Id>{baseId} AND Action='PriceChanged' AND Detail LIKE N'%(#59)%')=1 THEN 1 ELSE 0 END",
            "Saving an unchanged price adds no price-change entry");

        // Sửa giá món ở màn hình Sửa món (Quản lý món).
        await using (var cn = new SqlConnection(connection))
        {
            await cn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT Name,CategoryId,CAST(Price AS int),Unit,ISNULL(Description,N''),EstimatedPrepMinutes FROM dbo.MenuItems WHERE Id=58", cn);
            await using var r = await cmd.ExecuteReaderAsync();
            await r.ReadAsync();
            var (name, category, price58, unit, description, prep) = (r.GetString(0), r.GetInt32(1), r.GetInt32(2), r.GetString(3), r.GetString(4), r.GetInt32(5));
            await r.CloseAsync();
            var edit = await client.GetStringAsync("/QuanLyMon/Sua/58");
            using var saved = await client.PostAsync("/QuanLyMon/Sua/58", Form(
                ("__RequestVerificationToken", Token(edit)), ("Mon.Id", "58"), ("Mon.Ten", name), ("Mon.NhomMonId", category.ToString()),
                ("Mon.GiaBanVnd", (price58 + 2000).ToString()), ("Mon.DonViTinh", unit), ("Mon.MoTaNgan", description),
                ("Mon.ThoiGianCheBienPhut", prep.ToString()), ("Mon.TrangThai", "0")));
            Assert(saved.StatusCode == HttpStatusCode.Redirect, "Manager changes a price on the dish edit screen");
            await Check(connection, New($"Action='PriceChanged' AND UserId=1 AND RoleCode='Manager' AND IpAddress='{Ip}' AND Detail LIKE N'%(#58): {Vnd(price58)} ₫ → {Vnd(price58 + 2000)} ₫'"),
                "Price change from dish edit screen is recorded with IP");
        }

        // Màn hình nhật ký của quản lý: đủ cột, mới nhất lên đầu.
        using (var page = await client.GetAsync("/AuditLogs"))
        {
            Assert(page.StatusCode == HttpStatusCode.OK, "Manager opens audit log screen");
            var html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
            foreach (var header in new[] { "Thời điểm", "Tài khoản", "Vai trò", "Hành động", "Địa chỉ IP" })
                Assert(html.Contains($"<th scope=\"col\">{header}</th>"), "Audit screen column: " + header);
            var ids = Regex.Matches(html, "data-audit-id=\"(\\d+)\"").Select(m => long.Parse(m.Groups[1].Value)).ToArray();
            Assert(ids.Length >= 6 && ids.SequenceEqual(ids.OrderByDescending(id => id)), "Audit screen lists newest first");
            var first = Regex.Match(html, "<tr data-audit-id=.*?</tr>", RegexOptions.Singleline).Value;
            Assert(first.Contains("data-action=\"PriceChanged\"") && first.Contains("Sửa giá món") && first.Contains("(#58)"), "Most recent event (dish edit price change) is first");
            Assert(html.Contains("Đăng nhập thành công") && html.Contains("Đăng nhập thất bại") && html.Contains("Quản lý")
                && html.Contains($"<code>{Ip}</code>") && html.Contains("s1*** (không tồn tại)") && html.Contains("Không xác định"),
                "Audit screen shows login success/failure, role names, unknown account and IP");
            Assert(Regex.IsMatch(html, "\\d{2}/\\d{2}/\\d{4} \\d{2}:\\d{2}:\\d{2}"), "Audit screen shows full timestamp");
            Assert(!Regex.IsMatch(html, "<form(?=[^>]*method=\"post\")(?=[^>]*AuditLogs)[^>]*>", RegexOptions.IgnoreCase), "Audit screen has no edit/delete (POST) form");
        }
        await SecurityAuditFilterVerification.Run(connection, client);
        await Logout(client);

        // Vai trò khác bị chặn.
        using (var waiter = await Login(client, "waiter", password))
            Assert(waiter.StatusCode == HttpStatusCode.Redirect, "Waiter signs in");
        await Check(connection, New($"Action='LoginSucceeded' AND UserName=N'waiter' AND RoleCode='Waiter' AND IpAddress='{Ip}'"),
            "Waiter login recorded with Waiter role");
        using (var denied = await client.GetAsync("/AuditLogs"))
        {
            Assert(denied.StatusCode == HttpStatusCode.Redirect && denied.Headers.Location?.OriginalString.Contains("/Account/AccessDenied") == true,
                "Non-manager is redirected to access denied");
            using var forbidden = await client.GetAsync(denied.Headers.Location);
            Assert(forbidden.StatusCode == HttpStatusCode.Forbidden, "Access denied returns 403");
        }
        var home = await client.GetStringAsync("/");
        Assert(!home.Contains("href=\"/AuditLogs\""), "Non-manager menu hides audit log link");
        await Logout(client);
        await Reject(connection, "EXEC dbo.usp_SecurityAuditList @ActorUserId=2;", 51001, "Database also rejects audit list for non-manager");

        using (var anonymous = await client.GetAsync("/AuditLogs"))
            Assert(anonymous.StatusCode == HttpStatusCode.Redirect && anonymous.Headers.Location?.OriginalString.Contains("/Account/Login") == true,
                "Anonymous user is sent to login");

        // S1-05 Task 3: chỉ đọc tuyệt đối và kiểm soát truy cập với mọi vai trò.
        await SecurityAuditAccessVerification.Run(connection, password, client);

        // Kho nhật ký chỉ thêm.
        await Reject(connection, "UPDATE dbo.SecurityAuditLogs SET IpAddress='0.0.0.0';", 51110, "Audit entries cannot be updated");
        await Reject(connection, "DELETE dbo.SecurityAuditLogs;", 51110, "Audit entries cannot be deleted");
        Console.WriteLine("PASS: S1-05 Task 1 security audit log checks.");
    }

    private static string Vnd(decimal value) => value.ToString("#,##0", new System.Globalization.NumberFormatInfo { NumberGroupSeparator = "." });

    private static async Task<HttpResponseMessage> Login(HttpClient client, string identifier, string secret)
    {
        var html = await client.GetStringAsync("/Account/Login");
        return await client.PostAsync("/Account/Login", Form(("Identifier", identifier), ("Password", secret), ("__RequestVerificationToken", Token(html))));
    }

    private static async Task Logout(HttpClient client)
    {
        var page = await client.GetStringAsync("/");
        using var response = await client.PostAsync("/Account/Logout", Form(("__RequestVerificationToken", Token(page))));
        Assert(response.StatusCode == HttpStatusCode.Redirect, "Logged out");
    }

    private static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private static FormUrlEncodedContent Form(params (string Name, string Value)[] pairs) => new(pairs.Select(p => new KeyValuePair<string, string>(p.Name, p.Value)));

    private static async Task<T> Scalar<T>(string connection, string sql)
    {
        await using var cn = new SqlConnection(connection);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        return (T)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task Check(string connection, string sql, string name) => Assert(await Scalar<int>(connection, sql) == 1, name);

    private static async Task Reject(string connection, string sql, int error, string name)
    {
        try { await DatabaseTool.Execute(connection, sql); }
        catch (SqlException ex) when (ex.Number == error) { Assert(true, name); return; }
        Assert(false, name);
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
    }
}
