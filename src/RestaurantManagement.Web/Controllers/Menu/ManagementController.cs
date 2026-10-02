using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models;

namespace RestaurantManagement.Web.Controllers;

/// <summary>
/// Màn hình "Sửa giá món" cũ đã được gộp vào Quản lý món (/Dishes):
/// giá được sửa ngay ở trang Sửa món, nhật ký thay đổi giá hiển thị trong Quản lý món.
/// Controller này chỉ còn chuyển hướng đường dẫn cũ và xử lý nút "Tạm hết / Còn món" của Quản lý.
/// </summary>
[Authorize(Roles = "Manager")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class ManagementController(ManagementStore store) : Controller
{
    private int ActorId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public IActionResult Index() => RedirectToPage("/Dishes/Index");

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Availability(int id, bool soldOut)
    {
        if (!ModelState.IsValid) return BadRequest();
        try
        {
            await store.ChangeMenu(ActorId, id, soldOut: soldOut);
            TempData["Success"] = soldOut ? "Đã đánh dấu món tạm hết." : "Đã mở bán lại món.";
        }
        catch (SqlException ex) when (ex.Number is 51001 or 51039 or 51040)
        {
            TempData["Error"] = "Không thể cập nhật: món không tồn tại hoặc tài khoản không có quyền.";
        }
        return RedirectToPage("/Dishes/Index");
    }
}
