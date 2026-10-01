using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Controllers;

/// <summary>
/// S2-01 Task 1: thực đơn công khai cho khách (GET /ThucDon). Mở trên điện thoại, không cần đăng nhập, chỉ đọc.
/// Giỏ gọi món của nhân viên vẫn nằm ở /GoiMon (cần đăng nhập).
/// </summary>
[AllowAnonymous]
public sealed class ThucDonController(IQuanLyMonStore store) : Controller
{
    [HttpGet]
    public IActionResult Index() => View(store.LayThucDonCongKhai());
}
