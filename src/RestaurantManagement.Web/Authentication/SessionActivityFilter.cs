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
