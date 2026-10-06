using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Authentication;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Controllers;

[Authorize(Roles = "Manager,Waiter")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class TableMapController(ITableMapReader reader, ILogger<TableMapController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        try { return View(await reader.ReadAsync(cancellationToken)); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unable to load table map");
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return View(new TableMapSnapshot([], LoadFailed: true));
        }
    }

    [HttpGet("api/table-map/snapshot")]
    [PassiveSessionRead]
    public async Task<IActionResult> Snapshot(CancellationToken cancellationToken)
    {
        try { return Ok(await reader.ReadAsync(cancellationToken)); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unable to load table-map snapshot");
            return StatusCode(503, new { message = "Không tải được sơ đồ bàn. Vui lòng thử lại." });
        }
    }

    [HttpGet("api/table-map/changes")]
    [PassiveSessionRead]
    public async Task<IActionResult> Changes(string? after, CancellationToken cancellationToken)
    {
        if (!long.TryParse(after, out var cursor) || cursor < 0)
            return BadRequest(new { message = "Mốc đồng bộ không hợp lệ." });
        try { return Ok(await reader.ReadChangesAsync(cursor, cancellationToken)); }
        catch (ArgumentOutOfRangeException) { return Conflict(new { message = "Dữ liệu đã được đặt lại. Vui lòng tải lại sơ đồ." }); }
        catch (Microsoft.Data.SqlClient.SqlException exception) when (exception.Number == 52001)
        { return Conflict(new { message = "Dữ liệu đã được đặt lại. Vui lòng tải lại sơ đồ." }); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unable to load table-map changes");
            return StatusCode(503, new { message = "Không đồng bộ được sơ đồ. Dữ liệu có thể đã cũ." });
        }
    }
}
