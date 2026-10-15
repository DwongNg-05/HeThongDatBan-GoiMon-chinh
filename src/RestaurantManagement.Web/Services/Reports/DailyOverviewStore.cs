using System.Data;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models.Reports;

namespace RestaurantManagement.Web.Services;

public sealed class DailyOverviewStore(IConfiguration configuration, TimeProvider clock)
{
    public async Task<DailyOverview> TodayAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime;
        var today = DateOnly.FromDateTime(now);
        await using var cn = new SqlConnection(configuration.GetConnectionString("DefaultConnection"));
        await cn.OpenAsync(cancellationToken);
        var yesterdayHours = await HoursAsync(cn, today.AddDays(-1), cancellationToken);
        var date = DailyOverviewRules.BusinessDate(now, yesterdayHours);
        var hours = date == yesterdayHours.Date ? yesterdayHours : await HoursAsync(cn, date, cancellationToken);
        await using var command = new SqlCommand("""
            SELECT p.ReceivedAt, i.Total, i.GuestCount
            FROM dbo.Invoices i JOIN dbo.Payments p ON p.InvoiceId=i.Id
            WHERE i.Status='Paid' AND p.ReceivedAt>=@Start AND p.ReceivedAt<@End;
            SELECT COUNT(*) FROM dbo.DiningTables WHERE Status='Serving';
            """, cn);
        command.Parameters.Add("@Start", SqlDbType.DateTime2).Value = hours.Start.AddHours(-7);
        command.Parameters.Add("@End", SqlDbType.DateTime2).Value = hours.Closed ? hours.Start.AddHours(-7) : hours.End.AddHours(-7);
        var invoices = new List<ReportInvoice>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            invoices.Add(new(reader.GetDateTime(0).AddHours(7), reader.GetDecimal(1), reader.GetInt32(2)));
        await reader.NextResultAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return DailyOverviewRules.Calculate(hours, now, invoices, reader.GetInt32(0));
    }

    private static async Task<ReportHours> HoursAsync(SqlConnection cn, DateOnly date, CancellationToken cancellationToken)
    {
        await using var cmd = new SqlCommand("""
            SELECT TOP(1) IsClosed,OpensAt,ClosesAt FROM (
                SELECT 0 AS Priority,IsClosed,OpensAt,ClosesAt FROM dbo.SpecialDates WHERE BusinessDate=@Date
                UNION ALL
                SELECT 1,IsClosed,OpensAt,ClosesAt FROM dbo.OpeningHours WHERE DayOfWeek=@Day
            ) schedule ORDER BY Priority;
            SELECT COUNT(*) FROM dbo.SpecialHolidays WHERE HolidayDate=@Date AND IsActive=1;
            """, cn);
        cmd.Parameters.Add("@Date", SqlDbType.Date).Value = date.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("@Day", SqlDbType.Int).Value = ((int)date.DayOfWeek + 6) % 7 + 1;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var result = new ReportHours(date, default, default, true);
        if (await reader.ReadAsync(cancellationToken) && !reader.GetBoolean(0) && !reader.IsDBNull(1) && !reader.IsDBNull(2))
            result = new(date, TimeOnly.FromTimeSpan(reader.GetTimeSpan(1)), TimeOnly.FromTimeSpan(reader.GetTimeSpan(2)), false);
        await reader.NextResultAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return reader.GetInt32(0) > 0 ? result with { Closed = true } : result;
    }
}
