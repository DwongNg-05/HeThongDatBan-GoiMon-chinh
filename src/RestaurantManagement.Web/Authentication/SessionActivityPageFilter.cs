using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using RestaurantManagement.Web.Models;

namespace RestaurantManagement.Web.Authentication;

/// <summary>
/// Bản Razor Pages của <see cref="SessionActivityFilter"/>: thao tác ở các trang Razor (Gọi món /Ordering, gửi món
/// /Ordering/Checkout, Món ăn, Nhóm món…) cũng được tính là nhân viên đang làm việc, nên không bị coi là để yên
/// và hết phiên đăng nhập giữa lúc gọi món. (Bộ lọc action của MVC không chạy cho Razor Pages.)
/// </summary>
public sealed class SessionActivityPageFilter(LoginSessionStore sessions) : IAsyncPageFilter
{
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var executed = await next();
        var http = context.HttpContext;
        if (http.Response.HasStarted || http.RequestAborted.IsCancellationRequested)
            return;

        var endpoint = http.GetEndpoint();
        var status = (executed.Result as IStatusCodeActionResult)?.StatusCode ?? http.Response.StatusCode;
        if (executed.Exception is null && !executed.Canceled && status < 400
            && http.User.Identity?.IsAuthenticated == true
            && endpoint?.Metadata.GetMetadata<IAuthorizeData>() is not null
            && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null
            && endpoint.Metadata.GetMetadata<PassiveSessionReadAttribute>() is null)
        {
            // Không làm sống lại một phiên đã hết hạn trong lúc trang đang xử lý.
            if (!await sessions.Check(http.User, touch: true))
            {
                await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                executed.Result = IdleSessionEvents.IsApiRequest(http.Request)
                    ? new UnauthorizedResult()
                    : new RedirectToActionResult("Login", "Account", new { sessionExpired = true });
            }
        }
    }
}
