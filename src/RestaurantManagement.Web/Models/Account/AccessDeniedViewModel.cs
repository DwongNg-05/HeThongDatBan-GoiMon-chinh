using System.Security.Claims;
using RestaurantManagement.Web.Security;

namespace RestaurantManagement.Web.Models;

/// <summary>
/// S1-04 Task 3: nội dung trang “Không có quyền truy cập” (403).
/// Đã chốt với PO: thông điệp ngắn, nêu vai trò đang dùng và đường dẫn đã mở; luôn có nút “Về màn hình chính”
/// (trang đầu của vai trò) và danh sách các màn hình vai trò được dùng; tài khoản không có vai trò hợp lệ chỉ còn nút đăng xuất.
/// </summary>
public sealed record AccessDeniedViewModel(
    bool SignedIn,
    string? UserName,
    string RoleName,
    string? RequestedPath,
    NavLink? Home,
    IReadOnlyList<NavLink> Allowed)
{
    public const string Heading = "Không có quyền truy cập";
    public const string Message = "Tài khoản của bạn không có quyền mở màn hình này.";
    public const string Help = "Nếu bạn cần dùng chức năng này, hãy liên hệ Quản lý để được cấp quyền.";
    public const string HomeLabel = "Về màn hình chính";

    public static AccessDeniedViewModel For(ClaimsPrincipal user, string? requestedPath)
    {
        if (user.Identity?.IsAuthenticated != true)
            return new AccessDeniedViewModel(false, null, AppRoles.DisplayName(null), requestedPath, null, Array.Empty<NavLink>());
        var staff = RoleNavigation.Describe(user);
        var home = staff.Navigation.FirstOrDefault(n => n.Path == staff.LandingPath) ?? staff.Navigation.FirstOrDefault();
        return new AccessDeniedViewModel(true, staff.UserName, staff.RoleName, requestedPath, home, staff.Navigation);
    }
}
