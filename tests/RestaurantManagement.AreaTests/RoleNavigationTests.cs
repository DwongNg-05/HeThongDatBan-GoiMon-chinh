using System.Security.Claims;
using RestaurantManagement.DbTool;
using RestaurantManagement.Web.Authentication;
using RestaurantManagement.Web.Models;
using RestaurantManagement.Web.Security;

/// <summary>
/// S1-04 Task 1–4 (không cần database): bảng ánh xạ vai trò → màn hình, phạm vi API của từng vai trò,
/// thông tin vai trò trả về khi đăng nhập, lỗi quyền dạng JSON (Task 1) và trang báo không có quyền (Task 3).
/// Phục vụ thấy đúng 3 mục (sơ đồ bàn, đặt bàn, gọi món); Bếp thấy đúng 2 mục, Thu ngân đúng 3 mục; mỗi mục vai trò thấy đều nằm trong quyền máy chủ cho phép (ApiAccessMatrix);
/// Bếp và Thu ngân bị chặn ở máy chủ với các màn hình/API của vai trò khác.
/// </summary>
internal static class RoleNavigationTests
{
    internal static void Run(Action<bool, string> check)
    {
        static ClaimsPrincipal User(string? role)
        {
            if (role is null) return new ClaimsPrincipal(new ClaimsIdentity());
            return new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "staff"), new Claim("FullName", "Nhân viên thử"), new Claim(ClaimTypes.Role, role)], "Cookies"));
        }
        string[] Keys(string? role) => RoleNavigation.For(User(role)).Select(i => i.Key).ToArray();

        check(Keys("Kitchen").SequenceEqual(new[] { "kitchen-orders", "daily-dishes" }), "Navigation: kitchen sees exactly 2 items (kitchen screen; today's dishes with the one-tap \"Tạm hết\" switch, S2-08 Task 1)");
        check(Keys("Cashier").SequenceEqual(new[] { "cashier-payments", "cashier-invoices", "cashier-shift" }), "Navigation: cashier sees exactly 3 items (payments, invoices, shift close)");
        check(RoleNavigation.For(User("Kitchen")).Select(i => i.Title).SequenceEqual(new[] { "Màn hình bếp", "Món trong ngày" })
            && RoleNavigation.For(User("Cashier")).Select(i => i.Title).SequenceEqual(new[] { "Thanh toán", "Hoá đơn", "Chốt ca" }),
            "Navigation: kitchen and cashier items have Vietnamese titles");
        // S1-04 Task 1: Phục vụ thấy đúng 3 mục — đặt bàn, sơ đồ bàn, gọi món.
        check(Keys("Waiter").SequenceEqual(new[] { "reservations", "table-map", "ordering" })
            && RoleNavigation.For(User("Waiter")).Select(i => i.Title).SequenceEqual(new[] { "Danh sách đặt bàn", "Sơ đồ bàn", "Gọi món" }),
            "Navigation: waiter sees exactly 3 items (reservations, table map, ordering)");
        check(Keys("Manager").Length == RoleNavigation.Items.Count, "Navigation: manager sees every item");
        check(Keys(null).Length == 0 && Keys("Guest").Length == 0, "Navigation: anonymous and unknown roles see nothing");
        foreach (var (role, expected) in ApiAuthorizationVerification.ExpectedNavigation)
            check(Keys(role).SequenceEqual(expected.Nav) && RoleNavigation.LandingPath(User(role)) == expected.Landing,
                $"Navigation: {role} items and landing page match the HTTP verification");

        // Mỗi mục trên thanh điều hướng của một vai trò đều được máy chủ cho phép với vai trò đó (không hiện mục sẽ bị chặn).
        var getByPath = ApiAccessMatrix.All.Where(e => e.Method == "GET").GroupBy(e => e.Url.Split('?')[0]).ToDictionary(g => g.Key, g => g.First());
        var problems = new List<string>();
        foreach (var role in ApiAccessMatrix.Roles)
            foreach (var item in RoleNavigation.For(User(role)))
            {
                var path = item.Path == "/" ? "/Home/Index" : item.Path;
                if (!getByPath.TryGetValue(path, out var endpoint)) problems.Add($"{item.Key}: no API {path}");
                else if (!ApiAccessMatrix.IsAllowed(endpoint, role)) problems.Add($"{role} sees {item.Key} but the server blocks {path}");
            }
        check(problems.Count == 0, "Navigation: every item shown to a role is allowed for that role by the server" + (problems.Count > 0 ? ": " + string.Join("; ", problems) : ""));

        // Ngoài phạm vi: Bếp và Thu ngân bị chặn ở máy chủ với màn hình/API của vai trò khác.
        string[] Allowed(string role) => ApiAccessMatrix.All
            .Where(e => e.Allowed is not (ApiAccessMatrix.Anonymous or ApiAccessMatrix.SignedIn) && ApiAccessMatrix.IsAllowed(e, role))
            .Select(e => e.Key).ToArray();
        static bool Own(string key, params string[] prefixes) => prefixes.Any(p => key.StartsWith(p, StringComparison.Ordinal));
        var kitchenExtra = Allowed("Kitchen").Where(k => !Own(k, "Kitchen.", "Account.")).ToArray();
        var cashierExtra = Allowed("Cashier").Where(k => !Own(k, "Cashier.", "Account.")).ToArray();
        check(kitchenExtra.Length == 0, "Server scope: kitchen can only call kitchen APIs and its own account" + (kitchenExtra.Length > 0 ? ": " + string.Join(", ", kitchenExtra) : ""));
        check(cashierExtra.Length == 0, "Server scope: cashier can only call cashier APIs and its own account" + (cashierExtra.Length > 0 ? ": " + string.Join(", ", cashierExtra) : ""));
        check(!ApiAccessMatrix.IsAllowed(ApiAccessMatrix.All.Single(e => e.Key == "Kitchen.Advance POST"), "Manager")
            && !ApiAccessMatrix.IsAllowed(ApiAccessMatrix.All.Single(e => e.Key == "Cashier.Checkout POST"), "Kitchen")
            && !ApiAccessMatrix.IsAllowed(ApiAccessMatrix.All.Single(e => e.Key == "Management.Availability POST"), "Kitchen")
            && !ApiAccessMatrix.IsAllowed(ApiAccessMatrix.All.Single(e => e.Key == "Management.Availability POST"), "Cashier")
            && ApiAccessMatrix.All.All(e => !e.Url.StartsWith("/Kitchen/SoldOut", StringComparison.Ordinal))
            // S2-08 Task 1: bật/tắt "Tạm hết" — Bếp và Quản lý; Phục vụ, Thu ngân bị chặn.
            && ApiAccessMatrix.IsAllowed(ApiAccessMatrix.All.Single(e => e.Key == "Kitchen.TemporarilyOut POST"), "Kitchen")
            && ApiAccessMatrix.IsAllowed(ApiAccessMatrix.All.Single(e => e.Key == "Kitchen.TemporarilyOut POST"), "Manager")
            && !ApiAccessMatrix.IsAllowed(ApiAccessMatrix.All.Single(e => e.Key == "Kitchen.TemporarilyOut POST"), "Waiter")
            && !ApiAccessMatrix.IsAllowed(ApiAccessMatrix.All.Single(e => e.Key == "Kitchen.TemporarilyOut POST"), "Cashier")
            && !ApiAccessMatrix.IsAllowed(ApiAccessMatrix.All.Single(e => e.Key == "TableStatus.Snapshot GET"), "Kitchen")
            && !ApiAccessMatrix.IsAllowed(ApiAccessMatrix.All.Single(e => e.Key == "Page /Dishes/Index"), "Cashier")
            && !ApiAccessMatrix.IsAllowed(ApiAccessMatrix.All.Single(e => e.Key == "Reservations.Index GET"), "Kitchen")
            && !ApiAccessMatrix.IsAllowed(ApiAccessMatrix.All.Single(e => e.Key == "Reservations.Details GET"), "Cashier"),
            "Server scope: cooking status is Kitchen only; payments are not for Kitchen; sold-out-today only in dish management (Manager); \"Tạm hết\" toggle for Kitchen and Manager; table map and dish management are not for Kitchen/Cashier");

        // S1-04 Task 1: Phục vụ chỉ gọi được API sơ đồ bàn, đặt bàn, gọi món (và tài khoản của chính mình).
        var waiterExtra = Allowed("Waiter").Where(k => !Own(k, "Account.", "Home.Index", "TableDetails.", "TableStatus.", "TableMap.", "Reservations.", "ReservationConfirmations.", "ReservationManagement.", "Page /Ordering/", "Page /Orders/", "SentOrders.")).ToArray();
        check(waiterExtra.Length == 0, "Server scope: waiter can only call table map, reservation and ordering APIs and its own account" + (waiterExtra.Length > 0 ? ": " + string.Join(", ", waiterExtra) : ""));
        ApiEndpoint Api(string key) => ApiAccessMatrix.All.Single(e => e.Key == key);
        var waiterBlocked = new[]
        {
            // Thực đơn (quản lý món, nhóm món, sửa giá, tạm hết)
            "Page /Dishes/Index", "Page /Dishes/Create", "Page /Dishes/Edit", "Page /Dishes/PriceHistory", "Page /DishCategories/Index",
            "Page /DishCategories/Create", "Management.Availability POST",
            // Tài khoản nhân viên
            "EmployeeAccounts.Index GET", "EmployeeAccounts.Create POST", "EmployeeAccounts.CheckUserName GET", "EmployeeAccounts.Deactivate POST",
            // Báo cáo (nhật ký, hoá đơn, chốt ca)
            "AuditLogs.Index GET", "Cashier.Invoices GET", "Cashier.Shift GET"
        }.Where(k => ApiAccessMatrix.IsAllowed(Api(k), "Waiter")).ToArray();
        check(waiterBlocked.Length == 0, "Server scope: menu, account and report APIs are blocked for the waiter" + (waiterBlocked.Length > 0 ? ": " + string.Join(", ", waiterBlocked) : ""));
        var waiterAllowed = new[]
        {
            "Home.Index ANY", "TableStatus.Snapshot GET", "TableStatus.Update POST", "TableDetails.Get GET",
            "Reservations.Index GET", "Reservations.Create POST", "Reservations.Details GET",
            "Page /Ordering/Index", "Page /Ordering/Checkout", "Page /Orders/Details", "Account.Me GET"
        }.Where(k => !ApiAccessMatrix.IsAllowed(Api(k), "Waiter")).ToArray();
        check(waiterAllowed.Length == 0, "Server scope: table map, reservation and ordering APIs are open to the waiter" + (waiterAllowed.Length > 0 ? ": " + string.Join(", ", waiterAllowed) : ""));

        // S1-04 Task 1: đăng nhập thành công trả về vai trò; mã lỗi quyền là dữ liệu JSON thuần.
        var waiter = RoleNavigation.Describe(User("Waiter"));
        check(waiter.Role == "Waiter" && waiter.RoleName == "Phục vụ" && waiter.LandingPath == "/" && waiter.FullName == "Nhân viên thử"
            && waiter.Navigation.Select(n => n.Key).SequenceEqual(new[] { "reservations", "table-map", "ordering" }),
            "Login info: waiter gets role Waiter (Phục vụ), landing page and its 3 screens");
        check(AppRoles.DisplayName("Manager") == "Quản lý" && AppRoles.DisplayName("Kitchen") == "Bếp" && AppRoles.DisplayName("Cashier") == "Thu ngân"
            && AppRoles.DisplayName("Guest") == "Không xác định", "Login info: every role has a Vietnamese name");
        var forbidden = IdleSessionEvents.Serialize(IdleSessionEvents.ErrorFor(403, User("Waiter"), "/Dishes"));
        var unauthorized = IdleSessionEvents.Serialize(IdleSessionEvents.ErrorFor(401, User(null), "/api/table-status"));
        check(forbidden == "{\"status\":403,\"error\":\"forbidden\",\"message\":\"" + IdleSessionEvents.ForbiddenMessage + "\",\"role\":\"Waiter\",\"path\":\"/Dishes\"}"
            && unauthorized.Contains("\"status\":401") && unauthorized.Contains("\"error\":\"unauthorized\"") && unauthorized.Contains("\"role\":null"),
            "Permission error: blocked API returns plain JSON (403 forbidden with role, 401 unauthorized)");

        // S1-04 Task 3: trang báo không có quyền — nút về màn hình chính đúng trang đầu của từng vai trò.
        check(ApiAuthorizationVerification.ScreenPaths.OrderBy(p => p.Key).SequenceEqual(RoleNavigation.Items.Select(i => KeyValuePair.Create(i.Key, i.Path)).OrderBy(p => p.Key)),
            "Access denied: the HTTP check types every screen on the navigation bar");
        var homes = new[] { ("Manager", "/", "Sơ đồ bàn"), ("Waiter", "/", "Sơ đồ bàn"), ("Kitchen", "/Kitchen", "Màn hình bếp"), ("Cashier", "/Cashier", "Thanh toán") };
        foreach (var (role, path, title) in homes)
        {
            var page = AccessDeniedViewModel.For(User(role), "/AuditLogs");
            check(page.SignedIn && page.Home is { } back && back.Path == path && back.Title == title && page.RequestedPath == "/AuditLogs"
                && page.RoleName == AppRoles.DisplayName(role) && page.Allowed.Select(a => a.Key).SequenceEqual(Keys(role)),
                $"Access denied: {role} gets a way back to {path} and the list of its own screens");
        }
        var unknown = AccessDeniedViewModel.For(User("Guest"), "/Reservations");
        var anonymous = AccessDeniedViewModel.For(User(null), null);
        check(unknown.SignedIn && unknown.Home is null && unknown.Allowed.Count == 0 && !anonymous.SignedIn && anonymous.Home is null,
            "Access denied: unknown role only gets sign-out, anonymous gets sign-in");
        check(AccessDeniedViewModel.Message.Length > 0 && AccessDeniedViewModel.HomeLabel == "Về màn hình chính",
            "Access denied: message and home button text agreed with the PO");

        check(RoleNavigation.ActiveKey(RoleNavigation.Items, "/Kitchen") == "kitchen-orders"
            && RoleNavigation.ActiveKey(RoleNavigation.Items, "/Kitchen/Dishes") == "daily-dishes"
            && RoleNavigation.ActiveKey(RoleNavigation.Items, "/Cashier/Shift") == "cashier-shift"
            && RoleNavigation.ActiveKey(RoleNavigation.Items, "/") == "table-map",
            "Navigation: the current screen is highlighted");
    }
}
