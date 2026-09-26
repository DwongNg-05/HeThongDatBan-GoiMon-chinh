using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Models;
using RestaurantManagement.Web.Authentication;

namespace RestaurantManagement.Web.Controllers;

[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class AccountController(ManagementStore store, LoginSessionStore sessions) : Controller
{
    public const string InvalidCredentials = "Tên đăng nhập, số điện thoại hoặc mật khẩu không hợp lệ.";

    [AllowAnonymous, HttpGet]
    public IActionResult Login(bool sessionExpired = false)
    {
        var expiredNotice = TempData[IdleSessionEvents.ExpiredItem] is true;
        return User.Identity?.IsAuthenticated == true
        ? RedirectToAction("Index", "Management") : View(new LoginModel
        {
            SessionExpired = sessionExpired || expiredNotice || HttpContext.Items.ContainsKey(IdleSessionEvents.ExpiredItem)
        });
    }

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginModel model)
    {
        var result = ModelState.IsValid ? await store.Authenticate(model.Identifier, model.Password) : new LoginResult(null);
        var user = result.User;
        if (user is null)
        {
            ModelState.Clear();
            ModelState.AddModelError("", InvalidCredentials);
            model.Password = "";
            model.RemainingSeconds = result.RemainingSeconds;
            return View(model);
        }
        await sessions.Revoke(User);
        var sessionId = await sessions.Create(user.Id);
        var identity = new ClaimsIdentity(new[] {
            new Claim(LoginSessionStore.SessionClaim, sessionId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.UserName),
            new Claim("FullName", user.FullName),
            new Claim(ClaimTypes.Role, user.Role)
        }, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = false });
        TempData.Remove(IdleSessionEvents.ExpiredItem);
        TempData["Success"] = "Đăng nhập thành công.";
        return RedirectToAction("Index", "Management");
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await sessions.Revoke(User);
        await HttpContext.SignOutAsync();
        HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    public IActionResult AccessDenied() => StatusCode(403, "Bạn không có quyền truy cập chức năng này.");
}
