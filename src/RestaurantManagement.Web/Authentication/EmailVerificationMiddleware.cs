using Microsoft.AspNetCore.Authorization;
using RestaurantManagement.Web.Services.EmailVerification;

namespace RestaurantManagement.Web.Authentication;

/// <summary>
/// Người dùng (trừ Quản lý) đã đăng nhập nhưng chưa nhập mã email của phiên này chỉ được vào màn hình xác minh
/// (và đăng xuất). Chạy trước bước bắt buộc đổi mật khẩu, nên phải xác minh email rồi mới đổi mật khẩu.
/// </summary>
public sealed class EmailVerificationMiddleware(RequestDelegate next)
{
    public static readonly string[] AllowedPaths =
        ["/Account/VerifyEmail", "/Account/ResendVerificationCode", "/Account/SetVerificationEmail", "/Account/Logout"];

    public static bool IsAllowed(PathString path) =>
        AllowedPaths.Any(p => path.Equals(p, StringComparison.OrdinalIgnoreCase));

    public async Task InvokeAsync(HttpContext context, EmailVerificationService verification)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint is not null
            && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null
            && !IsAllowed(context.Request.Path)
            && verification.IsRequired(context.User))
        {
            var target = context.Request.PathBase + "/Account/VerifyEmail";
            if (HttpMethods.IsGet(context.Request.Method))
                target += "?returnUrl=" + Uri.EscapeDataString(context.Request.PathBase + context.Request.Path + context.Request.QueryString);
            context.Response.Redirect(target);
            return;
        }
        await next(context);
    }
}
