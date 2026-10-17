using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models.Reservations;
using RestaurantManagement.Web.Security;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Controllers;

/// <summary>
/// S4-08 Task 1: màn hình "Email nhắc lịch" — danh sách email nhắc trước giờ hẹn 2 tiếng
/// (mã đặt bàn, giờ hẹn, bàn, email nhận, trạng thái gửi). Quản lý, Phục vụ xem; chỉ Quản lý bấm "Chạy kiểm tra nhắc lịch ngay".
/// </summary>
[Authorize(Roles = AppRoles.FrontOfHouse)]
[Route("ReservationReminders")]
public class ReservationRemindersController(IBookingReminderStore reminders) : Controller
{
    [HttpGet("")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Index(string? filter = null, CancellationToken cancellationToken = default)
    {
        var f = BookingReminderFilter.Normalize(filter);
        try
        {
            var rows = await reminders.ListAsync(f, SqlBookingReminderStore.MaxRows, cancellationToken);
            var lead = await reminders.LeadMinutesAsync(cancellationToken);
            return View(BookingReminderRules.BuildList(rows, f, lead));
        }
        catch (SqlException ex)
        {
            ViewData["LoadError"] = $"Không tải được danh sách email nhắc lịch ({ex.Number}). Kiểm tra đã chạy migration 059 và 060 chưa.";
            return View(BookingReminderRules.BuildList([], f, BookingReminderRules.DefaultLeadMinutes));
        }
    }

    /// <summary>Demo / xử lý tay: chạy ngay tác vụ nhắc lịch thay vì chờ lượt chạy kế tiếp của web (mỗi 60 giây).</summary>
    [HttpPost("RunNow")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.Manager)]
    public async Task<IActionResult> RunNow([FromServices] BookingEmailDispatcher dispatcher)
    {
        try
        {
            var result = await dispatcher.SendDueRemindersAsync(HttpContext.RequestAborted);
            TempData["Success"] = result.Summary;
        }
        catch (SqlException ex)
        {
            TempData["Error"] = $"Không chạy được tác vụ nhắc lịch ({ex.Number}). {ex.Message}";
        }
        return RedirectToAction(nameof(Index));
    }
}
