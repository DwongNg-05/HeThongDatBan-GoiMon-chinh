using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using RestaurantManagement.Data;
using RestaurantManagement.Web.Services;
using System.Security.Claims;

namespace RestaurantManagement.Web.Controllers
{
    public class AccountController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly AuditLogService _auditLogService;

        public AccountController(
            ApplicationDbContext context,
            AuditLogService auditLogService)
        {
            _context = context;
            _auditLogService = auditLogService;
        }

        [AllowAnonymous, HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectAfterLogin(returnUrl);
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string username, string password, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
            username = (username ?? "").Trim();
            password ??= "";

            var user = await _context.Users
                .FirstOrDefaultAsync(x => x.Username == username && x.IsActive);

            if (user == null || !VerifyPassword(password, user.Password))
            {
                // Đăng nhập thất bại: nếu tồn tại tài khoản thì ghi đúng vai trò của nó
                await _auditLogService.LogAsync(
                    username,
                    user?.Role ?? "Unknown",
                    "Đăng nhập thất bại",
                    ipAddress);

                ModelState.AddModelError("", "Sai tài khoản hoặc mật khẩu.");
                return View();
            }

            // Đăng nhập thành công
            await _auditLogService.LogAsync(
                user.Username,
                user.Role,
                "Đăng nhập thành công",
                ipAddress);

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, user.Role)
            };

            var claimsIdentity = new ClaimsIdentity(
                claims, CookieAuthenticationDefaults.AuthenticationScheme);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity));

            return RedirectAfterLogin(returnUrl);
        }

        private IActionResult RedirectAfterLogin(string? returnUrl) =>
            Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl!) : RedirectToAction("Index", "Home");

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }

        // Trang hiển thị khi đã đăng nhập nhưng không đủ quyền
        [HttpGet]
        public IActionResult AccessDenied()
        {
            Response.StatusCode = 403;
            return View();
        }

        // Mật khẩu băm bcrypt (DbTool dùng BCrypt.Net-Next).
        // Nhánh so sánh chuỗi thường chỉ để tương thích tạm với dữ liệu cũ chưa băm.
        private static bool VerifyPassword(string input, string? stored)
        {
            if (string.IsNullOrEmpty(stored))
            {
                return false;
            }

            if (stored.StartsWith("$2"))
            {
                try
                {
                    return BCrypt.Net.BCrypt.Verify(input, stored);
                }
                catch
                {
                    return false;
                }
            }

            return input == stored;
        }
    }
}