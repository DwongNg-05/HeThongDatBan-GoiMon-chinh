using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace RestaurantManagement.DbTool;

/// <summary>
/// S1-05 Task 3 (AC3, AC4): nhật ký chỉ đọc tuyệt đối và kiểm soát truy cập chặt chẽ.
/// - Mọi vai trò không phải Quản lý (Waiter, Kitchen, Cashier) và người chưa đăng nhập: không thấy liên kết,
///   không vào được màn hình nhật ký kể cả bằng đường dẫn trực tiếp; database cũng từ chối.
/// - Không có thao tác sửa/xoá nhật ký trên giao diện; mọi lời gọi trực tiếp (POST/PUT/DELETE/PATCH) đều bị chặn.
/// - Ở database: chủ database bị trigger + khoá ngoại chặn UPDATE/DELETE/TRUNCATE; tài khoản ứng dụng bị DENY.
/// Bắt đầu và kết thúc khi client chưa đăng nhập.
/// </summary>
internal static class SecurityAuditAccessVerification
{
    private static readonly string[] ScreenUrls =
    [
        "/AuditLogs", "/AuditLogs/Index", "/auditlogs", "/AUDITLOGS/INDEX", "/AuditLogs/Index/1",
        "/AuditLogs?fromDate=2020-03-01&toDate=2020-03-31&userId=1", "/QuanLyMon/NhatKyGia/60"
    ];

    private static readonly string[] WriteUrls =
    [
        "/AuditLogs", "/AuditLogs/Index", "/AuditLogs/Index/1", "/AuditLogs/Delete/1", "/AuditLogs/Edit/1",
        "/AuditLogs/Update/1", "/AuditLogs/Xoa/1", "/AuditLogs/Sua/1", "/QuanLyMon/NhatKyGia/60"
    ];

    private static readonly HttpMethod[] WriteMethods = [HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete, HttpMethod.Patch];

    private static readonly string[] ManagerPages =
    [
        "/", "/AuditLogs", "/QuanLyMon/NhatKyGia/60", "/Management", "/QuanLyMon", "/QuanLyMon/Tao", "/QuanLyMon/Sua/60",
        "/QuanLyNhomMon", "/GoiMon", "/ThucDon", "/admin/employee-accounts", "/Areas", "/Tables", "/Reservations",
        "/OpeningHours", "/SpecialHolidays", "/Home/Privacy", "/Account/DoiMatKhau"
    ];

    // Các trang bắt buộc phải mở được để rà soát; các trang khác nếu lỗi môi trường thì ghi SKIP.
    private static readonly HashSet<string> RequiredPages = ["/", "/AuditLogs", "/QuanLyMon/NhatKyGia/60", "/Management", "/QuanLyMon"];

    internal static async Task Run(string connection, string password, HttpClient client)
    {
        var maxAudit = await Scalar<long>(connection, "SELECT ISNULL(MAX(Id),CAST(0 AS bigint)) FROM dbo.SecurityAuditLogs");
        var maxPrice = await Scalar<long>(connection, "SELECT ISNULL(MAX(Id),CAST(0 AS bigint)) FROM dbo.MenuPriceHistory");
        var before = await Fingerprint(connection, maxAudit, maxPrice);

        // 1. Mọi vai trò không phải Quản lý.
        foreach (var (user, role) in new[] { ("waiter", "Waiter"), ("kitchen", "Kitchen"), ("cashier", "Cashier") })
        {
            var userId = await Scalar<int>(connection, $"SELECT Id FROM dbo.Users WHERE UserName=N'{user}'");
            using (var login = await Login(client, user, password))
                Assert(login.StatusCode == HttpStatusCode.Redirect, $"{role}: signs in");
            var home = await client.GetStringAsync("/");
            Assert(!home.Contains("/AuditLogs", StringComparison.OrdinalIgnoreCase) && !WebUtility.HtmlDecode(home).Contains("Nhật ký hệ thống"),
                $"{role}: menu has no audit log link");
            var dishes = await client.GetStringAsync("/QuanLyMon");
            Assert(!dishes.Contains("NhatKyGia", StringComparison.OrdinalIgnoreCase), $"{role}: dish list has no price-history link");
            foreach (var url in ScreenUrls)
            {
                using var response = await client.GetAsync(url);
                Assert(response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location?.OriginalString.Contains("/Account/AccessDenied") == true,
                    $"{role}: direct URL {url} redirects to access denied");
                using var denied = await client.GetAsync(response.Headers.Location);
                var body = await denied.Content.ReadAsStringAsync();
                Assert(denied.StatusCode == HttpStatusCode.Forbidden && !body.Contains("security-audit-table") && !body.Contains("data-audit-id"),
                    $"{role}: {url} ends in 403 without log data");
            }
            await AssertWritesBlocked(client, home, role);
            await RejectDb(connection, $"EXEC dbo.usp_SecurityAuditList @ActorUserId={userId};", 51001, $"{role}: database refuses audit list");
            await RejectDb(connection, $"EXEC dbo.usp_SecurityAuditAccounts @ActorUserId={userId};", 51001, $"{role}: database refuses account list");
            await Logout(client);
        }

        // 2. Chưa đăng nhập.
        foreach (var url in ScreenUrls)
        {
            using var response = await client.GetAsync(url);
            Assert(response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location?.OriginalString.Contains("/Account/Login") == true,
                $"Anonymous: {url} redirects to login");
        }
        await AssertWritesBlocked(client, null, "Anonymous");

        // 3. Quản lý: rà soát giao diện không có thao tác sửa/xoá nhật ký; gọi trực tiếp cũng bị chặn.
        using (var login = await Login(client, "manager", password))
            Assert(login.StatusCode == HttpStatusCode.Redirect, "Manager signs in for UI review");
        string? auditHtml = null;
        foreach (var page in ManagerPages)
        {
            using var response = await client.GetAsync(page);
            if (!response.IsSuccessStatusCode)
            {
                Assert(!RequiredPages.Contains(page), $"Manager page {page} opens");
                Console.WriteLine($"SKIP: UI review of {page} (HTTP {(int)response.StatusCode})");
                continue;
            }
            var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
            if (page == "/AuditLogs") auditHtml = html;
            var postForms = Regex.Matches(html, "<form\\b[^>]*>", RegexOptions.IgnoreCase).Select(m => m.Value)
                .Where(f => Regex.IsMatch(f, "method=\"post\"", RegexOptions.IgnoreCase)).ToArray();
            Assert(postForms.All(f => !Regex.IsMatch(f, "auditlog|nhatky|securityaudit", RegexOptions.IgnoreCase)),
                $"UI review {page}: no POST form targets a log");
            Assert(!Regex.IsMatch(html, "(href|action|formaction)=\"[^\"]*/auditlogs/(?!index\\b)[^\"]*\"", RegexOptions.IgnoreCase)
                && !Regex.IsMatch(html, "(href|action|formaction)=\"[^\"]*nhatkygia[^\"]*(delete|edit|xoa|sua|remove)[^\"]*\"", RegexOptions.IgnoreCase),
                $"UI review {page}: no link to a log edit/delete action");
            Assert(!Regex.IsMatch(html, "(xo[áa]|s[ửu]a|delete|edit|remove)\\s+(b[ảa]n\\s+ghi\\s+)?nh[ậa]t\\s*k[ýy]", RegexOptions.IgnoreCase),
                $"UI review {page}: no edit/delete log wording");
        }
        Assert(auditHtml is not null, "Audit screen reviewed");
        var forms = Regex.Matches(auditHtml!, "<form\\b[^>]*>", RegexOptions.IgnoreCase).Select(m => m.Value).ToArray();
        Assert(forms.All(f => f.Contains("action=\"/Account/Logout\"") || (f.Contains("action=\"/AuditLogs\"") && f.Contains("method=\"get\""))),
            "Audit screen forms are only the GET filter and logout");
        var table = Regex.Match(auditHtml!, "<table[^>]*id=\"security-audit-table\".*?</table>", RegexOptions.Singleline).Value;
        Assert(table.Length > 0 && !Regex.IsMatch(table, "<(form|button|input|select|textarea|a)\\b", RegexOptions.IgnoreCase),
            "Audit table rows contain no buttons, links or inputs");
        var home2 = await client.GetStringAsync("/");
        await AssertWritesBlocked(client, home2, "Manager");
        await Logout(client);

        // 4. Quản lý bị hạ vai trò khi đang đăng nhập: cookie vẫn ghi "Manager" nhưng database từ chối → 403.
        var tempId = await CreateTempManager(connection, password);
        using (var login = await Login(client, "s105-temp-manager", password))
            Assert(login.StatusCode == HttpStatusCode.Redirect, "Temporary manager signs in");
        using (var allowed = await client.GetAsync("/AuditLogs"))
            Assert(allowed.StatusCode == HttpStatusCode.OK, "Temporary manager opens audit screen");
        await DatabaseTool.Execute(connection, $"UPDATE dbo.Users SET RoleId=2 WHERE Id={tempId};");
        using (var demoted = await client.GetAsync("/AuditLogs"))
        {
            Assert(demoted.StatusCode == HttpStatusCode.Redirect && demoted.Headers.Location?.OriginalString.Contains("/Account/AccessDenied") == true,
                "Demoted account is refused although its cookie still says Manager");
            var body = await demoted.Content.ReadAsStringAsync();
            Assert(!body.Contains("data-audit-id"), "Demoted account sees no log data");
        }
        await Logout(client);
        await DatabaseTool.Execute(connection, $"UPDATE dbo.Users SET IsActive=0 WHERE Id={tempId};");

        // 5. Gọi trực tiếp vào database.
        await DatabaseTool.Execute(connection, "IF USER_ID(N's105_audit_app') IS NULL BEGIN CREATE USER s105_audit_app WITHOUT LOGIN; ALTER ROLE restaurant_app ADD MEMBER s105_audit_app; END;");
        const string app = "s105_audit_app";
        foreach (var (sql, label) in new[]
        {
            ("SELECT TOP(1) Id FROM dbo.SecurityAuditLogs", "read the log table directly"),
            ("INSERT dbo.SecurityAuditLogs(UserName,Action,IpAddress) VALUES(N'giả mạo','LoginSucceeded','6.6.6.6')", "insert a forged entry"),
            ("UPDATE dbo.SecurityAuditLogs SET IpAddress='0.0.0.0'", "update entries"),
            ("DELETE dbo.SecurityAuditLogs", "delete entries"),
            ("UPDATE dbo.MenuPriceHistory SET NewPrice=NewPrice", "update price history"),
            ("DELETE dbo.AuditLogs", "delete business audit log")
        })
            Assert(await ErrorOf(connection, sql, app) == 229, $"App account cannot {label} (permission denied)");
        foreach (var (sql, label) in new[]
        {
            ("TRUNCATE TABLE dbo.SecurityAuditLogs", "truncate the log"),
            ("ALTER TABLE dbo.SecurityAuditLogs DISABLE TRIGGER tr_SecurityAuditLogs_AppendOnly", "disable the append-only trigger"),
            ("TRUNCATE TABLE dbo.MenuPriceHistory", "truncate price history")
        })
            Assert(await ErrorOf(connection, sql, app) != 0, $"App account cannot {label}");
        Assert(await ErrorOf(connection, "EXEC dbo.usp_SecurityAuditList @ActorUserId=1", app) == 0
            && await ErrorOf(connection, "EXEC dbo.usp_WriteLoginAudit @UserId=NULL,@UserName=N'ap*** (không tồn tại)',@Succeeded=0,@IpAddress='127.0.0.1'", app) == 0,
            "App account still writes and reads the log through stored procedures");

        foreach (var (sql, error, label) in new[]
        {
            ("UPDATE dbo.SecurityAuditLogs SET IpAddress='0.0.0.0'", 51110, "Owner cannot update security log (trigger)"),
            ("TRUNCATE TABLE dbo.SecurityAuditLogs", 4712, "Owner cannot truncate security log (foreign key guard)"),
            ("TRUNCATE TABLE dbo.AuditLogs", 4712, "Owner cannot truncate business audit log"),
            ("TRUNCATE TABLE dbo.MenuPriceHistory", 4712, "Owner cannot truncate price history"),
            ("UPDATE dbo.MenuPriceHistory SET NewPrice=NewPrice", 51112, "Owner cannot update price history (trigger)"),
            ("DELETE dbo.MenuPriceHistory", 51112, "Owner cannot delete price history (trigger)"),
            ("INSERT dbo.AuditTruncateGuard(Id) VALUES(1)", 547, "Truncate guard table always stays empty")
        })
            Assert(await ErrorOf(connection, sql, null) == error, label);

        // 6. Không bản ghi cũ nào bị đổi hay mất sau toàn bộ các lần thử.
        Assert(await Fingerprint(connection, maxAudit, maxPrice) == before, "Existing log and price-history rows are unchanged");
        Console.WriteLine("PASS: S1-05 Task 3 read-only log and access control checks.");
    }

    private static async Task AssertWritesBlocked(HttpClient client, string? pageWithToken, string who)
    {
        var token = pageWithToken is null ? "" : Token(pageWithToken);
        foreach (var url in WriteUrls)
        foreach (var method in WriteMethods)
        {
            using var request = new HttpRequestMessage(method, url)
            {
                Content = Form(("__RequestVerificationToken", token), ("Id", "1"), ("IpAddress", "0.0.0.0"))
            };
            using var response = await client.SendAsync(request);
            var location = response.Headers.Location?.OriginalString ?? "";
            var blocked = response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed
                || (response.StatusCode == HttpStatusCode.Redirect && (location.Contains("/Account/AccessDenied") || location.Contains("/Account/Login")));
            Assert(blocked, $"{who}: {method} {url} is rejected (HTTP {(int)response.StatusCode})");
        }
    }

    private static async Task<int> CreateTempManager(string connection, string password)
    {
        await using var cn = new SqlConnection(connection);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("""
            INSERT dbo.Users(RoleId,FullName,UserName,Phone,PasswordHash,MustChangePassword,IsActive)
            VALUES(1,N'Quản lý tạm S1-05',N's105-temp-manager','0000001055',@Hash,0,1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, cn);
        cmd.Parameters.AddWithValue("@Hash", BCrypt.Net.BCrypt.HashPassword(password, 4));
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>Chạy một câu lệnh (tuỳ chọn dưới danh nghĩa một database user) và trả về mã lỗi, 0 nếu thành công.</summary>
    private static async Task<int> ErrorOf(string connection, string sql, string? asUser)
    {
        // Không dùng lại kết nối trong pool để ngữ cảnh EXECUTE AS không thể rò sang kiểm thử khác.
        var builder = new SqlConnectionStringBuilder(connection) { Pooling = false };
        await using var cn = new SqlConnection(builder.ConnectionString);
        await cn.OpenAsync();
        var batch = (asUser is null ? "" : $"EXECUTE AS USER = N'{asUser}';\n")
            + $"DECLARE @e int=0;\nBEGIN TRY\n{sql};\nEND TRY\nBEGIN CATCH\nSET @e=ERROR_NUMBER();\nEND CATCH;\n"
            + (asUser is null ? "" : "REVERT;\n")
            + "SELECT @e AS ErrorNumber;";
        await using var cmd = new SqlCommand(batch, cn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var result = -1;
        do
        {
            while (await reader.ReadAsync())
                if (reader.FieldCount == 1 && reader.GetName(0) == "ErrorNumber") result = reader.GetInt32(0);
        } while (await reader.NextResultAsync());
        return result;
    }

    private static async Task RejectDb(string connection, string sql, int error, string name)
    {
        try { await DatabaseTool.Execute(connection, sql); }
        catch (SqlException ex) when (ex.Number == error) { Assert(true, name); return; }
        Assert(false, name);
    }

    private static Task<string> Fingerprint(string connection, long maxAudit, long maxPrice) => Scalar<string>(connection, $"""
        SELECT CONCAT(
         (SELECT COUNT_BIG(*) FROM dbo.SecurityAuditLogs WHERE Id<={maxAudit}),':',
         (SELECT CHECKSUM_AGG(BINARY_CHECKSUM(*)) FROM dbo.SecurityAuditLogs WHERE Id<={maxAudit}),':',
         (SELECT COUNT_BIG(*) FROM dbo.MenuPriceHistory WHERE Id<={maxPrice}),':',
         (SELECT CHECKSUM_AGG(BINARY_CHECKSUM(*)) FROM dbo.MenuPriceHistory WHERE Id<={maxPrice}))
        """);

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

    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
    }
}
