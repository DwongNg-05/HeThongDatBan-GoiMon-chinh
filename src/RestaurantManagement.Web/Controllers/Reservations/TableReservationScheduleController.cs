using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models.Reservations;

namespace RestaurantManagement.Web.Controllers;

[Authorize(Roles = "Manager")]
[Route("TableReservations")]
public sealed class TableReservationScheduleController(IConfiguration configuration) : Controller
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Chưa cấu hình ConnectionStrings:DefaultConnection.");

    [HttpGet("")]
    public async Task<IActionResult> Index(DateOnly? date)
    {
        var selectedDate = date ?? DateOnly.FromDateTime(VietnamTime.Now);
        var model = new TableReservationScheduleViewModel { Date = selectedDate };
        var byId = new Dictionary<int, TableReservationScheduleRowViewModel>();
        var dayStart = VietnamTime.ToUtc(selectedDate.ToDateTime(TimeOnly.MinValue));
        var dayEnd = VietnamTime.ToUtc(selectedDate.AddDays(1).ToDateTime(TimeOnly.MinValue));

        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand("""
            SELECT t.Id, t.Code, a.Name, t.MaxCapacity
            FROM dbo.DiningTables t JOIN dbo.Areas a ON a.Id=t.AreaId
            WHERE t.IsActive=1 AND a.IsActive=1
            ORDER BY a.SortOrder, t.SortOrder, t.Code;

            SELECT r.Id, r.Code, r.TableId, r.CustomerName, r.Phone, r.StartsAt, r.EndsAt, r.Status
            FROM dbo.Reservations r
            WHERE r.TableId IS NOT NULL
              AND r.StartsAt < @DayEnd
              AND DATEADD(minute, 15, r.EndsAt) > @DayStart
            ORDER BY r.TableId, r.StartsAt, r.Id;
            """, connection);
        command.Parameters.Add("@DayStart", SqlDbType.DateTime2).Value = dayStart;
        command.Parameters.Add("@DayEnd", SqlDbType.DateTime2).Value = dayEnd;
        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new TableReservationScheduleRowViewModel
            {
                TableId = reader.GetInt32(0),
                TableCode = reader.GetString(1),
                AreaName = reader.GetString(2),
                MaxCapacity = reader.GetInt32(3)
            };
            model.Tables.Add(row);
            byId[row.TableId] = row;
        }

        await reader.NextResultAsync();
        while (await reader.ReadAsync())
        {
            var tableId = reader.GetInt32(2);
            if (!byId.TryGetValue(tableId, out var table)) continue;
            table.Reservations.Add(new TableReservationScheduleItemViewModel
            {
                Id = reader.GetInt64(0),
                Code = reader.GetString(1),
                CustomerName = reader.GetString(3),
                Phone = reader.GetString(4),
                StartsAt = VietnamTime.FromUtc(reader.GetDateTime(5)),
                EndsAt = VietnamTime.FromUtc(reader.GetDateTime(6)),
                Status = reader.GetString(7)
            });
        }
        return View(model);
    }

    [HttpGet("Create")]
    public async Task<IActionResult> Create(int? tableId, DateOnly? date)
    {
        var model = new ManagedTableReservationCreateViewModel
        {
            TableId = tableId,
            ReservationDate = date ?? DateOnly.FromDateTime(VietnamTime.Now),
            StartTime = new TimeOnly(19, 0)
        };
        await LoadTablesAsync(model);
        return View(model);
    }

    [HttpPost("Create"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ManagedTableReservationCreateViewModel model)
    {
        Normalize(model);
        await LoadTablesAsync(model);
        if (!ModelState.IsValid) return View(model);

        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId)) return Forbid();
        try
        {
            var startsAtUtc = VietnamTime.ToUtc(model.ReservationDate!.Value.ToDateTime(model.StartTime!.Value));
            await using var connection = new SqlConnection(ConnectionString);
            await using var command = new SqlCommand("dbo.usp_CreateManagedTableReservation", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            command.Parameters.Add("@TableId", SqlDbType.Int).Value = model.TableId!.Value;
            command.Parameters.Add("@StartsAt", SqlDbType.DateTime2).Value = startsAtUtc;
            command.Parameters.Add("@CustomerName", SqlDbType.NVarChar, 100).Value = model.CustomerName;
            command.Parameters.Add("@Phone", SqlDbType.VarChar, 10).Value = model.Phone;
            command.Parameters.Add("@InitialStatus", SqlDbType.VarChar, 20).Value = model.InitialStatus;
            command.Parameters.Add("@ActorUserId", SqlDbType.Int).Value = actorUserId;
            await connection.OpenAsync();
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) throw new InvalidOperationException("Không nhận được kết quả khi tạo lượt đặt.");
            TempData["Success"] = $"Đã tạo lượt đặt {reader.GetString(reader.GetOrdinal("Code"))}.";
            return RedirectToAction(nameof(Index), new { date = model.ReservationDate.Value.ToString("yyyy-MM-dd") });
        }
        catch (SqlException ex) when (ex.Number == 51060)
        {
            ModelState.AddModelError(string.Empty, "Bàn vừa có người đặt trong khung giờ này. Vui lòng chọn giờ khác.");
            return View(model);
        }
        catch (SqlException ex) when (ex.Number is 51002 or 51061 or 51062 or 51063)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }
    }

    private async Task LoadTablesAsync(ManagedTableReservationCreateViewModel model)
    {
        const string sql = """
            SELECT t.Id, t.Code, a.Name, t.MaxCapacity
            FROM dbo.DiningTables t JOIN dbo.Areas a ON a.Id=t.AreaId
            WHERE t.IsActive=1 AND a.IsActive=1
            ORDER BY a.SortOrder, t.SortOrder, t.Code;
            """;
        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand(sql, connection);
        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            model.Tables.Add(new ManagedTableOptionViewModel
            {
                Id = reader.GetInt32(0), Code = reader.GetString(1),
                AreaName = reader.GetString(2), MaxCapacity = reader.GetInt32(3)
            });
    }

    private static void Normalize(ManagedTableReservationCreateViewModel model)
    {
        model.CustomerName = model.CustomerName?.Trim() ?? string.Empty;
        model.Phone = model.Phone?.Trim() ?? string.Empty;
    }
}
