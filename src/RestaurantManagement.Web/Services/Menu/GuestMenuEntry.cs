using Microsoft.AspNetCore.Http;

namespace RestaurantManagement.Web.Services;

/// <summary>
/// S2-01 Task 1: lối vào thực đơn cho khách hàng.
/// Khách chưa đăng nhập mở địa chỉ gốc của quán ("/") được chuyển thẳng tới thực đơn công khai /Menu,
/// thay vì bị chặn ở màn hình đăng nhập nhân viên.
/// <list type="bullet">
/// <item>Nhân viên đã đăng nhập: "/" vẫn là sơ đồ bàn, kèm các bước xác minh email / đổi mật khẩu như cũ.</item>
/// <item>Nhân viên có cookie đăng nhập nhưng phiên đã hết hạn: vẫn được đưa về màn hình đăng nhập như trước.</item>
/// </list>
/// Không dùng [AllowAnonymous] cho trang chủ vì middleware xác minh email / đổi mật khẩu bỏ qua trang cho phép khách.
/// </summary>
public static class GuestMenuEntry
{
    public const string AuthCookieName = "RestaurantManagement.Auth";
    public const string MenuPath = "/Menu";

    public static bool ShouldSendToMenu(HttpContext context) =>
        (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
        && (context.Request.Path == "/" || !context.Request.Path.HasValue)
        && context.User.Identity?.IsAuthenticated != true
        && !context.Request.Cookies.ContainsKey(AuthCookieName);

    /// <summary>
    /// S1-04 Task 2: nhân viên đã đăng nhập mở "/" được đưa tới màn hình đầu tiên thuộc phần việc của mình
    /// (Bếp → màn hình bếp, Thu ngân → thanh toán); Quản lý và Phục vụ ở lại sơ đồ bàn. Trả về null khi không cần chuyển.
    /// </summary>
    public static string? StaffLanding(HttpContext context)
    {
        if (!(HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
            || !(context.Request.Path == "/" || !context.Request.Path.HasValue)
            || context.User.Identity?.IsAuthenticated != true) return null;
        var landing = RestaurantManagement.Web.Security.RoleNavigation.LandingPath(context.User);
        return landing == "/" ? null : landing;
    }

    public static async Task Invoke(HttpContext context, RequestDelegate next)
    {
        if (ShouldSendToMenu(context))
        {
            context.Response.Redirect(context.Request.PathBase + MenuPath);
            return;
        }
        if (StaffLanding(context) is string landing)
        {
            context.Response.Redirect(context.Request.PathBase + landing);
            return;
        }
        await next(context);
    }
}
