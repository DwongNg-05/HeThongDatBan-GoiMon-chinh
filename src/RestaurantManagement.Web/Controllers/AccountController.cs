using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Models;

namespace RestaurantManagement.Web.Controllers
{
    public class AccountController : Controller
    {
        private readonly SignInManager<TaiKhoan> _signInManager;
        private readonly UserManager<TaiKhoan> _userManager;

        public AccountController(
            SignInManager<TaiKhoan> signInManager,
            UserManager<TaiKhoan> userManager)
        {
            _signInManager = signInManager;
            _userManager = userManager;
        }

        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string tenDangNhap, string matKhau)
        {
            if (string.IsNullOrWhiteSpace(tenDangNhap) || string.IsNullOrWhiteSpace(matKhau))
            {
                ViewBag.Loi = "Vui lòng nhập tên đăng nhập và mật khẩu.";
                return View();
            }

            // Đăng nhập có đếm số lần sai để khóa tài khoản khi sai ở trang Login
            var ketQua = await _signInManager.PasswordSignInAsync(
                tenDangNhap,
                matKhau,
                isPersistent: false,
                lockoutOnFailure: true);

            if (ketQua.Succeeded)
            {
                var taiKhoan = await _userManager.FindByNameAsync(tenDangNhap);

                if (taiKhoan != null && taiKhoan.BatBuocDoiMatKhau)
                {
                    return RedirectToAction("DoiMatKhau", "Account");
                }

                return RedirectToAction("Index", "Home");
            }

            if (ketQua.IsLockedOut)
            {
                ViewBag.Loi = "Tài khoản của bạn đã bị khóa do nhập sai quá số lần quy định.";
                return View();
            }

            ViewBag.Loi = "Tên đăng nhập hoặc mật khẩu không đúng.";
            return View();
        }

        [HttpGet]
        public IActionResult DoiMatKhau()
        {
            if (User.Identity?.IsAuthenticated != true)
            {
                return RedirectToAction("Login");
            }

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DoiMatKhau(
            string matKhauHienTai,
            string matKhauMoi,
            string xacNhanMatKhau)
        {
            if (User.Identity?.IsAuthenticated != true)
            {
                return RedirectToAction("Login");
            }

            // Kiểm tra rỗng
            if (string.IsNullOrWhiteSpace(matKhauHienTai))
            {
                ViewBag.Loi = "Vui lòng nhập mật khẩu hiện tại.";
                return View();
            }

            if (string.IsNullOrWhiteSpace(matKhauMoi))
            {
                ViewBag.Loi = "Vui lòng nhập mật khẩu mới.";
                return View();
            }

            if (matKhauMoi.Length < 8)
            {
                ViewBag.Loi = "Mật khẩu mới phải có ít nhất 8 ký tự.";
                return View();
            }

            if (!matKhauMoi.Any(char.IsLetter) || !matKhauMoi.Any(char.IsDigit))
            {
                ViewBag.Loi = "Mật khẩu mới phải có ít nhất một chữ cái và một chữ số.";
                return View();
            }

            if (matKhauMoi != xacNhanMatKhau)
            {
                ViewBag.Loi = "Mật khẩu xác nhận không khớp.";
                return View();
            }

            if (matKhauMoi == matKhauHienTai)
            {
                ViewBag.Loi = "Mật khẩu mới không được trùng mật khẩu hiện tại.";
                return View();
            }

            var taiKhoan = await _userManager.GetUserAsync(User);
            if (taiKhoan == null)
            {
                await _signInManager.SignOutAsync();
                return RedirectToAction("Login");
            }

            // Thực hiện đổi mật khẩu
            var ketQua = await _userManager.ChangePasswordAsync(taiKhoan, matKhauHienTai, matKhauMoi);

            // TASK 3 (AC4): Nếu sai mật khẩu ở màn hình Đổi mật khẩu
            // Không gọi AccessFailedAsync để KHÔNG làm tăng số lần khóa tài khoản
            if (!ketQua.Succeeded)
            {
                ViewBag.Loi = string.Join(" ", ketQua.Errors.Select(e => e.Description));
                return View();
            }

            // Cập nhật thông tin khi thành công
            taiKhoan.BatBuocDoiMatKhau = false;
            taiKhoan.ThoiGianDoiMatKhau = DateTime.UtcNow;
            await _userManager.UpdateAsync(taiKhoan);

            // Cập nhật SecurityStamp để hủy phiên ở các thiết bị khác & làm mới phiên hiện tại
            await _userManager.UpdateSecurityStampAsync(taiKhoan);
            await _signInManager.RefreshSignInAsync(taiKhoan);

            TempData["ThongBao"] = "Đổi mật khẩu thành công!";
            return RedirectToAction("Index", "Home");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Login", "Account");
        }
    }
}