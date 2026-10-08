using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace RestaurantManagement.Web.Controllers;

[Authorize(Roles = "Manager")]
[Route("menu")]
public sealed class TemporaryOutController(IConfiguration configuration) : Controller
{
    private string ConnectionString => Environment.GetEnvironmentVariable("RM_CONNECTION_STRING")
        ?? configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Set RM_CONNECTION_STRING to connect the menu to SQL Server.");


    [HttpGet("availability")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Availability(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync(cancellationToken);
            // Include temporary out flag directly from MenuItems so it reflects recent toggles
            await using var command = new SqlCommand(
                "SELECT m.Id,c.Name AS CategoryName,m.Name,m.Price,m.Unit,m.Description,COALESCE(m.ImagePath,c.DefaultImagePath) AS ImagePath,m.IsSoldOut,m.IsTemporarilyOut " +
                "FROM dbo.MenuItems m JOIN dbo.MenuCategories c ON c.Id=m.CategoryId WHERE m.IsActive=1 AND c.IsActive=1 ORDER BY c.SortOrder,c.Name,m.SortOrder,m.Name;",
                connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var items = new List<object>();
            while (await reader.ReadAsync(cancellationToken))
                items.Add(new { id = reader.GetInt32(0), category = reader.GetString(1), name = reader.GetString(2), price = reader.GetDecimal(3), unit = reader.GetString(4), description = reader.IsDBNull(5) ? null : reader.GetString(5), imagePath = reader.IsDBNull(6) ? null : reader.GetString(6), isSoldOut = reader.GetBoolean(7), isTemporarilyOut = reader.GetBoolean(8) });
            return Json(items);
        }
        catch (InvalidOperationException ex) { return Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable); }
        catch (SqlException) { return Problem("Không thể tải thực đơn lúc này.", statusCode: StatusCodes.Status503ServiceUnavailable); }
    }

    [Authorize(Roles = "Manager")]
    [HttpPost("{id:int}/availability")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetAvailability(int id, [FromForm] bool isSoldOut, CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId))
            return Forbid();

        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand("dbo.usp_SetMenuAvailability", connection) { CommandType = CommandType.StoredProcedure };
            command.Parameters.Add("@MenuItemId", SqlDbType.Int).Value = id;
            command.Parameters.Add("@IsSoldOut", SqlDbType.Bit).Value = isSoldOut;
            command.Parameters.Add("@ActorUserId", SqlDbType.Int).Value = actorUserId;
            await command.ExecuteNonQueryAsync(cancellationToken);
            return Ok(new { id, isSoldOut });
        }
        catch (SqlException ex) when (ex.Number == 51001) { return Forbid(); }
        catch (SqlException ex) when (ex.Number == 51040) { return NotFound(); }
        catch (SqlException) { return Problem("Không thể cập nhật trạng thái món lúc này.", statusCode: StatusCodes.Status503ServiceUnavailable); }
        catch (InvalidOperationException ex) { return Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable); }
    }

    [Authorize(Roles = "Manager")]
    [HttpPost("{id:int}/temporarily-out")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetTemporarilyOut(int id, [FromForm] bool isTemporarilyOut, CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId))
            return Forbid();

        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand("dbo.usp_SetMenuTemporarilyOut", connection) { CommandType = CommandType.StoredProcedure };
            command.Parameters.Add("@MenuItemId", SqlDbType.Int).Value = id;
            command.Parameters.Add("@IsTemporarilyOut", SqlDbType.Bit).Value = isTemporarilyOut;
            command.Parameters.Add("@ActorUserId", SqlDbType.Int).Value = actorUserId;
            await command.ExecuteNonQueryAsync(cancellationToken);
            return Ok(new { id, isTemporarilyOut });
        }
        catch (SqlException ex) when (ex.Number == 51001) { return Forbid(); }
        catch (SqlException ex) when (ex.Number == 51040) { return NotFound(); }
        catch (SqlException) { return Problem("Không thể cập nhật trạng thái món tạm hết lúc này.", statusCode: StatusCodes.Status503ServiceUnavailable); }
        catch (InvalidOperationException ex) { return Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable); }
    }

    [Authorize(Roles = "Manager")]
    [HttpGet("{id:int}/temporarily-history")]
    public async Task<IActionResult> TemporarilyHistory(int id, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(@"SELECT e.Id,e.MenuItemId,m.Name,e.OldIsTemporarilyOut,e.IsTemporarilyOut,e.ChangedBy,e.ChangedAt,u.FullName AS ChangedByName
FROM dbo.MenuTemporaryOutEvents e
JOIN dbo.MenuItems m ON m.Id=e.MenuItemId
LEFT JOIN dbo.Users u ON u.Id=e.ChangedBy
WHERE e.MenuItemId=@MenuItemId ORDER BY e.ChangedAt DESC,e.Id DESC;", connection);
            command.Parameters.Add("@MenuItemId", SqlDbType.Int).Value = id;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var rows = new List<object>();
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new
                {
                    id = reader.GetInt64(0),
                    menuItemId = reader.GetInt32(1),
                    menuItemName = reader.GetString(2),
                    oldState = reader.IsDBNull(3) ? (bool?)null : reader.GetBoolean(3),
                    newState = reader.GetBoolean(4),
                    changedBy = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5),
                    changedAt = DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc),
                    changedByName = reader.IsDBNull(7) ? null : reader.GetString(7)
                });
            }
            return Json(rows);
        }
        catch (SqlException) { return Problem("Không thể tải lịch sử tạm hết lúc này.", statusCode: StatusCodes.Status503ServiceUnavailable); }
    }
}
