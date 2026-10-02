using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models;

namespace RestaurantManagement.Web.Controllers;

/// <summary>
/// S1-05: màn hình nhật ký đăng nhập và sửa giá món. Chỉ vai trò Quản lý (Manager) được xem; chỉ đọc.
/// Task 2: lọc theo khoảng ngày (giờ Việt Nam) và tài khoản; mặc định 7 ngày gần nhất.
/// Task 3: chỉ có một action GET, không có sửa/xoá. Vai trò khác bị chuyển tới /Account/AccessDenied (403).
/// Database kiểm tra lại quyền Audit.Read mỗi lần xem, nên tài khoản bị hạ vai trò hoặc thu quyền trong lúc
/// vẫn còn phiên đăng nhập (cookie còn ghi vai trò cũ) cũng bị chặn.
/// </summary>
[Authorize(Roles = "Manager")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class AuditLogsController(SecurityAuditStore audit) : Controller
{
    /// <summary>Mã lỗi của dbo.usp_RequirePermission khi tài khoản không có quyền.</summary>
    public const int PermissionDeniedError = 51001;

    [HttpGet]
    public async Task<IActionResult> Index(DateOnly? fromDate, DateOnly? toDate, int? userId)
    {
        var actorId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        try
        {
            var accounts = await audit.Accounts(actorId);
            var invalidInput = ModelState.TryGetValue(nameof(fromDate), out var fromState) && fromState.Errors.Count > 0
                || ModelState.TryGetValue(nameof(toDate), out var toState) && toState.Errors.Count > 0;
            var filter = SecurityAuditFilter.Resolve(fromDate, toDate, userId,
                SecurityAuditFilter.TodayVietnam(DateTime.UtcNow), accounts.Select(a => a.Id).ToArray(), invalidInput);
            var page = await audit.Search(actorId, filter);
            return View(new SecurityAuditViewModel(filter, accounts, page));
        }
        catch (SqlException ex) when (ex.Number == PermissionDeniedError)
        {
            return Forbid();
        }
    }
}
