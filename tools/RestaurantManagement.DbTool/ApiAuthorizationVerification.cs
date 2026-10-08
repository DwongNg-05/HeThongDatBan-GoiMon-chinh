using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using static RestaurantManagement.DbTool.BookingConfirmationVerification;

namespace RestaurantManagement.DbTool;

/// <summary>
/// S1-04 Task 4 (web thật, SQL Server thật): gọi TOÀN BỘ API trong <see cref="ApiAccessMatrix"/> khi chưa đăng nhập,
/// với từng vai trò (Quản lý, Phục vụ, Bếp, Thu ngân) và với một tài khoản có vai trò không xác định.
/// Mọi API ngoài quyền phải bị chặn ở máy chủ; API trong quyền phải đi qua được bước kiểm tra quyền.
/// Lời gọi ghi (POST) cố tình không kèm mã chống giả mạo: nếu được phép thì dừng ở 400, không thay đổi dữ liệu.
/// </summary>
internal static class ApiAuthorizationVerification
{
    private const string UnknownRole = "Guest";
    private const string UnknownUser = "s104-unknown-role";

    /// <summary>Mật khẩu đặt lại cho các tài khoản kiểm thử (dùng tiếp ở KitchenCashierVerification).</summary>
    internal const string Password = "S104-Api-Check-9x";

    /// <summary>
    /// S1-04 Task 3: mọi màn hình giao diện trên thanh điều hướng (khoá → đường dẫn), khớp RoleNavigation.Items
    /// (RoleNavigationTests đối chiếu). Dùng để gõ thẳng từng đường dẫn với từng vai trò.
    /// </summary>
    internal static readonly Dictionary<string, string> ScreenPaths = new()
    {
        ["reservations"] = "/ReservationManagement", ["areas"] = "/Areas", ["table-map"] = "/", ["opening-hours"] = "/OpeningHours",
        ["privacy"] = "/Home/Privacy", ["dish-categories"] = "/DishCategories", ["dishes"] = "/Dishes", ["menu"] = "/Menu",
        ["ordering"] = "/Ordering", ["kitchen-orders"] = "/Kitchen", ["daily-dishes"] = "/Kitchen/Dishes",
        ["cashier-payments"] = "/Cashier", ["cashier-invoices"] = "/Cashier/Invoices", ["cashier-shift"] = "/Cashier/Shift",
        ["shift-reports"] = "/ShiftReports", ["employee-accounts"] = "/admin/employee-accounts", ["audit-logs"] = "/AuditLogs"
    };

    /// <summary>Màn hình công khai cho khách (thực đơn): ai cũng mở được, không tính là ngoài quyền.</summary>
    private static readonly string[] PublicScreens = { "menu" };

    /// <summary>Thêm vài màn hình không có trên thanh điều hướng nhưng gõ thẳng được (form tạo/sửa, cấu hình).</summary>
    private static readonly (string Path, string[] Roles)[] ExtraScreens =
    {
        ("/Dishes/Create", new[] { "Manager" }), ("/DishCategories/Create", new[] { "Manager" }), ("/Tables", new[] { "Manager" }),
        ("/SpecialHolidays", new[] { "Manager" }), ("/admin/employee-accounts/create", new[] { "Manager" })
        // "/Reservations/Create" nay là trang công khai (khách đặt bàn không cần đăng nhập) nên không còn trong danh sách giới hạn quyền.
    };

    /// <summary>
    /// S1-04 Task 1/2: thanh điều hướng mong đợi theo vai trò (khoá data-nav-item) và trang đầu tiên khi mở "/".
    /// Phục vụ: đúng 3 mục; Bếp: đúng 2 mục (màn hình bếp, món trong ngày — S2-08 Task 1); Thu ngân: đúng 3 mục.
    /// </summary>
    internal static readonly Dictionary<string, (string Landing, string[] Nav)> ExpectedNavigation = new()
    {
        ["Manager"] = ("/", new[] { "reservations", "areas", "table-map", "opening-hours", "privacy", "dish-categories", "dishes", "menu", "ordering",
            "kitchen-orders", "daily-dishes", "cashier-payments", "cashier-invoices", "cashier-shift", "shift-reports", "employee-accounts", "audit-logs" }),
        ["Waiter"] = ("/", new[] { "reservations", "table-map", "ordering" }),
        ["Kitchen"] = ("/Kitchen", new[] { "kitchen-orders", "daily-dishes" }),
        ["Cashier"] = ("/Cashier", new[] { "cashier-payments", "cashier-invoices", "cashier-shift" })
    };

    internal static async Task Run(string connection)
    {
        const string password = Password;
        await using (var cn = new SqlConnection(connection))
        {
            await cn.OpenAsync();
            await using var cmd = new SqlCommand("""
                IF NOT EXISTS(SELECT 1 FROM dbo.Roles WHERE Code='Guest')
                 INSERT dbo.Roles(Id,Code,Name) VALUES((SELECT MAX(Id)+1 FROM dbo.Roles),'Guest',N'Vai trò thử (không xác định)');
                IF NOT EXISTS(SELECT 1 FROM dbo.Users WHERE UserName=N's104-unknown-role')
                 INSERT dbo.Users(RoleId,FullName,UserName,Phone,IsActive)
                 VALUES((SELECT Id FROM dbo.Roles WHERE Code='Guest'),N'Vai trò không xác định','s104-unknown-role','0900000098',1);
                UPDATE dbo.Users SET PasswordHash=@hash,MustChangePassword=0,FailedLoginCount=0,FailureWindowStartedAt=NULL,LockedUntil=NULL,IsActive=1
                 WHERE UserName IN (N'manager',N'waiter',N'kitchen',N'cashier',N's104-unknown-role');
                """, cn);
            cmd.Parameters.AddWithValue("@hash", BCrypt.Net.BCrypt.HashPassword(password, workFactor: 10));
            await cmd.ExecuteNonQueryAsync();
        }

        await using var web = await Web.Start(connection, new() { ["Email__RetryPollSeconds"] = "0" });
        var principals = new (string Label, string? User, string? Role)[]
        {
            ("Chưa đăng nhập", null, null),
            ("Quản lý", "manager", "Manager"), ("Phục vụ", "waiter", "Waiter"),
            ("Bếp", "kitchen", "Kitchen"), ("Thu ngân", "cashier", "Cashier"),
            ("Vai trò không xác định", UnknownUser, UnknownRole)
        };

        var failures = new List<string>();
        var checkedCount = 0;

        // S1-04: quyền trong database khớp phạm vi của từng vai trò ở web (migration 031_RolePermissionsByScope.sql, 032_RemoveKitchenDishes.sql).
        var extraDbPermissions = await Scalar(connection, """
            SELECT COUNT(*) FROM dbo.RolePermissions rp
            JOIN dbo.Roles r ON r.Id=rp.RoleId JOIN dbo.Permissions p ON p.Id=rp.PermissionId
            WHERE (r.Code='Waiter' AND p.Code IN ('Menu.Availability','Kitchen.Read','Payments.Read'))
               OR (r.Code='Kitchen' AND p.Code='Menu.Availability')
               OR (r.Code IN ('Kitchen','Cashier') AND p.Code='Reservations.Read')
            """);
        if (extraDbPermissions != 0)
            failures.Add($"Database còn {extraDbPermissions} quyền rộng hơn web (Phục vụ: tạm hết/bếp/hoá đơn; Bếp: tạm hết; Bếp, Thu ngân: xem đặt bàn). Hãy chạy migrate để áp dụng 031 và 032.");
        else
            Console.WriteLine("PASS: S1-04 — quyền trong database khớp phạm vi từng vai trò ở web.");

        foreach (var (label, user, role) in principals)
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() };
            using var client = new HttpClient(handler) { BaseAddress = web.Client.BaseAddress, Timeout = TimeSpan.FromSeconds(30) };
            if (user is not null) await Login(client, user, password);
            if (role is not null && ExpectedNavigation.TryGetValue(role, out var nav))
                failures.AddRange(await CheckNavigation(client, label, nav.Landing, nav.Nav));

            var allowed = 0;
            foreach (var endpoint in ApiAccessMatrix.All)
            {
                var expected = ApiAccessMatrix.IsAllowed(endpoint, role);
                var (blocked, detail) = await Probe(client, endpoint);
                checkedCount++;
                if (blocked == expected)
                    failures.Add($"{label}: {endpoint.Method} {endpoint.Url} ({endpoint.Key}) — mong đợi {(expected ? "được phép" : "bị chặn")}, thực tế {detail}");
                if (!blocked) allowed++;
            }
            // Phiên đăng nhập vẫn còn hiệu lực sau khi gọi hết các API (không API nào làm mất phiên hay đăng xuất nhầm).
            if (user is not null)
            {
                using var stillSignedIn = await client.GetAsync("/Account/ChangePassword");
                var stillOk = role == UnknownRole
                    ? IsBlocked(stillSignedIn, "/Account/ChangePassword").Blocked && !LocationOf(stillSignedIn).Contains("/Account/Login")
                    : stillSignedIn.StatusCode == HttpStatusCode.OK;
                if (!stillOk) failures.Add($"{label}: phiên đăng nhập không còn hiệu lực sau khi gọi thử ({(int)stillSignedIn.StatusCode} {LocationOf(stillSignedIn)})");
            }
            Console.WriteLine($"PASS: S1-04 Task 4 — {label}: {ApiAccessMatrix.All.Count} API, được phép {allowed}, bị chặn {ApiAccessMatrix.All.Count - allowed}.");
        }

        // API trả mã lỗi (không chuyển hướng sang trang HTML) khi chưa đăng nhập / sai vai trò.
        using (var anonymous = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = web.Client.BaseAddress })
        using (var api = await anonymous.GetAsync("/api/table-status"))
            if (api.StatusCode != HttpStatusCode.Unauthorized) failures.Add($"API khi chưa đăng nhập phải trả 401, thực tế {(int)api.StatusCode}");
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() };
            using var kitchen = new HttpClient(handler) { BaseAddress = web.Client.BaseAddress };
            await Login(kitchen, "kitchen", password);
            using var update = await kitchen.PostAsync("/api/table-status/A01", new StringContent("""{"status":"Serving"}""", Encoding.UTF8, "application/json"));
            if (update.StatusCode != HttpStatusCode.Forbidden) failures.Add($"Bếp đổi trạng thái bàn qua API phải nhận 403, thực tế {(int)update.StatusCode}");
        }

        failures.AddRange(await CheckWaiter(web.Client.BaseAddress!, password));
        failures.AddRange(await CheckTypedScreens(web.Client.BaseAddress!, password));

        if (failures.Count > 0)
            throw new Exception("FAIL: S1-04 Task 4 — " + failures.Count + " lỗi phân quyền:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
        Console.WriteLine($"PASS: S1-04 Task 4 — {checkedCount} lời gọi ({ApiAccessMatrix.All.Count} API × {principals.Length} trường hợp) đều đúng quyền.");
    }

    /// <summary>
    /// S1-04 Task 1: tài khoản Phục vụ — đăng nhập (gọi bằng fetch) nhận về vai trò; gọi API thực đơn/tài khoản/báo cáo
    /// nhận 403 kèm dữ liệu lỗi JSON thuần; gọi API sơ đồ bàn/đặt bàn/gọi món thành công.
    /// </summary>
    private static async Task<List<string>> CheckWaiter(Uri baseAddress, string password)
    {
        var problems = new List<string>();
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() };
        using var client = new HttpClient(handler) { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(30) };

        // 1. Đăng nhập bằng fetch: máy chủ trả về vai trò đọc từ database.
        var loginPage = await client.GetStringAsync("/Account/Login");
        using (var login = new HttpRequestMessage(HttpMethod.Post, "/Account/Login")
               { Content = Form(("Identifier", "waiter"), ("Password", password), ("__RequestVerificationToken", Token(loginPage))) })
        {
            login.Headers.Add("X-Requested-With", "XMLHttpRequest");
            using var response = await client.SendAsync(login);
            var body = await response.Content.ReadAsStringAsync();
            var ok = response.StatusCode == HttpStatusCode.OK && TryJson(body, out var json)
                && json.TryGetProperty("succeeded", out var succeeded) && succeeded.ValueKind == JsonValueKind.True
                && json.TryGetProperty("user", out var user) && Text(user, "role") == "Waiter" && Text(user, "roleName") == "Phục vụ";
            if (!ok)
                problems.Add($"Phục vụ: đăng nhập phải trả về vai trò Waiter (Phục vụ), thực tế {(int)response.StatusCode} {body}");
        }
        using (var me = await client.GetAsync("/api/account/me"))
        {
            var body = await me.Content.ReadAsStringAsync();
            var ok = me.StatusCode == HttpStatusCode.OK && TryJson(body, out var json) && Text(json, "role") == "Waiter"
                && json.TryGetProperty("navigation", out var nav) && nav.ValueKind == JsonValueKind.Array
                && nav.EnumerateArray().Select(n => Text(n, "key")).SequenceEqual(ExpectedNavigation["Waiter"].Nav);
            if (!ok)
                problems.Add($"Phục vụ: /api/account/me phải trả vai trò Waiter và 3 mục điều hướng, thực tế {(int)me.StatusCode} {body}");
        }

        // 2. API thực đơn / tài khoản / báo cáo: bị chặn, mã 403, dữ liệu lỗi JSON thuần.
        var blocked = new (string Group, string Method, string Url)[]
        {
            ("thực đơn", "GET", "/Dishes"), ("thực đơn", "GET", "/DishCategories"), ("thực đơn", "GET", "/Dishes/Create"),
            ("thực đơn", "POST", "/Management/Availability"),
            ("tài khoản", "GET", "/admin/employee-accounts"), ("tài khoản", "GET", "/admin/employee-accounts/check-username?userName=s104"),
            ("tài khoản", "POST", "/admin/employee-accounts/create"),
            ("báo cáo", "GET", "/AuditLogs"), ("báo cáo", "GET", "/Cashier/Invoices"), ("báo cáo", "GET", "/Cashier/Shift")
        };
        foreach (var (group, method, url) in blocked)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), url);
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            if (method == "POST") request.Content = new FormUrlEncodedContent([]);
            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            var ok = response.StatusCode == HttpStatusCode.Forbidden && response.Content.Headers.ContentType?.MediaType == "application/json"
                && TryJson(body, out var json) && Text(json, "error") == "forbidden" && Text(json, "role") == "Waiter"
                && json.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.Number && status.GetInt32() == 403;
            if (!ok)
                problems.Add($"Phục vụ: API {group} {method} {url} phải bị từ chối 403 kèm lỗi JSON, thực tế {(int)response.StatusCode} {response.Content.Headers.ContentType} {body}");
        }

        // 3. API sơ đồ bàn / đặt bàn / gọi món: thành công.
        foreach (var url in new[] { "/", "/api/table-status", "/Reservations", "/Reservations/Create", "/Ordering" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (url.StartsWith("/api/", StringComparison.Ordinal)) request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            using var response = await client.SendAsync(request);
            if (response.StatusCode != HttpStatusCode.OK)
                problems.Add($"Phục vụ: {url} phải mở được (200), thực tế {(int)response.StatusCode} {LocationOf(response)}");
        }
        if (problems.Count == 0)
            Console.WriteLine($"PASS: S1-04 Task 1 — Phục vụ: đăng nhập nhận vai trò Waiter, {blocked.Length} API thực đơn/tài khoản/báo cáo bị từ chối (403 JSON), sơ đồ bàn/đặt bàn/gọi món mở được.");
        return problems;
    }

    /// <summary>
    /// S1-04 Task 3: mỗi vai trò gõ thẳng đường dẫn từng màn hình. Ngoài quyền → 403 ngay tại đường dẫn đó (không chuyển hướng),
    /// thấy trang “Không có quyền truy cập” có thông điệp, đường dẫn đã mở và nút về màn hình chính của vai trò.
    /// Trong quyền → vào bình thường (200). Vai trò không xác định → trang 403 chỉ có nút đăng xuất.
    /// </summary>
    private static async Task<List<string>> CheckTypedScreens(Uri baseAddress, string password)
    {
        var problems = new List<string>();
        var users = new (string Label, string User, string Role)[]
        {
            ("Quản lý", "manager", "Manager"), ("Phục vụ", "waiter", "Waiter"), ("Bếp", "kitchen", "Kitchen"),
            ("Thu ngân", "cashier", "Cashier"), ("Vai trò không xác định", UnknownUser, UnknownRole)
        };
        foreach (var (label, user, role) in users)
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() };
            using var client = new HttpClient(handler) { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(30) };
            await Login(client, user, password);
            var known = ExpectedNavigation.TryGetValue(role, out var expected);
            var screens = ScreenPaths.Where(s => !PublicScreens.Contains(s.Key))
                .Select(s => (Path: s.Value, Allowed: known && expected.Nav.Contains(s.Key)))
                .Concat(ExtraScreens.Select(e => (Path: e.Path, Allowed: e.Roles.Contains(role))))
                .ToArray();
            int denied = 0, opened = 0;
            foreach (var (path, allowed) in screens)
            {
                using var response = await client.GetAsync(path);
                var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
                if (path == "/" && known && expected.Landing != "/")
                {
                    // Bếp, Thu ngân mở "/" được đưa về trang đầu của mình (S1-04 Task 2), không phải lỗi.
                    if (response.StatusCode != HttpStatusCode.Redirect || LocationOf(response) != expected.Landing)
                        problems.Add($"{label}: gõ \"/\" phải về {expected.Landing}, thực tế {(int)response.StatusCode} {LocationOf(response)}");
                    continue;
                }
                if (allowed)
                {
                    if (response.StatusCode != HttpStatusCode.OK || html.Contains("data-access-denied=\"403\""))
                        problems.Add($"{label}: {path} thuộc quyền nhưng không vào được ({(int)response.StatusCode} {LocationOf(response)})");
                    else opened++;
                    continue;
                }
                var home = known ? $"data-home-link=\"{expected.Landing}\"" : "data-denied-logout";
                var ok = response.StatusCode == HttpStatusCode.Forbidden && response.Headers.Location is null
                    && html.Contains("data-access-denied=\"403\"") && html.Contains("Không có quyền truy cập")
                    && html.Contains("Tài khoản của bạn không có quyền mở màn hình này.")
                    && html.Contains($"data-requested-path>{path}</code>") && html.Contains(home);
                if (!ok) problems.Add($"{label}: gõ thẳng {path} (ngoài quyền) phải nhận 403 và trang báo không có quyền, thực tế {(int)response.StatusCode} {LocationOf(response)}");
                else denied++;
            }
            if (problems.Count == 0)
                Console.WriteLine($"PASS: S1-04 Task 3 — {label}: {denied} màn hình ngoài quyền trả 403 + trang báo không có quyền, {opened} màn hình trong quyền vào bình thường.");
        }
        return problems;
    }

    private static bool TryJson(string body, out JsonElement root)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            root = document.RootElement.Clone();
            return root.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException) { root = default; return false; }
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>Mở "/" → đúng trang đầu của vai trò; thanh điều hướng có đúng các mục của vai trò, không thừa không thiếu.</summary>
    private static async Task<List<string>> CheckNavigation(HttpClient client, string label, string landing, string[] expected)
    {
        var problems = new List<string>();
        using (var root = await client.GetAsync("/"))
        {
            var ok = landing == "/" ? root.StatusCode == HttpStatusCode.OK
                : root.StatusCode == HttpStatusCode.Redirect && LocationOf(root) == landing;
            if (!ok) problems.Add($"{label}: mở \"/\" phải tới {landing}, thực tế {(int)root.StatusCode} {LocationOf(root)}");
        }
        using var page = await client.GetAsync(landing);
        var html = await page.Content.ReadAsStringAsync();
        var shown = System.Text.RegularExpressions.Regex.Matches(html, "data-nav-item=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToArray();
        if (page.StatusCode != HttpStatusCode.OK || !shown.SequenceEqual(expected))
            problems.Add($"{label}: thanh điều hướng phải có [{string.Join(", ", expected)}], thực tế [{string.Join(", ", shown)}] (HTTP {(int)page.StatusCode})");
        else
            Console.WriteLine($"PASS: S1-04 Task 2 — {label} mở \"/\" vào {landing}, thấy đúng {expected.Length} mục: {string.Join(", ", expected)}.");
        return problems;
    }

    private static async Task<(bool Blocked, string Detail)> Probe(HttpClient client, ApiEndpoint endpoint)
    {
        using var request = new HttpRequestMessage(new HttpMethod(endpoint.Method), endpoint.Url);
        if (endpoint.Method != "GET")
            request.Content = endpoint.Url.StartsWith("/api/", StringComparison.Ordinal)
                ? new StringContent("{}", Encoding.UTF8, "application/json")
                : new FormUrlEncodedContent([]);
        // Chỉ đọc phần đầu phản hồi: luồng sự kiện (SSE) không bao giờ kết thúc.
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        return IsBlocked(response, endpoint.Url);
    }

    /// <summary>Bị chặn = 401, 403 (trừ chính trang AccessDenied) hoặc chuyển hướng tới trang đăng nhập / từ chối truy cập.</summary>
    private static (bool Blocked, string Detail) IsBlocked(HttpResponseMessage response, string url)
    {
        var status = (int)response.StatusCode;
        var location = LocationOf(response);
        var blocked = status == 401
            || (status == 403 && !url.StartsWith("/Account/AccessDenied", StringComparison.OrdinalIgnoreCase))
            || (status is 301 or 302 or 303 or 307 or 308
                && (location.Contains("/Account/Login", StringComparison.OrdinalIgnoreCase) || location.Contains("/Account/AccessDenied", StringComparison.OrdinalIgnoreCase)));
        return (blocked, location.Length > 0 ? $"{status} → {location}" : status.ToString());
    }

    private static string LocationOf(HttpResponseMessage response) => response.Headers.Location?.OriginalString ?? "";
}
