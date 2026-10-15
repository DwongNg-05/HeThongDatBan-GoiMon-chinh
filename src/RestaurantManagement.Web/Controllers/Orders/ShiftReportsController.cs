using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models.Orders;
using RestaurantManagement.Web.Security;

namespace RestaurantManagement.Web.Controllers;

/// <summary>
/// S3-05 Task 2: báo cáo cuối ca — nhật ký huỷ món (huỷ thành công và yêu cầu bị từ chối) của từng ca.
/// Chỉ Quản lý: web chặn bằng [Authorize(Manager)], database kiểm tra quyền Audit.Read (dbo.usp_ShiftCancellationReport).
/// Tài liệu: docs/S3-05-Task2.md.
/// </summary>
[Route("Reports/Shift")]
[Authorize(Roles = AppRoles.Manager)]
public sealed class ShiftReportsController(IConfiguration configuration) : Controller
{
    private ShiftCancellationStore Store => new(configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Chưa cấu hình kết nối database."));

    /// <summary>Trang báo cáo: chọn ca (mặc định ca đang mở hoặc ca gần nhất), xem mọi lần huỷ món của ca đó.</summary>
    [HttpGet("Cancellations")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Cancellations(long? shiftId, CancellationToken cancellationToken)
    {
        IReadOnlyList<ShiftOption> shifts;
        try
        {
            shifts = await Store.ShiftsAsync(cancellationToken);
        }
        catch (SqlException)
        {
            return View(new ShiftCancellationPage([], shiftId, null, LoadError));
        }
        var selected = shiftId ?? shifts.FirstOrDefault()?.Id;
        if (selected is null)
            return View(new ShiftCancellationPage(shifts, null, null, null));
        try
        {
            var report = await Store.ReportAsync(selected.Value, User.ActorUserId(), cancellationToken);
            return View(new ShiftCancellationPage(shifts, selected, report, null));
        }
        catch (SqlException ex)
        {
            return View(new ShiftCancellationPage(shifts, selected, null, ex.Number == 51141 ? ex.Message : LoadError));
        }
    }

    /// <summary>Dữ liệu báo cáo (JSON) của một ca — cùng quyền với trang.</summary>
    [HttpGet("/api/reports/shift-cancellations")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> CancellationsData(long? shiftId, CancellationToken cancellationToken)
    {
        try
        {
            var selected = shiftId ?? (await Store.ShiftsAsync(cancellationToken)).FirstOrDefault()?.Id;
            if (selected is null) return NotFound(new { message = "Chưa có ca nào." });
            var report = await Store.ReportAsync(selected.Value, User.ActorUserId(), cancellationToken);
            return Json(new
            {
                shift = report.Shift, report.CancelledCount, report.RejectedCount, report.FreeAmount, report.ChargedAmount,
                entries = report.Entries.Select(e => new
                {
                    e.Kind, e.KindLabel, occurredAtUtc = e.OccurredAtUtc, e.OccurredAtLabel, e.BillSessionId, e.TableCode, e.BatchNumber,
                    e.OrderItemId, e.ItemName, e.Quantity, e.Unit, e.LineTotal, e.ItemStatus, e.ChargeWhenCancelled, e.ChargeKind, e.ChargeLabel,
                    e.Reason, e.ReasonLabel, e.Note, e.ActorName, e.ActorRole, e.Message
                })
            });
        }
        catch (SqlException ex) when (ex.Number == 51141)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (SqlException ex) when (ex.Number == 51001)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Chỉ Quản lý được xem báo cáo huỷ món." });
        }
        catch (SqlException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = LoadError });
        }
    }

    private const string LoadError =
        "Không tải được báo cáo huỷ món. Vui lòng thử lại; nếu vẫn lỗi, kiểm tra database đã chạy migrate (057_S305ShiftCancellationReport.sql).";
}
