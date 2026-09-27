using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using RestaurantManagement.Web.Models;

namespace RestaurantManagement.Web.Authentication;

public sealed class SessionActivityFilter(LoginSessionStore sessions) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var executed = await next();

        // A streaming response (for example the real-time table-status endpoint)
        // may already have sent its headers by the time the action completes.
        // Signing out at that point would try to modify the auth cookie and throws
        // "Headers are read-only, response has already started".
        if (context.HttpContext.Response.HasStarted)
        {
            return;
        }

        var endpoint = context.HttpContext.GetEndpoint();
        var status = (executed.Result as IStatusCodeActionResult)?.StatusCode ?? context.HttpContext.Response.StatusCode;
        if (executed.Exception is null && !executed.Canceled && context.ModelState.IsValid && status < 400
            && context.HttpContext.User.Identity?.IsAuthenticated == true
            && endpoint?.Metadata.GetMetadata<IAuthorizeData>() is not null
            && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null)
        {
            // Never revive a session that expired while an action was running.
            if (!await sessions.Check(context.HttpContext.User, touch: true))
            {
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                executed.Result = new RedirectToActionResult("Login", "Account", new { sessionExpired = true });
            }
        }
    }
}
