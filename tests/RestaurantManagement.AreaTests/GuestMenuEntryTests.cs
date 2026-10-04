using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using RestaurantManagement.Web.Services;

/// <summary>S2-01 Task 1: khách mở "/" tới thẳng thực đơn; nhân viên không bị ảnh hưởng.</summary>
static class GuestMenuEntryTests
{
    public static void Run(Action<bool, string> check)
    {
        static HttpContext Context(string path, string method = "GET", bool signedIn = false, bool authCookie = false)
        {
            var context = new DefaultHttpContext();
            context.Request.Method = method;
            context.Request.Path = path;
            if (authCookie) context.Request.Headers.Cookie = GuestMenuEntry.AuthCookieName + "=old";
            if (signedIn) context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "manager")], "Cookies"));
            return context;
        }

        check(GuestMenuEntry.ShouldSendToMenu(Context("/")), "Guest menu: guest opening / goes to the public menu");
        check(GuestMenuEntry.ShouldSendToMenu(Context("/", "HEAD")), "Guest menu: HEAD / also goes to the menu");
        check(!GuestMenuEntry.ShouldSendToMenu(Context("/", signedIn: true)), "Guest menu: signed-in staff still get the table map at /");
        check(!GuestMenuEntry.ShouldSendToMenu(Context("/", authCookie: true)), "Guest menu: staff with an expired login cookie still go to login");
        check(!GuestMenuEntry.ShouldSendToMenu(Context("/", "POST")), "Guest menu: POST / is not redirected");
        check(!GuestMenuEntry.ShouldSendToMenu(Context("/Areas")) && !GuestMenuEntry.ShouldSendToMenu(Context("/Home/Index")),
            "Guest menu: staff pages still require login");

        var redirected = Context("/");
        GuestMenuEntry.Invoke(redirected, _ => Task.CompletedTask).GetAwaiter().GetResult();
        check(redirected.Response.StatusCode == StatusCodes.Status302Found && redirected.Response.Headers.Location == "/Menu",
            "Guest menu: redirect target is /Menu");
    }
}
