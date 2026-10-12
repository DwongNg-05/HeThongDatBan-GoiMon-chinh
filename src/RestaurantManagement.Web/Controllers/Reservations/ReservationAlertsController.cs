using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Authentication;
using RestaurantManagement.Web.Models.Reservations;
using RestaurantManagement.Web.Security;

namespace RestaurantManagement.Web.Controllers;

/// <summary>
/// S3-09 Task 1: cảnh báo đặt bàn trễ 15 phút (sơ đồ bàn, danh sách đặt bàn) và thao tác "Đánh dấu khách không tới".
/// Quản lý và Phục vụ (cùng quyền Reservations.Manage trong database). Tài liệu: docs/S3-09-Task1.md.
/// </summary>
[Route("api/reservation-alerts")]
[Authorize(Roles = AppRoles.FrontOfHouse)]
public sealed class ReservationAlertsController(IConfiguration configuration) : Controller
{
    private LateReservationStore Store => new(configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Chưa cấu hình kết nối database."));

    /// <summary>
    /// Cảnh báo hiện tại + mốc cảnh báo kế tiếp. Màn hình gọi định kỳ nên không tính là thao tác của nhân viên
    /// (PassiveSessionRead: không kéo dài phiên đăng nhập đang để yên).
    /// </summary>
    [HttpGet("")]
    [PassiveSessionRead]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        try
        {
            return Json(await Store.ReadAsync(cancellationToken));
        }
        catch (SqlException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Không tải được cảnh báo đặt bàn trễ. Đang thử lại…" });
        }
    }

    /// <summary>
    /// Đánh dấu khách không tới: đổi trạng thái, ghi lịch sử số điện thoại và giải phóng bàn trong cùng một giao dịch.
    /// Gửi lặp hoặc hai nhân viên cùng bấm: lần sau nhận "đã đánh dấu trước đó" (200), không ghi thêm.
    /// </summary>
    [HttpPost("{id:long}/no-show")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkNoShow(long id, CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId)) return Forbid();
        try
        {
            var result = await Store.MarkNoShowAsync(id, actorUserId, cancellationToken);
            return Json(new
            {
                result.AlreadyMarked, result.ReservationCode, result.TableCode, result.TableStatus, result.TableReleased,
                result.PhoneNoShowCount, result.Message
            });
        }
        catch (NoShowRejectedException rejected)
        {
            return StatusCode(rejected.StatusCode, new { message = rejected.Message });
        }
    }
}
