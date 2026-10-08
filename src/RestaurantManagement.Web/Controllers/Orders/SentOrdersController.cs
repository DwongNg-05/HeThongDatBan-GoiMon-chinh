using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Authentication;
using RestaurantManagement.Web.Security;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Controllers;

[Authorize(Roles = AppRoles.FrontOfHouse)]
[Route("Ordering/Sent")]
public sealed class SentOrdersController(PendingOrderStore orders) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct) => View(await orders.List(ct));

    [HttpGet("Snapshot")]
    [PassiveSessionRead]
    public async Task<IActionResult> Snapshot(CancellationToken ct) => Ok(await orders.List(ct));

    [HttpPost("{id:long}/Cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(long id, int quantity, string? reason, Guid requestId, CancellationToken ct)
    {
        if (id <= 0 || quantity is < 1 or > 99 || requestId == Guid.Empty
            || reason is not ("ChangedMind" or "Mistake" or "SoldOut"))
            return BadRequest(new { message = "Chọn số lượng hợp lệ và một lý do huỷ bắt buộc." });
        try
        {
            var changed = await orders.Cancel(id, quantity, reason, User.ActorUserId(), requestId, ct);
            return Ok(new { changed, message = changed ? "Đã huỷ món và cập nhật tạm tính." : "Yêu cầu này đã được xử lý; không huỷ thêm." });
        }
        catch (SqlException ex) when (ex.Number is >= 51000 and < 51500)
        {
            var status = ex.Number switch { 51001 => 403, 51035 => 404, 51032 or 51034 => 400, _ => 409 };
            return StatusCode(status, new { message = ex.Number == 51001 ? "Bạn không có quyền huỷ món." : ex.Message });
        }
    }
}
