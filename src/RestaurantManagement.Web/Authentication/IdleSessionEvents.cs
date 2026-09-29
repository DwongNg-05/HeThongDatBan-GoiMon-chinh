using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using RestaurantManagement.Web.Models;

namespace RestaurantManagement.Web.Authentication;

public sealed class IdleSessionEvents(LoginSessionStore sessions, ITempDataDictionaryFactory tempDataFactory) : CookieAuthenticationEvents
{
    public const string ExpiredItem = "LoginSessionExpired";

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        if (context.Principal is not null && await sessions.Check(context.Principal)) return;
        context.RejectPrincipal();
        context.HttpContext.Items[ExpiredItem] = true;
        // Preserve the notice if an asset or public page discovers expiration first.
        var notice = tempDataFactory.GetTempData(context.HttpContext);
        notice[ExpiredItem] = true;
        notice.Save();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        var url = context.RedirectUri;
        if (context.HttpContext.Items.ContainsKey(ExpiredItem))
            url += (url.Contains('?') ? "&" : "?") + "sessionExpired=true";
        context.Response.Redirect(url);
        return Task.CompletedTask;
    }
}
