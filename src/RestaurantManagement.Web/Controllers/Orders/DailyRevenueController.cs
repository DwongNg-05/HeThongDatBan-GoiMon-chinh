using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models.Reports;
using RestaurantManagement.Web.Models.Reservations;
using RestaurantManagement.Web.Security;

namespace RestaurantManagement.Web.Controllers;

/// <summary>S4-06 Task 1: five live daily overview indicators, visible only to the Manager.</summary>
[Route("Reports")]
[Authorize(Roles = AppRoles.Manager)]
public sealed class DailyRevenueController(IConfiguration configuration) : Controller
{
    [HttpGet("Daily")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Chưa cấu hình kết nối database.");
        var businessDate = DateOnly.FromDateTime(VietnamTime.Now);
        var invoices = new List<DailyRevenueInvoice>();
        var timedInvoices = new List<DailyRevenueInvoiceTime>();
        TimeOnly? opensAt = null;
        TimeOnly? closesAt = null;
        var isClosed = true;

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using (var command = new SqlCommand("""
            SELECT Status,Total,GuestCount,IssuedAt
            FROM dbo.Invoices
            WHERE BusinessDate=@BusinessDate;
            """, connection))
        {
            command.Parameters.Add("@BusinessDate", System.Data.SqlDbType.Date).Value = businessDate.ToDateTime(TimeOnly.MinValue);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                invoices.Add(new DailyRevenueInvoice(reader.GetString(0), reader.GetDecimal(1), reader.GetInt32(2)));
                timedInvoices.Add(new DailyRevenueInvoiceTime(reader.GetString(0), reader.GetDecimal(1), reader.GetDateTime(3)));
            }
        }

        await using (var command = new SqlCommand("""
            SELECT TOP (1) IsClosed,OpensAt,ClosesAt
            FROM (
                SELECT CAST(0 AS int) AS Priority,CAST(1 AS bit) AS IsClosed,CAST(NULL AS time(0)) AS OpensAt,CAST(NULL AS time(0)) AS ClosesAt
                FROM dbo.SpecialHolidays WHERE HolidayDate=@BusinessDate AND IsActive=1
                UNION ALL
                SELECT 1,IsClosed,OpensAt,ClosesAt FROM dbo.SpecialDates WHERE BusinessDate=@BusinessDate
                UNION ALL
                SELECT 2,IsClosed,OpensAt,ClosesAt FROM dbo.OpeningHours WHERE DayOfWeek=@DayOfWeek
            ) AS schedule ORDER BY Priority;
            """, connection))
        {
            command.Parameters.Add("@BusinessDate", System.Data.SqlDbType.Date).Value = businessDate.ToDateTime(TimeOnly.MinValue);
            command.Parameters.Add("@DayOfWeek", System.Data.SqlDbType.TinyInt).Value = businessDate.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)businessDate.DayOfWeek;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync())
            {
                isClosed = reader.GetBoolean(0);
                if (!reader.IsDBNull(1)) opensAt = TimeOnly.FromTimeSpan(reader.GetTimeSpan(1));
                if (!reader.IsDBNull(2)) closesAt = TimeOnly.FromTimeSpan(reader.GetTimeSpan(2));
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

        var summary = DailyRevenueSummary.Calculate(businessDate, invoices, servingTables);
        return View(summary with { HourlyRevenue = DailyRevenueSummary.BuildHourlyRevenue(businessDate, opensAt, closesAt, isClosed, timedInvoices) });
    }
}
