using RestaurantManagement.Web.Security;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models;

namespace RestaurantManagement.Web.Controllers;

/// <summary>
/// S1-05: màn hình nhật ký đăng nhập và sửa giá món. Chỉ vai trò Quản lý (Manager) được xem; chỉ đọc.
/// Task 2: lọc theo khoảng ngày (giờ Việt Nam) và tài khoản; mặc định 7 ngày gần nhất.
/// Phân trang: tham số <c>page</c> (bắt đầu từ 1), mỗi trang <see cref="SecurityAuditFilter.PageSize"/> dòng; giữ nguyên bộ lọc khi chuyển trang.
/// Task 3: chỉ có một action GET, không có sửa/xoá. Vai trò khác nhận 403 kèm trang “Không có quyền truy cập” (S1-04 Task 3).
/// Database kiểm tra lại quyền Audit.Read mỗi lần xem, nên tài khoản bị hạ vai trò hoặc thu quyền trong lúc
/// vẫn còn phiên đăng nhập (cookie còn ghi vai trò cũ) cũng bị chặn.
/// </summary>
[Authorize(Roles = AppRoles.Manager)]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class AuditLogsController(SecurityAuditStore audit) : Controller
{
    /// <summary>Mã lỗi của dbo.usp_RequirePermission khi tài khoản không có quyền.</summary>
    public const int PermissionDeniedError = 51001;

    [HttpGet]
    public async Task<IActionResult> Index(DateOnly? fromDate, DateOnly? toDate, int? userId, int? page)
    {
        var actorId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        try
        {
            var accounts = await audit.Accounts(actorId);
            var invalidInput = ModelState.TryGetValue(nameof(fromDate), out var fromState) && fromState.Errors.Count > 0
                || ModelState.TryGetValue(nameof(toDate), out var toState) && toState.Errors.Count > 0;
            var filter = SecurityAuditFilter.Resolve(fromDate, toDate, userId,
                SecurityAuditFilter.TodayVietnam(DateTime.UtcNow), accounts.Select(a => a.Id).ToArray(), invalidInput);
            var result = await audit.Search(actorId, filter, page ?? 1);
            return View(new SecurityAuditViewModel(filter, accounts, result));
        }
        catch (SqlException ex) when (ex.Number == PermissionDeniedError)
        {
            return Forbid();
        }
    }
}
