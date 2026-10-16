using System.Data;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models.Reports;

namespace RestaurantManagement.Web.Services;

public sealed class DailyOverviewStore(IConfiguration configuration, TimeProvider clock)
{
    public async Task<DailyOverview> TodayAsync(CancellationToken cancellationToken)
        => await RangeAsync(null, null, cancellationToken);

    public async Task<DailyOverview> RangeAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        if ((from is not null || to is not null) && ReportRangeRules.Validate(from, to) is { } error)
            throw new ArgumentException(error);
        var now = clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime;
        var today = DateOnly.FromDateTime(now);
        await using var cn = new SqlConnection(configuration.GetConnectionString("DefaultConnection"));
        await cn.OpenAsync(cancellationToken);
        if (from is null)
        {
            var yesterdayHours = await HoursAsync(cn, today.AddDays(-1), cancellationToken);
            from = to = DailyOverviewRules.BusinessDate(now, yesterdayHours);
        }
        var schedules = await RangeHoursAsync(cn, from.Value, to!.Value, cancellationToken);
        var open = schedules.Where(s => !s.Closed).ToArray();
        var start = open.Length == 0 ? from.Value.ToDateTime(TimeOnly.MinValue) : open.Min(s => s.Start);
        var end = open.Length == 0 ? start : open.Max(s => s.End);
        await using var command = new SqlCommand("""
            SELECT p.ReceivedAt, i.Total, i.GuestCount
            FROM dbo.Invoices i JOIN dbo.Payments p ON p.InvoiceId=i.Id
            WHERE i.Status='Paid' AND p.ReceivedAt>=@Start AND p.ReceivedAt<@End;
            SELECT COUNT(*) FROM dbo.DiningTables WHERE Status='Serving';
            """, cn);
        command.Parameters.Add("@Start", SqlDbType.DateTime2).Value = start.AddHours(-7);
        command.Parameters.Add("@End", SqlDbType.DateTime2).Value = end.AddHours(-7);
        var invoices = new List<ReportInvoice>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            invoices.Add(new(reader.GetDateTime(0).AddHours(7), reader.GetDecimal(1), reader.GetInt32(2)));
        await reader.NextResultAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return DailyOverviewRules.CalculateRange(schedules, clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime, invoices, reader.GetInt32(0));
    }

    private static async Task<IReadOnlyList<ReportHours>> RangeHoursAsync(SqlConnection cn, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        await using var cmd = new SqlCommand("""
            ;WITH Dates AS (
                SELECT @From AS BusinessDate
                UNION ALL SELECT DATEADD(day,1,BusinessDate) FROM Dates WHERE BusinessDate<@To
            )
            SELECT d.BusinessDate,
                CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.SpecialHolidays h WHERE h.HolidayDate=d.BusinessDate AND h.IsActive=1)
                    THEN 1 ELSE COALESCE(s.IsClosed,w.IsClosed,1) END),
                CASE WHEN s.BusinessDate IS NOT NULL THEN s.OpensAt ELSE w.OpensAt END,
                CASE WHEN s.BusinessDate IS NOT NULL THEN s.ClosesAt ELSE w.ClosesAt END
            FROM Dates d LEFT JOIN dbo.SpecialDates s ON s.BusinessDate=d.BusinessDate
            LEFT JOIN dbo.OpeningHours w ON w.DayOfWeek=((DATEDIFF(day,CONVERT(date,'19000101',112),d.BusinessDate)%7)+1)
            ORDER BY d.BusinessDate OPTION (MAXRECURSION 100);
            """, cn);
        cmd.Parameters.Add("@From", SqlDbType.Date).Value = from.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("@To", SqlDbType.Date).Value = to.ToDateTime(TimeOnly.MinValue);
        var schedules = new List<ReportHours>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var closed = reader.GetBoolean(1) || reader.IsDBNull(2) || reader.IsDBNull(3);
            schedules.Add(new(DateOnly.FromDateTime(reader.GetDateTime(0)),
                closed ? default : TimeOnly.FromTimeSpan(reader.GetTimeSpan(2)),
                closed ? default : TimeOnly.FromTimeSpan(reader.GetTimeSpan(3)), closed));
        }
        return schedules;
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
