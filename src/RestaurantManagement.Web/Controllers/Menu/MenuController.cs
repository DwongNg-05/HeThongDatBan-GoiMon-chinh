using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Models;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Controllers;

/// <summary>
/// S2-01 Task 1: thực đơn công khai cho khách (GET /Menu). Mở trên điện thoại, không cần đăng nhập, chỉ đọc.
/// S2-01 Task 2: tìm món theo tên qua tham số <c>q</c> (GET /Menu?q=com+rang), không phân biệt hoa/thường và dấu.
/// Giỏ gọi món của nhân viên vẫn nằm ở /Ordering (cần đăng nhập).
/// </summary>
[AllowAnonymous]
public sealed class MenuController(IMenuStore store) : Controller
{
    [HttpGet]
    public IActionResult Index(string? q = null)
    {
        var menu = store.GetPublicMenu();
        var keyword = MenuSearch.CleanKeyword(q);
        return View(new PublicMenuViewModel(keyword, MenuSearch.Filter(menu, keyword), menu.Count == 0));
    }
}
