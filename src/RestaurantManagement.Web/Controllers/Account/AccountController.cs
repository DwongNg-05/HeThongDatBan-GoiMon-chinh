using RestaurantManagement.Web.Security;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
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
            // S1-04 Task 1: gọi bằng fetch (X-Requested-With) nhận dữ liệu lỗi thuần thay vì trang HTML.
            if (IdleSessionEvents.IsApiRequest(Request))
                return Unauthorized(new { succeeded = false, message = InvalidCredentials, remainingSeconds = result.RemainingSeconds });
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
        // Mỗi tài khoản (trừ Quản lý) chỉ xác minh email một lần, ở lần đăng nhập đầu tiên sau khi tạo tài khoản.
        // Email chỉ được lưu vào tài khoản sau khi nhập đúng mã, nên tài khoản đã có email nghĩa là đã xác minh:
        // đánh dấu phiên mới là đã xác minh luôn, không gửi mã nữa.
        if (verification.IsRequired(principal)
            && (await verification.State(user.Id, sessionId))?.Email is not null)
            identity.AddClaim(new Claim(EmailVerificationService.VerifiedClaim, sessionId.ToString()));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            principal, new AuthenticationProperties { IsPersistent = false });
        await audit.WriteLogin(user.Id, user.UserName, succeeded: true, ipAddress);
        TempData.Remove(IdleSessionEvents.ExpiredItem);
        TempData["Success"] = "Đăng nhập thành công.";
        // Xác minh email lần đầu (mọi vai trò trừ Quản lý) diễn ra trước bước đổi mật khẩu.
        string next;
        if (verification.IsRequired(principal))
        {
            var state = await verification.State(user.Id, sessionId);
            if (state?.Email is { } email)
            {
                var sent = await verification.SendCode(user.Id, sessionId, email, user.FullName, HttpContext.RequestAborted);
                TempData[sent.Sent ? VerifyInfo : VerifyError] = sent.Sent ? "Đã gửi mã xác minh tới email của bạn." : sent.Error;
            }
            next = Url.Action(nameof(VerifyEmail), new { returnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null }) ?? "/Account/VerifyEmail";
        }
        else if (await passwords.IsRequired(user.Id))
            next = Url.Action(nameof(ChangePassword)) ?? "/Account/ChangePassword";
        else
            next = Url.IsLocalUrl(returnUrl) ? returnUrl! : Url.Action("Index", "Home") ?? "/";

        // S1-04 Task 1: đăng nhập thành công trả về vai trò (máy chủ đọc từ dbo.Roles, trình duyệt không tự chọn được)
        // khi gọi bằng fetch; form đăng nhập thường vẫn chuyển trang như trước.
        if (IdleSessionEvents.IsApiRequest(Request))
            return Ok(new { succeeded = true, redirectUrl = next, user = RoleNavigation.Describe(principal) });
        return LocalRedirect(next);
    }

    /// <summary>S1-04 Task 1: vai trò và các mục điều hướng của tài khoản đang đăng nhập (JSON).</summary>
    [Authorize, HttpGet("/api/account/me")]
    [Produces("application/json")]
    public IActionResult Me() => Ok(RoleNavigation.Describe(User));

    private const string VerifyInfo = "EmailVerificationInfo";
    private const string VerifyError = "EmailVerificationError";

    private (int UserId, Guid SessionId) CurrentIds() =>
        (int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), Guid.Parse(User.FindFirstValue(LoginSessionStore.SessionClaim)!));

    [Authorize, HttpGet]
    public async Task<IActionResult> VerifyEmail(string? returnUrl = null)
    {
        if (!verification.IsRequired(User)) return await AfterVerification(returnUrl);
        var (userId, sessionId) = CurrentIds();
        var state = await verification.State(userId, sessionId);
        if (state is null) return await ExpiredSession();
        if (state.Verified) return await CompleteVerification(sessionId, returnUrl);
        return View(BuildModel(state, returnUrl, TempData[VerifyInfo] as string, TempData[VerifyError] as string));
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyEmail(string? code, string? returnUrl = null)
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
    public async Task<IActionResult> ResendVerificationCode(string? returnUrl = null)
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
        return RedirectToAction(nameof(VerifyEmail), new { returnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null });
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetVerificationEmail(string? email, string? returnUrl = null)
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
        return RedirectToAction(nameof(VerifyEmail), new { returnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null });
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
        return await passwords.IsRequired(userId) ? RedirectToAction(nameof(ChangePassword)) : RedirectAfterLogin(returnUrl);
    }

    private async Task<IActionResult> ExpiredSession()
    {
        await sessions.Revoke(User);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login), new { sessionExpired = true });
    }

    [Authorize, HttpGet]
    public IActionResult ChangePassword() => View();

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(string? currentPassword, string? newPassword, string? confirmPassword)
    {
        string? error = null;
        if (string.IsNullOrWhiteSpace(currentPassword)) error = "Vui lòng nhập mật khẩu hiện tại.";
        else if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8
            || System.Text.Encoding.UTF8.GetByteCount(newPassword) > 72
            || !newPassword.Any(char.IsLetter) || !newPassword.Any(char.IsDigit))
            error = "Mật khẩu mới phải có ít nhất 8 ký tự, gồm chữ và số, tối đa 72 byte UTF-8.";
        else if (newPassword != confirmPassword) error = "Mật khẩu xác nhận không khớp.";
        else if (newPassword == currentPassword) error = "Mật khẩu mới không được trùng mật khẩu hiện tại.";
        if (error is null)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var sessionId = Guid.Parse(User.FindFirstValue(LoginSessionStore.SessionClaim)!);
            if (await passwords.Change(userId, sessionId, currentPassword!, newPassword!))
            {
                TempData["Success"] = "Đổi mật khẩu thành công!";
                return RedirectToAction("Index", "Home");
            }
            error = "Mật khẩu hiện tại không đúng hoặc phiên đăng nhập không còn hợp lệ.";
        }
        // A wrong current password must not increment the login lockout counter.
        ModelState.AddModelError("", error);
        ViewBag.Error = error;
        return View();
    }

    private IActionResult RedirectAfterLogin(string? returnUrl) =>
        Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl!) : RedirectToAction("Index", "Home");

    // S1-04 Task 4: đăng xuất chỉ cần đã đăng nhập, kể cả khi vai trò không xác định (mọi API khác đều chặn vai trò đó).
    [Authorize(Policy = AppRoles.SignedInPolicy), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await sessions.Revoke(User);
        await HttpContext.SignOutAsync();
        HttpContext.Session.Clear();
        HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
        return RedirectToAction(nameof(Login));
    }

    /// <summary>
    /// S1-04 Task 3: trang báo không có quyền (luôn trả 403). Thường được máy chủ hiển thị ngay tại đường dẫn bị từ chối;
    /// mở trực tiếp vẫn được. Trang có thông điệp, vai trò, đường dẫn đã mở và nút về màn hình chính của vai trò.
    /// </summary>
    [AllowAnonymous]
    public IActionResult AccessDenied(string? returnUrl = null)
    {
        var original = HttpContext.Features.Get<IStatusCodeReExecuteFeature>();
        var requested = original is not null
            ? original.OriginalPathBase + original.OriginalPath + original.OriginalQueryString
            : Url.IsLocalUrl(returnUrl) ? returnUrl : null;
        var view = View(AccessDeniedViewModel.For(User, requested));
        view.StatusCode = StatusCodes.Status403Forbidden;
        return view;
    }
}
