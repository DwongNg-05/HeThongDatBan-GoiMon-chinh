using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Models;
using RestaurantManagement.Web.Authentication;
using RestaurantManagement.Web.Services.EmailVerification;

namespace RestaurantManagement.Web.Controllers;

[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class AccountController(ManagementStore store, LoginSessionStore sessions, PasswordChangeStore passwords, SecurityAuditStore audit,
    EmailVerificationService verification) : Controller
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
        var principal = new ClaimsPrincipal(identity);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            principal, new AuthenticationProperties { IsPersistent = false });
        await audit.WriteLogin(user.Id, user.UserName, succeeded: true, ipAddress);
        TempData.Remove(IdleSessionEvents.ExpiredItem);
        TempData["Success"] = "Đăng nhập thành công.";
        // Xác minh email (mọi vai trò trừ Quản lý) diễn ra trước bước đổi mật khẩu.
        if (verification.IsRequired(principal))
        {
            var state = await verification.State(user.Id, sessionId);
            if (state?.Email is { } email)
            {
                var sent = await verification.SendCode(user.Id, sessionId, email, user.FullName, HttpContext.RequestAborted);
                TempData[sent.Sent ? VerifyInfo : VerifyError] = sent.Sent ? "Đã gửi mã xác minh tới email của bạn." : sent.Error;
            }
            return RedirectToAction(nameof(XacMinhEmail), new { returnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null });
        }
        return await passwords.IsRequired(user.Id)
            ? RedirectToAction(nameof(DoiMatKhau)) : RedirectAfterLogin(returnUrl);
    }

    private const string VerifyInfo = "EmailVerificationInfo";
    private const string VerifyError = "EmailVerificationError";

    private (int UserId, Guid SessionId) CurrentIds() =>
        (int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), Guid.Parse(User.FindFirstValue(LoginSessionStore.SessionClaim)!));

    [Authorize, HttpGet]
    public async Task<IActionResult> XacMinhEmail(string? returnUrl = null)
    {
        if (!verification.IsRequired(User)) return await AfterVerification(returnUrl);
        var (userId, sessionId) = CurrentIds();
        var state = await verification.State(userId, sessionId);
        if (state is null) return await ExpiredSession();
        if (state.Verified) return await CompleteVerification(sessionId, returnUrl);
        return View(BuildModel(state, returnUrl, TempData[VerifyInfo] as string, TempData[VerifyError] as string));
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> XacMinhEmail(string? code, string? returnUrl = null)
    {
        if (!verification.IsRequired(User)) return await AfterVerification(returnUrl);
        var (userId, sessionId) = CurrentIds();
        var state = await verification.State(userId, sessionId);
        if (state is null) return await ExpiredSession();
        var normalized = VerificationCode.Normalize(code);
        string error;
        if (!VerificationCode.IsWellFormed(normalized))
            error = "Mã xác minh gồm đúng 6 ký tự, chỉ có chữ in hoa (A–Z) và số (0–9).";
        else
        {
            var (status, remaining) = await verification.Verify(userId, sessionId, normalized);
            if (status == VerifyStatus.Verified)
            {
                TempData["Success"] = "Xác minh email thành công.";
                return await CompleteVerification(sessionId, returnUrl);
            }
            error = status switch
            {
                VerifyStatus.Wrong => $"Mã xác minh không đúng. Bạn còn {remaining} lần thử.",
                VerifyStatus.TooManyAttempts => "Bạn đã nhập sai quá nhiều lần. Hãy bấm “Gửi lại mã” để nhận mã mới.",
                _ => "Mã đã hết hạn hoặc chưa được gửi. Hãy bấm “Gửi lại mã” để nhận mã mới."
            };
        }
        var model = BuildModel(state, returnUrl, null, error);
        model.Code = normalized.Length <= 8 ? normalized : null;
        return View(model);
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> GuiLaiMaXacMinh(string? returnUrl = null)
    {
        if (!verification.IsRequired(User)) return await AfterVerification(returnUrl);
        var (userId, sessionId) = CurrentIds();
        var state = await verification.State(userId, sessionId);
        if (state is null) return await ExpiredSession();
        if (state.TargetEmail is not { } email)
            TempData[VerifyError] = "Hãy nhập email của bạn trước.";
        else
        {
            var sent = await verification.SendCode(userId, sessionId, email, User.FindFirstValue("FullName") ?? "", HttpContext.RequestAborted);
            TempData[sent.Sent ? VerifyInfo : VerifyError] = sent.Sent ? "Đã gửi mã mới. Mã cũ không còn dùng được." : sent.Error;
        }
        return RedirectToAction(nameof(XacMinhEmail), new { returnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null });
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DatEmailXacMinh(string? email, string? returnUrl = null)
    {
        if (!verification.IsRequired(User)) return await AfterVerification(returnUrl);
        var (userId, sessionId) = CurrentIds();
        var state = await verification.State(userId, sessionId);
        if (state is null) return await ExpiredSession();
        if (state.Email is not null)
            TempData[VerifyError] = "Tài khoản đã có email. Mã xác minh luôn được gửi tới email này.";
        else if (!EmailVerificationService.IsValidEmail(email))
            TempData[VerifyError] = "Email không hợp lệ. Ví dụ đúng: tenban@gmail.com";
        else
        {
            var sent = await verification.SendCode(userId, sessionId, email!.Trim(), User.FindFirstValue("FullName") ?? "", HttpContext.RequestAborted);
            TempData[sent.Sent ? VerifyInfo : VerifyError] = sent.Sent ? "Đã gửi mã xác minh. Hãy mở email và nhập mã bên dưới." : sent.Error;
        }
        return RedirectToAction(nameof(XacMinhEmail), new { returnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null });
    }

    private EmailVerificationViewModel BuildModel(EmailVerificationState state, string? returnUrl, string? info, string? error)
    {
        var wait = state.LastSentAtUtc is { } last
            ? (int)Math.Ceiling((last.AddSeconds(verification.Options.ResendCooldownSeconds) - DateTime.UtcNow).TotalSeconds) : 0;
        return new EmailVerificationViewModel
        {
            HasSavedEmail = state.Email is not null,
            MaskedEmail = state.TargetEmail is { } e ? VerificationCode.MaskEmail(e) : null,
            CodeSent = state.ActiveExpiresAtUtc is not null,
            ResendAfterSeconds = Math.Max(0, wait),
            ValidMinutes = verification.Options.ValidMinutes,
            ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null,
            Info = info,
            Error = error
        };
    }

    /// <summary>Ghi nhận phiên đã xác minh vào cookie đăng nhập rồi chuyển sang bước tiếp theo (đổi mật khẩu nếu bắt buộc).</summary>
    private async Task<IActionResult> CompleteVerification(Guid sessionId, string? returnUrl)
    {
        var claims = User.Claims.Where(c => c.Type != EmailVerificationService.VerifiedClaim)
            .Append(new Claim(EmailVerificationService.VerifiedClaim, sessionId.ToString()));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties { IsPersistent = false });
        HttpContext.User = principal;
        return await AfterVerification(returnUrl);
    }

    private async Task<IActionResult> AfterVerification(string? returnUrl)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return await passwords.IsRequired(userId) ? RedirectToAction(nameof(DoiMatKhau)) : RedirectAfterLogin(returnUrl);
    }

    private async Task<IActionResult> ExpiredSession()
    {
        await sessions.Revoke(User);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login), new { sessionExpired = true });
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
