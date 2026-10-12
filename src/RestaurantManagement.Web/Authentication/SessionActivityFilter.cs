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
        // The response already went out (for example the table map's live stream api/table-status/stream, which ends when
        // the user logs out or leaves the page): headers are read-only, so we can neither sign out nor redirect. Leave it;
        // the next request is checked again by the cookie events (IdleSessionEvents), which sign out an ended session.
        if (context.HttpContext.Response.HasStarted || context.HttpContext.RequestAborted.IsCancellationRequested)
            return;
        var endpoint = context.HttpContext.GetEndpoint();
        var status = (executed.Result as IStatusCodeActionResult)?.StatusCode ?? context.HttpContext.Response.StatusCode;
        if (executed.Exception is null && !executed.Canceled && context.ModelState.IsValid && status < 400
            && context.HttpContext.User.Identity?.IsAuthenticated == true
            && endpoint?.Metadata.GetMetadata<IAuthorizeData>() is not null
            && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null)
        {
            if (endpoint.Metadata.GetMetadata<PassiveSessionReadAttribute>() is not null)
                return;
            // Never revive a session that expired while an action was running.
            if (!await sessions.Check(context.HttpContext.User, touch: true))
            {
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                executed.Result = new RedirectToActionResult("Login", "Account", new { sessionExpired = true });
            }
        }
    }
}
