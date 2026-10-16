using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Security;

namespace RestaurantManagement.Web.Controllers;

[Authorize(Roles = AppRoles.FrontOfHouse)]
[Route("api/table-cleaning")]
public sealed class TableCleaningController(IConfiguration configuration) : ControllerBase
{
    [HttpGet("warnings")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Warnings(CancellationToken ct)
    {
        await using var cn = new SqlConnection(configuration.GetConnectionString("DefaultConnection"));
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("""
            DECLARE @now datetime2(3)=SYSUTCDATETIME();
            SELECT t.Code,t.StatusChangedAt,dbo.fn_IsCleaningOverdue(t.Status,t.StatusChangedAt,@now)
            FROM dbo.DiningTables t JOIN dbo.Areas a ON a.Id=t.AreaId
            WHERE t.IsActive=1 AND a.IsActive=1 AND t.Status='Cleaning';
            """, cn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var tables = new List<object>();
        while (await reader.ReadAsync(ct))
            tables.Add(new {
                code = reader.GetString(0),
                startedAtUtc = DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc),
                overdue = reader.GetBoolean(2)
            });
        return Ok(tables);
    }

    [HttpPost("{code}/complete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 20) return BadRequest();
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) return Forbid();
        try
        {
            await using var cn = new SqlConnection(configuration.GetConnectionString("DefaultConnection"));
            await cn.OpenAsync(ct);
            await using var cmd = new SqlCommand("dbo.usp_CompleteTableCleaning", cn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.Add("@Code", SqlDbType.VarChar, 20).Value = code.Trim();
            cmd.Parameters.Add("@ActorUserId", SqlDbType.Int).Value = actor;
            await cmd.ExecuteNonQueryAsync(ct);
            return Ok(new { message = $"Bàn {code} đã dọn xong." });
        }
        catch (SqlException e) when (e.Number is 51070 or 51071) { return Conflict(new { message = e.Message }); }
        catch (SqlException e) when (e.Number == 51001) { return Forbid(); }
        catch (SqlException e) when (e.Number is 51000 or 1205)
        { return StatusCode(503,new { message = "Bàn đang được xử lý. Vui lòng thử lại." }); }
    }
}
