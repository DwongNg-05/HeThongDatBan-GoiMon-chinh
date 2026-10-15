using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models.Reports;
using RestaurantManagement.Web.Security;
using RestaurantManagement.Web.Services;
using System.Globalization;

namespace RestaurantManagement.Web.Controllers;

[Authorize(Roles = AppRoles.Manager)]
[Route("Reports/Today")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DailyOverviewController(DailyOverviewStore store, TimeProvider clock, ILogger<DailyOverviewController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken, string? from = null, string? to = null)
    {
        var now = clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime;
        DateOnly? start = null, end = null;
        var requested = Request.Query.ContainsKey("from") || Request.Query.ContainsKey("to");
        if (requested)
        {
            if (DateOnly.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedStart)) start = parsedStart;
            if (DateOnly.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedEnd)) end = parsedEnd;
            var error = ReportRangeRules.Validate(start, end);
            if (error is not null)
            {
                Response.StatusCode = StatusCodes.Status400BadRequest;
                return View(new DailyOverview(start ?? DateOnly.FromDateTime(now), now, 0, 0, 0, 0, 0, [],
                    RangeEnd: end, FromInput: from ?? "", ToInput: to ?? "", ValidationError: error));
            }
        }
        try { return View(await store.RangeAsync(start, end, cancellationToken)); }
        catch (SqlException ex)
        {
            logger.LogError(ex, "Không tải được tổng quan hôm nay.");
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return View(new DailyOverview(start ?? DateOnly.FromDateTime(now), now, 0, 0, 0, 0, 0, [],
                LoadError: "Không tải được số liệu. Vui lòng thử tải lại trang.", RangeEnd: end,
                FromInput: from, ToInput: to));
        }
    }
}
