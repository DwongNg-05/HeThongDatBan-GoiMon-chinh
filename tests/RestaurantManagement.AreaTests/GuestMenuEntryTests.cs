using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using RestaurantManagement.Web.Services;

/// <summary>S2-01 Task 1: khách mở "/" tới thẳng thực đơn; nhân viên không bị ảnh hưởng.</summary>
static class GuestMenuEntryTests
{
    public static void Run(Action<bool, string> check)
    {
        static HttpContext Context(string path, string method = "GET", bool signedIn = false, bool authCookie = false, string? role = null)
        {
            var context = new DefaultHttpContext();
            context.Request.Method = method;
            context.Request.Path = path;
            if (authCookie) context.Request.Headers.Cookie = GuestMenuEntry.AuthCookieName + "=old";
            if (signedIn)
            {
                var claims = new List<Claim> { new(ClaimTypes.Name, "staff") };
                if (role is not null) claims.Add(new Claim(ClaimTypes.Role, role));
                context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookies"));
            }
            return context;
        }

        check(GuestMenuEntry.ShouldSendToMenu(Context("/")), "Guest menu: guest opening / goes to the public menu");
        check(GuestMenuEntry.ShouldSendToMenu(Context("/", "HEAD")), "Guest menu: HEAD / also goes to the menu");
        check(!GuestMenuEntry.ShouldSendToMenu(Context("/", signedIn: true)), "Guest menu: signed-in staff still get the table map at /");
        check(!GuestMenuEntry.ShouldSendToMenu(Context("/", authCookie: true)), "Guest menu: staff with an expired login cookie still go to login");
        check(!GuestMenuEntry.ShouldSendToMenu(Context("/", "POST")), "Guest menu: POST / is not redirected");
        check(!GuestMenuEntry.ShouldSendToMenu(Context("/Areas")) && !GuestMenuEntry.ShouldSendToMenu(Context("/Home/Index")),
            "Guest menu: staff pages still require login");

        // S1-04 Task 2: nhân viên mở "/" vào thẳng phần việc của mình.
        check(GuestMenuEntry.StaffLanding(Context("/", signedIn: true, role: "Kitchen")) == "/Kitchen"
            && GuestMenuEntry.StaffLanding(Context("/", signedIn: true, role: "Cashier")) == "/Cashier"
            && GuestMenuEntry.StaffLanding(Context("/", signedIn: true, role: "Waiter")) is null
            && GuestMenuEntry.StaffLanding(Context("/", signedIn: true, role: "Manager")) is null
            && GuestMenuEntry.StaffLanding(Context("/Reservations", signedIn: true, role: "Kitchen")) is null,
            "Staff landing: kitchen opens the kitchen screen, cashier the payment screen, waiter and manager the table map");

        var redirected = Context("/");
        GuestMenuEntry.Invoke(redirected, _ => Task.CompletedTask).GetAwaiter().GetResult();
        check(redirected.Response.StatusCode == StatusCodes.Status302Found && redirected.Response.Headers.Location == "/Menu",
            "Guest menu: redirect target is /Menu");
    }
}
