using Microsoft.AspNetCore.Diagnostics;

namespace RestaurantManagement.Web.Authentication;

/// <summary>
/// S1-04 Task 3: gõ thẳng đường dẫn màn hình ngoài quyền của vai trò đang đăng nhập → máy chủ trả 403 ngay tại đường dẫn đó,
/// kèm trang “Không có quyền truy cập” (thông điệp + nút về màn hình chính), thay vì chuyển hướng sang trang chữ thuần.
/// Cơ chế: UseStatusCodePagesWithReExecute("/Account/AccessDenied") nhưng chỉ bật cho đúng các yêu cầu bị từ chối quyền;
/// mọi mã lỗi khác (404, 400 do thiếu mã chống giả mạo…) giữ nguyên như trước.
/// </summary>
public static class AccessDeniedPage
{
    public const string Path = "/Account/AccessDenied";

    /// <summary>Đặt ngay sau UseStatusCodePagesWithReExecute: mặc định tắt trang mã lỗi cho mọi yêu cầu.</summary>
    public static Task DisableByDefault(HttpContext context, RequestDelegate next)
    {
        if (context.Features.Get<IStatusCodePagesFeature>() is { } feature) feature.Enabled = false;
        return next(context);
    }

    /// <summary>
    /// Trả 403 và bật trang báo không có quyền cho yêu cầu hiện tại.
    /// Trả false khi không có trang mã lỗi (ví dụ máy chủ kiểm thử không cấu hình) để nơi gọi dùng cách cũ (chuyển hướng).
    /// </summary>
    public static bool TryShow(HttpContext context)
    {
        if (context.Response.HasStarted || context.Features.Get<IStatusCodePagesFeature>() is not { } feature) return false;
        feature.Enabled = true;
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return true;
    }
}
