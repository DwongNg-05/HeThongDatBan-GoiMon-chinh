using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models.Cashier;
using RestaurantManagement.Web.Models.Reservations;
using RestaurantManagement.Web.Security;

namespace RestaurantManagement.Web.Controllers;

[Route("Cashier/ShiftManagement")]
[Authorize(Roles = AppRoles.Manager)]
public sealed class ShiftManagementController(
    IConfiguration configuration) : Controller
{
    private string ConnectionString =>
        Environment.GetEnvironmentVariable("RM_CONNECTION_STRING")
        ?? configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException(
            "Chưa cấu hình kết nối database.");

    [HttpGet("")]
    public async Task<IActionResult> Index(
        long? shiftId,
        CancellationToken cancellationToken)
    {
        Response.Headers["Cache-Control"] = "no-store";

        if (shiftId is <= 0)
        {
            return BadRequest("Mã ca không hợp lệ.");
        }

        await using var connection =
            new SqlConnection(ConnectionString);

        await connection.OpenAsync(cancellationToken);

        var shifts = new List<ManagedShiftRow>();

        await using (var command = new SqlCommand("""
            SELECT TOP (50)
                Id,
                Name,
                BusinessDate,
                Status,
                OpenedAt,
                ClosedAt
            FROM dbo.Shifts
            ORDER BY OpenedAt DESC, Id DESC;
            """, connection))
        {
            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                shifts.Add(new ManagedShiftRow(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetDateTime(2),
                    reader.GetString(3),
                    VietnamTime.FromUtc(reader.GetDateTime(4)),
                    reader.IsDBNull(5)
                        ? null
                        : VietnamTime.FromUtc(reader.GetDateTime(5))));
            }
        }

        bool hasOpenShift;

        await using (var command = new SqlCommand("""
            SELECT CAST(
                CASE WHEN EXISTS (
                    SELECT 1
                    FROM dbo.Shifts
                    WHERE Status = 'Open'
                ) THEN 1 ELSE 0 END
            AS bit);
            """, connection))
        {
            hasOpenShift =
                (bool)(await command.ExecuteScalarAsync(
                    cancellationToken))!;
        }

        var selectedShiftId =
            shiftId ?? shifts.FirstOrDefault()?.Id;

        if (selectedShiftId.HasValue)
        {
            await using var existsCommand = new SqlCommand("""
                SELECT COUNT(*)
                FROM dbo.Shifts
                WHERE Id = @ShiftId;
                """, connection);

            existsCommand.Parameters.Add(
                "@ShiftId", SqlDbType.BigInt)
                .Value = selectedShiftId.Value;

            var exists = Convert.ToInt32(
                await existsCommand.ExecuteScalarAsync(
                    cancellationToken));

            if (exists == 0)
            {
                return NotFound("Không tìm thấy ca.");
            }
        }

        var history = new List<ShiftHistoryRow>();

        if (selectedShiftId.HasValue)
        {
            await using var command = new SqlCommand("""
                SELECT
                    Id,
                    ShiftId,
                    Action,
                    ActorUserId,
                    Reason,
                    OccurredAt
                FROM dbo.ShiftEvents
                WHERE ShiftId = @ShiftId
                ORDER BY OccurredAt DESC, Id DESC;
                """, connection);

            command.Parameters.Add(
                "@ShiftId", SqlDbType.BigInt)
                .Value = selectedShiftId.Value;

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                history.Add(new ShiftHistoryRow(
                    reader.GetInt64(0),
                    reader.GetInt64(1),
                    reader.GetString(2),
                    reader.GetInt32(3),
                    reader.IsDBNull(4)
                        ? null
                        : reader.GetString(4),
                    VietnamTime.FromUtc(reader.GetDateTime(5))));
            }
        }

        return View(new ShiftManagementViewModel
        {
            Shifts = shifts,
            History = history,
            SelectedShiftId = selectedShiftId,
            HasOpenShift = hasOpenShift
        });
    }

    [HttpPost("Reopen")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reopen(
        long shiftId,
        string? reason,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || shiftId <= 0)
        {
            TempData["Error"] = "Thông tin ca không hợp lệ.";
            return RedirectToAction(nameof(Index));
        }

        reason = reason?.Trim();

        if (string.IsNullOrWhiteSpace(reason))
        {
            TempData["Error"] = "Phải nhập lý do mở lại ca.";

            return RedirectToAction(
                nameof(Index), new { shiftId });
        }

        if (reason.Length > 500)
        {
            TempData["Error"] =
                "Lý do mở lại ca không được vượt quá 500 ký tự.";

            return RedirectToAction(
                nameof(Index), new { shiftId });
        }

        try
        {
            await using var connection =
                new SqlConnection(ConnectionString);

            await using var command = new SqlCommand(
                "dbo.usp_ReopenShift", connection)
            {
                CommandType = CommandType.StoredProcedure
            };

            command.Parameters.Add(
                "@ShiftId", SqlDbType.BigInt).Value = shiftId;

            command.Parameters.Add(
                "@Reason", SqlDbType.NVarChar, 500).Value = reason;

            command.Parameters.Add(
                "@ActorUserId", SqlDbType.Int)
                .Value = User.ActorUserId();

            await connection.OpenAsync(cancellationToken);
            await command.ExecuteNonQueryAsync(cancellationToken);

            TempData["Success"] =
                $"Đã mở lại ca #{shiftId}. "
                + "Lý do và người thực hiện đã được lưu vào lịch sử.";
        }
        catch (SqlException ex)
            when (ex.Number is >= 51000 and < 51500)
        {
            TempData["Error"] = ex.Number == 51001
                ? "Tài khoản không có quyền mở lại ca."
                : ex.Message;
        }

        return RedirectToAction(
            nameof(Index), new { shiftId });
    }
}