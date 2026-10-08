using System.Security.Claims;

namespace RestaurantManagement.Web.Security;

/// <summary>Một mục trên thanh điều hướng: khoá (data-nav-item), tên, đường dẫn và các vai trò được thấy.</summary>
public sealed record NavItem(string Key, string Title, string Path, params string[] Roles);

/// <summary>Một mục điều hướng trả về cho trình duyệt (không kèm danh sách vai trò).</summary>
public sealed record NavLink(string Key, string Title, string Path);

/// <summary>
/// S1-04 Task 1: thông tin vai trò của tài khoản đang đăng nhập — trả về khi đăng nhập thành công
/// (POST /Account/Login gọi bằng fetch) và ở GET /api/account/me.
/// </summary>
public sealed record SignedInStaff(string UserName, string FullName, string Role, string RoleName, string LandingPath, IReadOnlyList<NavLink> Navigation);

/// <summary>
/// S1-04 Task 1/2: bảng ánh xạ vai trò → màn hình được thấy trên thanh điều hướng (một nguồn duy nhất cho _Layout và kiểm thử).
/// Phục vụ chỉ thấy 3 mục (sơ đồ bàn, đặt bàn, gọi món); Bếp thấy 2 mục (màn hình bếp; món trong ngày — S2-08 Task 1 bật/tắt tạm hết); Thu ngân chỉ thấy 3 mục (thanh toán, hoá đơn, chốt ca).
/// Thanh điều hướng chỉ là phần hiển thị: máy chủ vẫn chặn từng API theo vai trò ([Authorize], xem AppRoles và docs/S1-04-Task4.md).
/// </summary>
public static class RoleNavigation
{
    private const string M = AppRoles.Manager, W = AppRoles.Waiter, K = AppRoles.Kitchen, C = AppRoles.Cashier;

    public static readonly IReadOnlyList<NavItem> Items =
    [
        new("reservations", "Danh sách đặt bàn", "/ReservationManagement", M, W),
        new("areas", "Khu vực & bàn", "/Areas", M),
        new("table-map", "Sơ đồ bàn", "/", M, W),
        new("opening-hours", "Giờ hoạt động", "/OpeningHours", M),
        new("privacy", "Thông tin", "/Home/Privacy", M),
        new("dish-categories", "Quản lý nhóm món", "/DishCategories", M),
        new("dishes", "Quản lý món", "/Dishes", M),
        new("menu", "Thực đơn", "/Menu", M),
        new("ordering", "Gọi món", "/Ordering", M, W),
        new("kitchen-orders", "Màn hình bếp", "/Kitchen", M, K),
        new("daily-dishes", "Món trong ngày", "/Kitchen/Dishes", M, K),
        new("cashier-payments", "Thanh toán", "/Cashier", M, C),
        new("cashier-invoices", "Hoá đơn", "/Cashier/Invoices", M, C),
        new("cashier-shift", "Chốt ca", "/Cashier/Shift", M, C),
        new("employee-accounts", "Quản lý tài khoản", "/admin/employee-accounts", M),
        new("audit-logs", "Nhật ký hệ thống", "/AuditLogs", M)
    ];

    /// <summary>Các mục người dùng được thấy, theo vai trò trong phiên đăng nhập (claim Role do máy chủ cấp khi đăng nhập).</summary>
    public static IReadOnlyList<NavItem> For(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true ? Items.Where(i => i.Roles.Any(user.IsInRole)).ToArray() : Array.Empty<NavItem>();

    /// <summary>
    /// Trang đầu tiên sau khi đăng nhập / khi mở "/": Quản lý, Phục vụ ở sơ đồ bàn; Bếp ở màn hình bếp; Thu ngân ở thanh toán.
    /// </summary>
    public static string LandingPath(ClaimsPrincipal user)
    {
        if (user.IsInRole(AppRoles.Manager) || user.IsInRole(AppRoles.Waiter)) return "/";
        return For(user).FirstOrDefault()?.Path ?? "/";
    }

    /// <summary>S1-04 Task 1: vai trò, tên vai trò, trang đầu và các mục điều hướng của người đang đăng nhập.</summary>
    public static SignedInStaff Describe(ClaimsPrincipal user)
    {
        var role = user.FindFirstValue(ClaimTypes.Role) ?? "";
        return new SignedInStaff(
            user.Identity?.Name ?? "",
            user.FindFirstValue("FullName") ?? "",
            role,
            AppRoles.DisplayName(role),
            LandingPath(user),
            For(user).Select(i => new NavLink(i.Key, i.Title, i.Path)).ToArray());
    }

    /// <summary>Mục đang mở: đường dẫn trùng khớp dài nhất ("/" ứng với cả /Home/Index).</summary>
    public static string? ActiveKey(IEnumerable<NavItem> items, string requestPath)
    {
        var path = string.IsNullOrEmpty(requestPath) ? "/" : requestPath;
        return items
            .Where(i => i.Path == "/"
                ? path == "/" || path.Equals("/Home/Index", StringComparison.OrdinalIgnoreCase) || path.Equals("/Home", StringComparison.OrdinalIgnoreCase)
                : path.Equals(i.Path, StringComparison.OrdinalIgnoreCase) || path.StartsWith(i.Path + "/", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(i => i.Path.Length)
            .FirstOrDefault()?.Key;
    }
}
