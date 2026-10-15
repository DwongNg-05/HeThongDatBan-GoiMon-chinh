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
/// S3-09 Task 2: thao tác "Gia hạn 15 phút" (tối đa một lần) và danh sách lượt đang gia hạn giữ bàn.
/// S3-09 Task 3: tra cứu số lần khách không tới trong 90 ngày của một số điện thoại khi nhân viên tạo đặt bàn.
/// Quản lý và Phục vụ (cùng quyền Reservations.Manage trong database). Tài liệu: docs/S3-09-Task1.md … docs/S3-09-Task3.md.
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Trình duyệt đã huỷ yêu cầu (chuyển trang, đăng xuất): không phải lỗi.
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
        catch (SqlException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = $"Không tải được cảnh báo đặt bàn trễ ({ex.Number}). {LateReservationRules.DatabaseNotUpdated}" });
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

    /// <summary>
    /// S3-09 Task 2: gia hạn giữ bàn thêm 15 phút cho lượt đang có cảnh báo trễ, tối đa một lần. Trong thời gian gia hạn
    /// cảnh báo được gỡ; hết hạn mà khách chưa tới thì cảnh báo hiện lại và chỉ còn "Đánh dấu khách không tới".
    /// Lần thứ hai (gửi lặp, hai nhân viên cùng bấm, hay bấm lại sau khi hết hạn) bị từ chối với 409.
    /// </summary>
    [HttpPost("{id:long}/extend")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExtendHold(long id, CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId)) return Forbid();
        try
        {
            var result = await Store.ExtendHoldAsync(id, actorUserId, cancellationToken);
            return Json(new
            {
                result.ReservationCode, result.TableCode, result.HoldExtendedUntilUtc, result.HoldUntilTime, result.ExtensionCount,
                result.Message
            });
        }
        catch (NoShowRejectedException rejected)
        {
            return StatusCode(rejected.StatusCode, new { message = rejected.Message });
        }
    }

    /// <summary>
    /// S3-09 Task 3: số lần khách không tới trong 90 ngày của số điện thoại đang nhập trên form tạo đặt bàn.
    /// Chỉ nhân viên (Quản lý, Phục vụ) — khách vãng lai không tra cứu được lịch sử của số điện thoại khác.
    /// Dùng POST (số điện thoại nằm trong thân yêu cầu, không nằm trên URL / nhật ký truy cập) và mã chống giả mạo.
    /// Mọi cách nhập tương đương ("0901 234 567", "+84 901 234 567"…) cho cùng kết quả; trả kèm số đã chuẩn hoá để form điền lại.
    /// </summary>
    [HttpPost("phone-history")]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> PhoneHistory([FromForm] string? phone, CancellationToken cancellationToken)
    {
        try
        {
            var summary = await new PhoneNoShowStore(configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Chưa cấu hình kết nối database.")).CountAsync(phone, cancellationToken);
            if (summary is null)
                return Json(new { valid = false, noShowCount = 0, isWarning = false });
            return Json(new
            {
                valid = true, normalizedPhone = summary.Phone, noShowCount = summary.NoShowCount,
                lastNoShowAtUtc = summary.LastNoShowAtUtc, isWarning = summary.IsWarning, message = summary.Message,
                threshold = PhoneNoShowRules.WarningThreshold, windowDays = PhoneNoShowRules.WindowDays
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
        catch (SqlException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Không kiểm tra được lịch sử không tới của số điện thoại. Hệ thống sẽ kiểm tra lại khi tạo đặt bàn." });
        }
    }
}
