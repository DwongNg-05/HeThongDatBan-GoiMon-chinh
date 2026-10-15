using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models.Reports;
using RestaurantManagement.Web.Models.Reservations;
using RestaurantManagement.Web.Security;

namespace RestaurantManagement.Web.Controllers;

/// <summary>S4-06 Tasks 1–4: current revenue snapshot for a Manager-selected date range.</summary>
[Route("Reports")]
[Authorize(Roles = AppRoles.Manager)]
public sealed class DailyRevenueController(IConfiguration configuration) : Controller
{
    [HttpGet("Daily")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Index(DateOnly? fromDate, DateOnly? toDate, bool fragment = false,
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(VietnamTime.Now);
        if (!RevenueDateRange.TryCreate(fromDate, toDate, today, out var range, out var error))
        {
            var invalid = new DailyRevenuePageModel(fromDate ?? today, toDate ?? today, null, error);
            if (fragment) return BadRequest(error);
            return View(invalid);
        }

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Chưa cấu hình kết nối database.");
        var selectedRange = range!;
        var invoices = new List<DailyRevenueInvoice>();
        var timedInvoices = new List<DailyRevenueInvoiceTime>();

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using (var command = new SqlCommand("""
            SELECT Status,Total,GuestCount,IssuedAt
            FROM dbo.Invoices
            WHERE BusinessDate BETWEEN @FromDate AND @ToDate;
            """, connection))
        {
            command.Parameters.Add("@FromDate", System.Data.SqlDbType.Date).Value = selectedRange.FromDate.ToDateTime(TimeOnly.MinValue);
            command.Parameters.Add("@ToDate", System.Data.SqlDbType.Date).Value = selectedRange.ToDate.ToDateTime(TimeOnly.MinValue);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                invoices.Add(new DailyRevenueInvoice(reader.GetString(0), reader.GetDecimal(1), reader.GetInt32(2)));
                timedInvoices.Add(new DailyRevenueInvoiceTime(reader.GetString(0), reader.GetDecimal(1), reader.GetDateTime(3)));
            }
        }

        long servingTables;
        await using (var command = new SqlCommand("""
            SELECT COUNT_BIG(*)
            FROM dbo.DiningTables t
            JOIN dbo.Areas a ON a.Id=t.AreaId
            WHERE t.IsActive=1 AND a.IsActive=1 AND t.Status='Serving';
            """, connection))
            servingTables = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));

        var summary = DailyRevenueSummary.Calculate(selectedRange.FromDate, selectedRange.ToDate, invoices, servingTables);
        summary = summary with
        {
            HourlyRevenue = selectedRange.Days == 1
                ? await LoadSingleDayHours(connection, selectedRange.FromDate, timedInvoices, cancellationToken)
                : DailyRevenueSummary.BuildHourlyRevenueRange(selectedRange.FromDate, timedInvoices)
        };
        var model = new DailyRevenuePageModel(selectedRange.FromDate, selectedRange.ToDate, summary, null);
        if (fragment) return PartialView("_DailyRevenueDashboard", model);
        return View(model);
    }

    private static async Task<IReadOnlyList<HourlyRevenueSlot>> LoadSingleDayHours(SqlConnection connection,
        DateOnly date, IReadOnlyList<DailyRevenueInvoiceTime> invoices, CancellationToken cancellationToken)
    {
        TimeOnly? opensAt = null;
        TimeOnly? closesAt = null;
        var isClosed = true;
        await using var command = new SqlCommand("""
            SELECT TOP (1) IsClosed,OpensAt,ClosesAt
            FROM (
                SELECT CAST(0 AS int) AS Priority,CAST(1 AS bit) AS IsClosed,CAST(NULL AS time(0)) AS OpensAt,CAST(NULL AS time(0)) AS ClosesAt
                FROM dbo.SpecialHolidays WHERE HolidayDate=@BusinessDate AND IsActive=1
                UNION ALL
                SELECT 1,IsClosed,OpensAt,ClosesAt FROM dbo.SpecialDates WHERE BusinessDate=@BusinessDate
                UNION ALL
                SELECT 2,IsClosed,OpensAt,ClosesAt FROM dbo.OpeningHours WHERE DayOfWeek=@DayOfWeek
            ) AS schedule ORDER BY Priority;
            """, connection);
        command.Parameters.Add("@BusinessDate", System.Data.SqlDbType.Date).Value = date.ToDateTime(TimeOnly.MinValue);
        command.Parameters.Add("@DayOfWeek", System.Data.SqlDbType.TinyInt).Value = date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                isClosed = reader.GetBoolean(0);
                if (!reader.IsDBNull(1)) opensAt = TimeOnly.FromTimeSpan(reader.GetTimeSpan(1));
                if (!reader.IsDBNull(2)) closesAt = TimeOnly.FromTimeSpan(reader.GetTimeSpan(2));
            }
        }
        return DailyRevenueSummary.BuildHourlyRevenue(date, opensAt, closesAt, isClosed, invoices);
    }
}
