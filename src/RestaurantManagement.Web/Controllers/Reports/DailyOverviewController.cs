using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models.Reports;
using RestaurantManagement.Web.Security;
using RestaurantManagement.Web.Services;
using System.Globalization;
using RestaurantManagement.Web.Authentication;

namespace RestaurantManagement.Web.Controllers;

[Authorize(Roles = AppRoles.Manager)]
[Route("Reports/Today")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DailyOverviewController(DailyOverviewStore store, TimeProvider clock, ILogger<DailyOverviewController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken, string? from = null, string? to = null)
        => await LoadAsync(cancellationToken, from, to, false);

    [HttpGet("Snapshot")]
    [PassiveSessionRead]
    public async Task<IActionResult> Snapshot(CancellationToken cancellationToken, string? from = null, string? to = null)
        => await LoadAsync(cancellationToken, from, to, true);

    private async Task<IActionResult> LoadAsync(CancellationToken cancellationToken, string? from, string? to, bool snapshot)
    {
        IActionResult Render(DailyOverview model)
        {
            if (!snapshot) return View("Index", model);
            Response.Headers["X-Report-Snapshot"] = "1";
            return PartialView("_Snapshot", model);
        }
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
                return Render(new DailyOverview(start ?? DateOnly.FromDateTime(now), now, 0, 0, 0, 0, 0, [],
                    RangeEnd: end, FromInput: from ?? "", ToInput: to ?? "", ValidationError: error));
            }
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        try { return Render(await store.RangeAsync(start, end, deadline.Token)); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return Render(new DailyOverview(start ?? DateOnly.FromDateTime(now), now, 0, 0, 0, 0, 0, [],
                LoadError: "Cập nhật quá thời gian chờ. Vui lòng thử lại.", RangeEnd: end, FromInput: from, ToInput: to));
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "Không tải được tổng quan hôm nay.");
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return Render(new DailyOverview(start ?? DateOnly.FromDateTime(now), now, 0, 0, 0, 0, 0, [],
                LoadError: "Không tải được số liệu. Vui lòng thử tải lại trang.", RangeEnd: end,
                FromInput: from, ToInput: to));
        }
    }
}
