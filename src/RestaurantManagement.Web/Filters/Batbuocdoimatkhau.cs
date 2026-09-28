

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using RestaurantManagement.Web.Models;

namespace RestaurantManagement.Web.Filters
{
    public class BatBuocDoiMatKhauFilter : IAsyncActionFilter
    {
        private readonly UserManager<TaiKhoan> _userManager;

        public BatBuocDoiMatKhauFilter(
            UserManager<TaiKhoan> userManager)
        {
            _userManager = userManager;
        }

        public async Task OnActionExecutionAsync(
            ActionExecutingContext context,
            ActionExecutionDelegate next)
        {
            var user = context.HttpContext.User;

            // Nếu chưa đăng nhập thì cho phép tiếp tục
            if (user.Identity?.IsAuthenticated != true)
            {
                await next();
                return;
            }

            var controller = context.RouteData.Values["controller"]?.ToString();
            var action = context.RouteData.Values["action"]?.ToString();

            // Cho phép vào trang đổi mật khẩu và đăng xuất
            if (string.Equals(controller, "Account",
                    StringComparison.OrdinalIgnoreCase)
                && (string.Equals(action, "DoiMatKhau",
                        StringComparison.OrdinalIgnoreCase)
                    || string.Equals(action, "Logout",
                        StringComparison.OrdinalIgnoreCase)))
            {
                await next();
                return;
            }

            // Lấy tài khoản đang đăng nhập
            var taiKhoan = await _userManager.GetUserAsync(user);

            // Nếu tài khoản chưa đổi mật khẩu thì chuyển hướng
            if (taiKhoan != null && taiKhoan.BatBuocDoiMatKhau)
            {
                context.Result = new RedirectToActionResult(
                    "DoiMatKhau",
                    "Account",
                    null);

                return;
            }

            await next();
        }
    }
}