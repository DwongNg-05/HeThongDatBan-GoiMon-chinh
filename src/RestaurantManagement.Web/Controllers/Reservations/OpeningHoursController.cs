using Microsoft.AspNetCore.Authorization;
using RestaurantManagement.Web.Security;
using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models;

namespace RestaurantManagement.Web.Controllers;

// S1-04 Task 4: kiểm tra quyền ở máy chủ theo vai trò (docs/S1-04-Task4.md).
[Authorize(Roles = AppRoles.Manager)]
public class OpeningHoursController(IConfiguration configuration) : Controller
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Chưa cấu hình kết nối database.");

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var model = new OpeningHoursViewModel();
        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand("SELECT DayOfWeek, IsClosed, OpensAt, ClosesAt FROM dbo.OpeningHours ORDER BY DayOfWeek; SELECT DefaultBookingMinutes FROM dbo.RestaurantSettings WHERE Id=1;", connection);
        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            model.Days.Add(new OpeningDayViewModel
            {
                DayOfWeek = reader.GetByte(0), IsClosed = reader.GetBoolean(1),
                OpensAt = reader.IsDBNull(2) ? null : TimeOnly.FromTimeSpan(reader.GetTimeSpan(2)).ToString("HH:mm"),
                ClosesAt = reader.IsDBNull(3) ? null : TimeOnly.FromTimeSpan(reader.GetTimeSpan(3)).ToString("HH:mm")
            });
        await reader.NextResultAsync();
        if (await reader.ReadAsync()) model.DefaultBookingMinutes = reader.GetInt32(0);
        await reader.CloseAsync();
        await LoadHolidays(model);
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(OpeningHoursViewModel model)
    {
        if (!ModelState.IsValid)
        {
            await LoadHolidays(model);
            return View(model);
        }
        var days = new DataTable();
        days.Columns.Add("DayOfWeek", typeof(byte));
        days.Columns.Add("IsClosed", typeof(bool));
        days.Columns.Add("OpensAt", typeof(TimeSpan));
        days.Columns.Add("ClosesAt", typeof(TimeSpan));
        foreach (var day in model.Days)
        {
            OpeningDayViewModel.TryTime(day.OpensAt, out var open);
            OpeningDayViewModel.TryTime(day.ClosesAt, out var close);
            days.Rows.Add(day.DayOfWeek, day.IsClosed, day.IsClosed ? DBNull.Value : open.ToTimeSpan(), day.IsClosed ? DBNull.Value : close.ToTimeSpan());
        }
        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await using var command = new SqlCommand("dbo.usp_SaveOpeningHours", connection) { CommandType = CommandType.StoredProcedure };
            command.Parameters.Add(new SqlParameter("@Days", SqlDbType.Structured) { TypeName = "dbo.OpeningHoursWeek", Value = days });
            command.Parameters.Add("@DefaultBookingMinutes", SqlDbType.Int).Value = model.DefaultBookingMinutes!.Value;
            // Người thực hiện là tài khoản đang đăng nhập; thủ tục SQL kiểm tra lại quyền của tài khoản này.
            command.Parameters.Add("@ActorUserId", SqlDbType.Int).Value = User.ActorUserId();
            await connection.OpenAsync();
            await command.ExecuteNonQueryAsync();
            TempData["Success"] = "Đã lưu giờ hoạt động thành công.";
            return RedirectToAction(nameof(Index));
        }
        catch (SqlException ex) when (ex.Number is 51001 or 51501 or 51502)
        {
            ModelState.AddModelError(string.Empty, ex.Number == 51001 ? "Tài khoản hiện tại không có quyền cấu hình giờ hoạt động." : ex.Message);
            await LoadHolidays(model);
            return View(model);
        }
    }

    private async Task LoadHolidays(OpeningHoursViewModel model)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await using var cmd = new SqlCommand("SELECT Id,HolidayDate,Name,IsActive FROM dbo.SpecialHolidays ORDER BY HolidayDate,Id", cn);
        await cn.OpenAsync();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) model.Holidays.Add(new SpecialHolidayViewModel
        {
            Id = reader.GetInt32(0), HolidayDate = DateOnly.FromDateTime(reader.GetDateTime(1)),
            Name = reader.GetString(2), IsActive = reader.GetBoolean(3)
        });
    }

    [HttpGet]
    public async Task<IActionResult> Calendar(DateOnly? date)
    {
        var model = new DailyBookingCalendarViewModel { Date = date };
        if (!ModelState.IsValid || date is null) return View(model);
        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand("""
            SELECT Name FROM dbo.SpecialHolidays WHERE HolidayDate=@Date AND IsActive=1;
            SELECT DayOfWeek,IsClosed,OpensAt,ClosesAt FROM dbo.OpeningHours WHERE DayOfWeek=@Day;
            """, connection);
        command.Parameters.Add("@Date", SqlDbType.Date).Value = date.Value.ToDateTime(TimeOnly.MinValue);
        command.Parameters.Add("@Day", SqlDbType.Int).Value = date.Value.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.Value.DayOfWeek;
        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync()) model.HolidayName = reader.GetString(0);
        await reader.NextResultAsync();
        if (await reader.ReadAsync()) model.WeeklyDay = new OpeningDayViewModel
        {
            DayOfWeek = reader.GetByte(0), IsClosed = reader.GetBoolean(1),
            OpensAt = reader.IsDBNull(2) ? null : TimeOnly.FromTimeSpan(reader.GetTimeSpan(2)).ToString("HH:mm"),
            ClosesAt = reader.IsDBNull(3) ? null : TimeOnly.FromTimeSpan(reader.GetTimeSpan(3)).ToString("HH:mm")
        };
        return View(model);
    }
}
