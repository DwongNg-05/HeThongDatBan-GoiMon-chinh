using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Models;
using RestaurantManagement.Web.Authentication;

namespace RestaurantManagement.Web.Controllers;

[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class AccountController(ManagementStore store, LoginSessionStore sessions, PasswordChangeStore passwords, SecurityAuditStore audit) : Controller
{
    public const string InvalidCredentials = "Tên đăng nhập, số điện thoại hoặc mật khẩu không hợp lệ.";

    [AllowAnonymous, HttpGet]
    public IActionResult Login(bool sessionExpired = false, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        var expiredNotice = TempData[IdleSessionEvents.ExpiredItem] is true;
        return User.Identity?.IsAuthenticated == true
        ? RedirectAfterLogin(returnUrl) : View(new LoginModel
        {
            SessionExpired = sessionExpired || expiredNotice || HttpContext.Items.ContainsKey(IdleSessionEvents.ExpiredItem)
        });
    }

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        var result = ModelState.IsValid ? await store.Authenticate(model.Identifier, model.Password) : new LoginResult(null);
        var user = result.User;
        var ipAddress = ClientIp.From(HttpContext);
        if (user is null)
        {
            // S1-05: ghi đăng nhập thất bại (sai mật khẩu, tài khoản khoá/ngừng hoạt động, định danh không tồn tại, thiếu dữ liệu).
            await audit.WriteLogin(result.Candidate?.Id, model.Identifier, succeeded: false, ipAddress);
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
        await audit.WriteLogin(user.Id, user.UserName, succeeded: true, ipAddress);
        TempData.Remove(IdleSessionEvents.ExpiredItem);
        TempData["Success"] = "Đăng nhập thành công.";
        return await passwords.IsRequired(user.Id)
            ? RedirectToAction(nameof(DoiMatKhau)) : RedirectAfterLogin(returnUrl);
    }

    [Authorize, HttpGet]
    public IActionResult DoiMatKhau() => View();

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DoiMatKhau(string? matKhauHienTai, string? matKhauMoi, string? xacNhanMatKhau)
    {
        string? error = null;
        if (string.IsNullOrWhiteSpace(matKhauHienTai)) error = "Vui lòng nhập mật khẩu hiện tại.";
        else if (string.IsNullOrWhiteSpace(matKhauMoi) || matKhauMoi.Length < 8
            || System.Text.Encoding.UTF8.GetByteCount(matKhauMoi) > 72
            || !matKhauMoi.Any(char.IsLetter) || !matKhauMoi.Any(char.IsDigit))
            error = "Mật khẩu mới phải có ít nhất 8 ký tự, gồm chữ và số, tối đa 72 byte UTF-8.";
        else if (matKhauMoi != xacNhanMatKhau) error = "Mật khẩu xác nhận không khớp.";
        else if (matKhauMoi == matKhauHienTai) error = "Mật khẩu mới không được trùng mật khẩu hiện tại.";
        if (error is null)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var sessionId = Guid.Parse(User.FindFirstValue(LoginSessionStore.SessionClaim)!);
            if (await passwords.Change(userId, sessionId, matKhauHienTai!, matKhauMoi!))
            {
                TempData["Success"] = "Đổi mật khẩu thành công!";
                return RedirectToAction("Index", "Home");
            }
            error = "Mật khẩu hiện tại không đúng hoặc phiên đăng nhập không còn hợp lệ.";
        }
        // A wrong current password must not increment the login lockout counter.
        ModelState.AddModelError("", error);
        ViewBag.Loi = error;
        return View();
    }

    private IActionResult RedirectAfterLogin(string? returnUrl) =>
        Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl!) : RedirectToAction("Index", "Home");

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await sessions.Revoke(User);
        await HttpContext.SignOutAsync();
        HttpContext.Session.Clear();
        HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    public IActionResult AccessDenied() => StatusCode(403, "Bạn không có quyền truy cập chức năng này.");
}
