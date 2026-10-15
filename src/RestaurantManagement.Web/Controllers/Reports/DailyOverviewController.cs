using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models.Reports;
using RestaurantManagement.Web.Security;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Controllers;

[Authorize(Roles = AppRoles.Manager)]
[Route("Reports/Today")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DailyOverviewController(DailyOverviewStore store, TimeProvider clock, ILogger<DailyOverviewController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        try { return View(await store.TodayAsync(cancellationToken)); }
        catch (SqlException ex)
        {
            logger.LogError(ex, "Không tải được tổng quan hôm nay.");
            var now = clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime;
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return View(new DailyOverview(DateOnly.FromDateTime(now), now, 0, 0, 0, 0, 0, [],
                LoadError: "Không tải được số liệu. Vui lòng thử tải lại trang."));
        }
    }
}
