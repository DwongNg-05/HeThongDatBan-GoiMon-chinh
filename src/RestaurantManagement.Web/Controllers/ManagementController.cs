using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models;

namespace RestaurantManagement.Web.Controllers;

[Authorize(Roles = "Manager")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class ManagementController(ManagementStore store) : Controller
{
    private int ActorId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public async Task<IActionResult> Index() => View(await store.GetMenu(ActorId));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Price(int id, decimal price)
    {
        if (!ModelState.IsValid || price <= 0 || price > 50000000 || decimal.Truncate(price) != price)
        {
            ModelState.AddModelError("price", "Giá không hợp lệ.");
            TempData["Error"] = "Giá phải là số nguyên từ 1 đến 50.000.000 đồng.";
        }
        else await Save(() => store.ChangeMenu(ActorId, id, price: price));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Availability(int id, bool soldOut)
    {
        if (!ModelState.IsValid) return BadRequest();
        await Save(() => store.ChangeMenu(ActorId, id, soldOut: soldOut));
        return RedirectToAction(nameof(Index));
    }

    private async Task Save(Func<Task> change)
    {
        try { await change(); TempData["Success"] = "Đã lưu thay đổi và ghi nhận người thực hiện."; }
        catch (SqlException ex) when (ex.Number is 51001 or 51039 or 51040)
        {
            ModelState.AddModelError("", "Không thể cập nhật thực đơn.");
            TempData["Error"] = "Không thể cập nhật: món không tồn tại hoặc tài khoản không có quyền.";
        }
    }
}
