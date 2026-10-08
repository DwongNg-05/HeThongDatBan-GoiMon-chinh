using Microsoft.AspNetCore.Authorization;
using RestaurantManagement.Web.Security;
using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models;

namespace RestaurantManagement.Web.Controllers;

// S1-04 Task 4: kiểm tra quyền ở máy chủ theo vai trò (docs/S1-04-Task4.md).
[Authorize(Roles = AppRoles.Manager)]
public class SpecialHolidaysController(IConfiguration configuration) : Controller
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Chưa cấu hình kết nối database.");
    private int Actor => User.ActorUserId();

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var rows = new List<SpecialHolidayViewModel>();
        await using var cn = new SqlConnection(ConnectionString);
        await using var cmd = new SqlCommand("SELECT Id,HolidayDate,Name,IsActive FROM dbo.SpecialHolidays ORDER BY HolidayDate,Id", cn);
        await cn.OpenAsync();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) rows.Add(Read(reader));
        return View(rows);
    }

    [HttpGet]
    public IActionResult Create() => View("Edit", new SpecialHolidayViewModel());

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var model = await Find(id);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Create(SpecialHolidayViewModel model)
    {
        model.Id = null;
        return Save(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Edit(int id, SpecialHolidayViewModel model)
    {
        model.Id = id;
        return Save(model);
    }

    private async Task<IActionResult> Save(SpecialHolidayViewModel model)
    {
        if (!ModelState.IsValid) return View("Edit", model);
        try
        {
            await using var cn = new SqlConnection(ConnectionString);
            await using var cmd = new SqlCommand("dbo.usp_SaveSpecialHoliday", cn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.Add("@ActorUserId", SqlDbType.Int).Value = Actor;
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = (object?)model.Id ?? DBNull.Value;
            cmd.Parameters.Add("@HolidayDate", SqlDbType.Date).Value = model.HolidayDate!.Value.ToDateTime(TimeOnly.MinValue);
            cmd.Parameters.Add("@Name", SqlDbType.NVarChar, -1).Value = model.Name.Trim();
            cmd.Parameters.Add("@IsActive", SqlDbType.Bit).Value = model.IsActive;
            await cn.OpenAsync(); await cmd.ExecuteNonQueryAsync();
            TempData["Success"] = "Đã lưu ngày nghỉ đặc biệt.";
            return RedirectToAction(nameof(Index));
        }
        catch (SqlException ex) when (ex.Number == 51514) { return NotFound(); }
        catch (SqlException ex) when (ex.Number is 51001 or 51511 or 51512 or 2601 or 2627)
        {
            ModelState.AddModelError(ex.Number is 51512 or 2601 or 2627 ? nameof(model.HolidayDate) : "",
                ex.Number == 51001 ? "Tài khoản hiện tại không có quyền quản lý ngày nghỉ." : ex.Number is 51512 or 2601 or 2627 ? "Ngày nghỉ này đã tồn tại." : ex.Message);
            return View("Edit", model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Delete(int id)
    {
        var model = await Find(id);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmDelete(int id)
    {
        try
        {
            await using var cn = new SqlConnection(ConnectionString);
            await using var cmd = new SqlCommand("dbo.usp_DeleteSpecialHoliday", cn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.Add("@ActorUserId", SqlDbType.Int).Value = Actor;
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
            await cn.OpenAsync(); await cmd.ExecuteNonQueryAsync();
            TempData["Success"] = "Đã xóa ngày nghỉ đặc biệt.";
        }
        catch (SqlException ex) when (ex.Number == 51514) { return NotFound(); }
        catch (SqlException ex) when (ex.Number == 51001) { TempData["Error"] = "Tài khoản hiện tại không có quyền quản lý ngày nghỉ."; }
        return RedirectToAction(nameof(Index));
    }

    private async Task<SpecialHolidayViewModel?> Find(int id)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await using var cmd = new SqlCommand("SELECT Id,HolidayDate,Name,IsActive FROM dbo.SpecialHolidays WHERE Id=@Id", cn);
        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
        await cn.OpenAsync(); await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Read(reader) : null;
    }

    private static SpecialHolidayViewModel Read(SqlDataReader reader) => new()
    {
        Id = reader.GetInt32(0), HolidayDate = DateOnly.FromDateTime(reader.GetDateTime(1)),
        Name = reader.GetString(2), IsActive = reader.GetBoolean(3)
    };
}
