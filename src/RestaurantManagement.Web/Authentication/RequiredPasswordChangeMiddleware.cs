using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using RestaurantManagement.Web.Models;

namespace RestaurantManagement.Web.Authentication;

// Covers both MVC actions and Razor Pages, including direct API requests.
public sealed class RequiredPasswordChangeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, PasswordChangeStore passwords)
    {
        var endpoint = context.GetEndpoint();
        var path = context.Request.Path;
        var allowed = path.Equals("/Account/DoiMatKhau", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/Account/Logout", StringComparison.OrdinalIgnoreCase)
            // Bước xác minh email diễn ra trước bước đổi mật khẩu.
            || EmailVerificationMiddleware.IsAllowed(path);
        if (!allowed && endpoint is not null
            && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null
            && context.User.Identity?.IsAuthenticated == true
            && int.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            && await passwords.IsRequired(userId))
        {
            context.Response.Redirect(context.Request.PathBase + "/Account/DoiMatKhau");
            return;
        }
        await next(context);
    }
}
