using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Authentication;
using RestaurantManagement.Web.Models.Reservations;
using RestaurantManagement.Web.Security;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Controllers;

[Authorize(Roles = AppRoles.FrontOfHouse)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class NoShowController(IConfiguration configuration) : Controller
{
    private SqlConnection Connect() => new(configuration.GetConnectionString("DefaultConnection"));

    [HttpGet, PassiveSessionRead]
    public async Task<IActionResult> Alerts(CancellationToken ct)
    {
        await using var cn = Connect();
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT Id,Code,CustomerName,StartsAt,TableCode FROM dbo.v_NoShowAlerts ORDER BY StartsAt,Id", cn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var items = new List<object>();
        while (await r.ReadAsync(ct)) items.Add(new {
            id = r.GetInt64(0), code = r.GetString(1), customerName = r.GetString(2),
            appointment = VietnamTime.FromUtc(r.GetDateTime(3)).ToString("dd/MM/yyyy HH:mm"),
            tableCode = r.IsDBNull(4) ? "Chưa xếp bàn" : r.GetString(4)
        });
        return Json(items);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Mark(long id, CancellationToken ct)
    {
        try
        {
            await using var cn = Connect();
            await cn.OpenAsync(ct);
            await using var cmd = new SqlCommand("dbo.usp_RecordReservationNoShow", cn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.Add("@ReservationId", SqlDbType.BigInt).Value = id;
            cmd.Parameters.Add("@ActorUserId", SqlDbType.Int).Value = User.ActorUserId();
            await cmd.ExecuteNonQueryAsync(ct);
            return Json(new { message = "Đã ghi nhận khách không tới, giải phóng lượt giữ bàn và lưu lịch sử." });
        }
        catch (SqlException e) when (e.Number is 51064 or 51065)
        { return Conflict(new { message = e.Message }); }
        catch (SqlException e) when (e.Number == 51001) { return Forbid(); }
        catch (SqlException e) when (e.Number is 51000 or 1205)
        { return StatusCode(503, new { message = "Hệ thống đang xử lý thao tác khác. Vui lòng làm mới và thử lại." }); }
    }

    [HttpGet]
    public Task<IActionResult> History(long id, CancellationToken ct) => ReadHistory(id, null, ct);

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> HistoryByPhone(string? phone, CancellationToken ct)
    {
        var normalized = ReservationPhoneNormalizer.Normalize(phone);
        return normalized is null
            ? Task.FromResult<IActionResult>(BadRequest(new { message = "Vui lòng nhập số điện thoại Việt Nam hợp lệ." }))
            : ReadHistory(0, normalized, ct);
    }

    private async Task<IActionResult> ReadHistory(long id, string? phone, CancellationToken ct)
    {
        await using var cn = Connect();
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("""
            SELECT h.ReservationCode,h.AppointmentAt,h.NoShowAt
            FROM dbo.ReservationNoShowHistory h
            WHERE h.NormalizedPhone=COALESCE(@phone,(SELECT dbo.fn_NoShowPhone(Phone) FROM dbo.Reservations WHERE Id=@id))
            ORDER BY h.NoShowAt DESC,h.Id DESC;
            """, cn);
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
        cmd.Parameters.Add("@phone", SqlDbType.NVarChar, 100).Value = (object?)phone ?? DBNull.Value;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var items = new List<object>();
        while (await r.ReadAsync(ct)) items.Add(new {
            code = r.GetString(0), appointment = VietnamTime.FromUtc(r.GetDateTime(1)).ToString("dd/MM/yyyy HH:mm"),
            recordedAt = VietnamTime.FromUtc(r.GetDateTime(2)).ToString("dd/MM/yyyy HH:mm:ss")
        });
        return Json(items);
    }
}
