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

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using (var command = new SqlCommand("""
            SELECT Status,Total,GuestCount
            FROM dbo.Invoices
            WHERE BusinessDate=@BusinessDate;
            """, connection))
        {
            command.Parameters.Add("@BusinessDate", System.Data.SqlDbType.Date).Value = businessDate.ToDateTime(TimeOnly.MinValue);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                invoices.Add(new DailyRevenueInvoice(reader.GetString(0), reader.GetDecimal(1), reader.GetInt32(2)));
        }

        long servingTables;
        await using (var command = new SqlCommand("""
            SELECT COUNT_BIG(*)
            FROM dbo.DiningTables t
            JOIN dbo.Areas a ON a.Id=t.AreaId
            WHERE t.IsActive=1 AND a.IsActive=1 AND t.Status='Serving';
            """, connection))
            servingTables = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));

        return View(DailyRevenueSummary.Calculate(businessDate, invoices, servingTables));
    }
}
