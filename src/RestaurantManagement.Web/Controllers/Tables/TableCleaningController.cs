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
