using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models.Reports;
using RestaurantManagement.Web.Security;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Controllers;

/// <summary>S4-06: Quản lý xem món bán chạy, món hủy nhiều và xuất CSV; dữ liệu chỉ đọc.</summary>
[Authorize(Roles = AppRoles.Manager)]
[Route("Reports/MenuPerformance")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MenuPerformanceController(MenuPerformanceStore store, TimeProvider clock, ILogger<MenuPerformanceController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken, string? from = null, string? to = null)
    {
        var page = await LoadAsync(cancellationToken, from, to);
        if (page.Error is not null && page.Report is null)
            Response.StatusCode = page.Error.StartsWith("Vui lòng", StringComparison.Ordinal) || page.Error.StartsWith("Ngày", StringComparison.Ordinal)
                ? StatusCodes.Status400BadRequest : StatusCodes.Status503ServiceUnavailable;
        return View(page);
    }

    [HttpGet("Export")]
    public async Task<IActionResult> Export(CancellationToken cancellationToken, string? from = null, string? to = null)
    {
        var page = await LoadAsync(cancellationToken, from, to);
        if (page.Report is null) return BadRequest(new { message = page.Error });
        var report = page.Report;
        var csv = new StringBuilder("\uFEFFNhóm;Mã món;Tên món;Trạng thái;Số lượng;Doanh thu gộp;Lý do hủy phổ biến\r\n");
        AppendRows(csv, "Bán chạy theo số lượng", report.BestByQuantity.Select(x => (x.MenuItemId, x.Name, x.IsActive ? "Đang bán" : "Ngừng bán", x.Quantity, x.GrossRevenue, "")));
        AppendRows(csv, "Bán chạy theo doanh thu", report.BestByRevenue.Select(x => (x.MenuItemId, x.Name, x.IsActive ? "Đang bán" : "Ngừng bán", x.Quantity, x.GrossRevenue, "")));
        AppendRows(csv, "Hủy nhiều", report.MostCancelled.Select(x => (x.MenuItemId, x.Name, "Không áp dụng", x.Quantity, 0m, x.ReasonLabel)));
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8",
            $"bao-cao-mon-{report.From:yyyyMMdd}-{report.To:yyyyMMdd}.csv");
    }

    private async Task<MenuPerformancePage> LoadAsync(CancellationToken cancellationToken, string? fromInput, string? toInput)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime);
        var from = today;
        var to = today;
        var requested = Request.Query.ContainsKey("from") || Request.Query.ContainsKey("to");
        if (requested)
        {
            if (!DateOnly.TryParseExact(fromInput, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out from) ||
                !DateOnly.TryParseExact(toInput, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out to))
                return new MenuPerformancePage(null, fromInput ?? "", toInput ?? "", "Vui lòng chọn đầy đủ ngày bắt đầu và ngày kết thúc.");
            if (ReportRangeRules.Validate(from, to) is { } rangeError)
                return new MenuPerformancePage(null, fromInput ?? "", toInput ?? "", rangeError);
        }

        try
        {
            var report = await store.GetAsync(from, to, User.ActorUserId(), cancellationToken);
            return new MenuPerformancePage(report, from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }
        catch (SqlException ex) when (ex.Number == 51001)
        {
            return new MenuPerformancePage(null, from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                "Tài khoản không có quyền xem báo cáo này.");
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "Không tải được báo cáo món.");
            return new MenuPerformancePage(null, from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                "Không tải được báo cáo món. Vui lòng kiểm tra database đã chạy migration 004_ViewsAndGuards.sql rồi thử lại.");
        }
    }

    private static void AppendRows(StringBuilder csv, string group,
        IEnumerable<(int Id, string Name, string Status, long Quantity, decimal Revenue, string Reason)> rows)
    {
        foreach (var row in rows)
            csv.Append(Csv(group)).Append(';').Append(row.Id).Append(';').Append(Csv(row.Name)).Append(';')
                .Append(Csv(row.Status)).Append(';').Append(row.Quantity).Append(';')
                .Append(row.Revenue.ToString("0.##", CultureInfo.InvariantCulture)).Append(';').Append(Csv(row.Reason)).Append("\r\n");
    }

    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
}
