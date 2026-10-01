using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Controllers;

[Authorize(Roles = "Waiter")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class TableMapController(ITableMapReader reader, ILogger<TableMapController> logger) : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        try { return View(reader.Read()); }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unable to load table map");
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return View(new TableMapSnapshot([], LoadFailed: true));
        }
    }

    [HttpGet("api/table-map")]
    public IActionResult Snapshot()
    {
        try { return Ok(reader.Read()); }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unable to load table map snapshot");
            return StatusCode(503, new { message = "Không tải được sơ đồ bàn. Vui lòng thử lại." });
        }
    }
}
