using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RestaurantManagement.Web.Controllers;

/// <summary>
/// Trang giới thiệu Bếp Nhà cho khách (GET /About): không gian, đầu bếp, ẩm thực và thông tin liên hệ.
/// Trang công khai, chỉ đọc, không cần đăng nhập. Giao diện theo tinh thần Gucci Osteria (wwwroot/css/about.css, wwwroot/js/about.js).
/// </summary>
[AllowAnonymous]
public sealed class AboutController : Controller
{
    [HttpGet]
    public IActionResult Index() => View();
}
