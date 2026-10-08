using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace RestaurantManagement.Web.Security;

/// <summary>
/// S1-04 Task 4: vai trò và chính sách phân quyền dùng chung cho mọi controller, Razor Page và API.
/// Mã vai trò khớp dbo.Roles.Code (005_ReferenceData.sql); nhóm vai trò khớp quyền trong dbo.RolePermissions
/// (005_ReferenceData.sql, thu hẹp theo S1-04 Task 1/2 ở 031_RolePermissionsByScope.sql).
/// Phạm vi từng vai trò: docs/S1-04-TongHop.md; bảng phân quyền của từng API: docs/S1-04-Task4.md.
/// </summary>
public static class AppRoles
{
    public const string Manager = "Manager";
    public const string Waiter = "Waiter";
    public const string Kitchen = "Kitchen";
    public const string Cashier = "Cashier";

    /// <summary>Mọi vai trò nhân viên hợp lệ: chỉ dùng cho tài khoản của chính mình (đổi mật khẩu, xác minh email, /api/account/me).</summary>
    public const string AllStaff = Manager + "," + Waiter + "," + Kitchen + "," + Cashier;

    /// <summary>
    /// Sơ đồ bàn, đặt bàn (xem và tạo), gọi món (Reservations.Read/Manage, Sessions.Manage, Orders.Manage).
    /// S1-04 Task 1: đây cũng là toàn bộ phạm vi của Phục vụ.
    /// </summary>
    public const string FrontOfHouse = Manager + "," + Waiter;

    /// <summary>
    /// S1-04 Task 2: xem màn hình bếp và danh sách món trong ngày.
    /// S1-04 Task 1: Phục vụ không còn xem màn hình bếp (ngoài phạm vi sơ đồ bàn, đặt bàn, gọi món) → Quản lý, Bếp.
    /// </summary>
    public const string KitchenReaders = Manager + "," + Kitchen;

    /// <summary>Chuyển trạng thái chế biến (Kitchen.Manage): chỉ Bếp — Quản lý chỉ xem, đúng như 005_ReferenceData.sql.</summary>
    public const string KitchenWorkers = Kitchen;

    /// <summary>Thanh toán, hoá đơn, mở/chốt ca (Payments.Manage: Quản lý, Thu ngân).</summary>
    public const string Cashiers = Manager + "," + Cashier;

    public static readonly IReadOnlyList<string> All = [Manager, Waiter, Kitchen, Cashier];

    /// <summary>Chỉ cần đã đăng nhập, kể cả vai trò không xác định (dùng cho Đăng xuất).</summary>
    public const string SignedInPolicy = "SignedIn";

    /// <summary>Tên vai trò tiếng Việt (hiển thị và trả về khi đăng nhập thành công).</summary>
    public static string DisplayName(string? role) => role switch
    {
        Manager => "Quản lý",
        Waiter => "Phục vụ",
        Kitchen => "Bếp",
        Cashier => "Thu ngân",
        _ => "Không xác định"
    };

    public static bool IsKnown(string? role) => role is not null && All.Contains(role, StringComparer.Ordinal);

    /// <summary>
    /// Mặc định ([Authorize] không ghi vai trò) và dự phòng (endpoint không gắn gì): phải đăng nhập VÀ có một vai trò hợp lệ.
    /// Tài khoản có vai trò không xác định (hoặc không có vai trò) bị chặn ở mọi API, chỉ còn đăng xuất được.
    /// </summary>
    public static void Configure(AuthorizationOptions options)
    {
        var knownStaff = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireRole(All).Build();
        options.DefaultPolicy = knownStaff;
        options.FallbackPolicy = knownStaff;
        options.AddPolicy(SignedInPolicy, policy => policy.RequireAuthenticatedUser());
    }

    /// <summary>Id người dùng đang đăng nhập (dùng làm ActorUserId cho thủ tục SQL); 0 khi không xác định.</summary>
    public static int ActorUserId(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id > 0 ? id : 0;
}
