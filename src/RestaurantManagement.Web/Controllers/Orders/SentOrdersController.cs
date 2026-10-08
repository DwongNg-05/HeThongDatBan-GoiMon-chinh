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

    [HttpPost("{id:long}/CancelPrepared")]
    [Authorize(Roles = AppRoles.Manager)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelPrepared(long id, int quantity, string? reason, Guid requestId,
        string? expectedStatus, bool confirmCharged, CancellationToken ct)
    {
        if (id <= 0 || quantity is < 1 or > 99 || requestId == Guid.Empty || !confirmCharged
            || expectedStatus is not ("Preparing" or "Ready") || reason is not ("ChangedMind" or "Mistake" or "SoldOut"))
            return BadRequest(new { message = "Chọn lý do và xác nhận huỷ toàn bộ dòng có tính tiền." });
        try
        {
            var changed = await orders.CancelPrepared(id, quantity, reason, User.ActorUserId(), requestId, expectedStatus, confirmCharged, ct);
            return Ok(new { changed, message = changed ? "Đã huỷ có tính tiền. Bếp nhận thông báo dừng món; tạm tính giữ nguyên." : "Yêu cầu này đã được xử lý; không huỷ hoặc tính tiền thêm." });
        }
        catch (SqlException ex) when (ex.Number is >= 51000 and < 51500)
        {
            var status = ex.Number switch { 51001 => 403, 51035 => 404, 51032 or 51034 or 51036 => 400, _ => 409 };
            return StatusCode(status, new { message = ex.Number == 51001 ? "Chỉ quản lý được huỷ món đã bắt đầu chế biến." : ex.Message });
        }
    }
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
