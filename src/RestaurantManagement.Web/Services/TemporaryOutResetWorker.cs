using Microsoft.Data.SqlClient;

namespace RestaurantManagement.Web.Services;

public sealed class TemporaryOutResetWorker(IConfiguration configuration, ILogger<TemporaryOutResetWorker> logger) : BackgroundService
{
    private static readonly TimeZoneInfo VietnamTimeZone = FindVietnamTimeZone();
    private string ConnectionString => Environment.GetEnvironmentVariable("RM_CONNECTION_STRING")
        ?? configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Set RM_CONNECTION_STRING to connect the daily menu reset worker to SQL Server.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var nowUtc = DateTimeOffset.UtcNow;
            var vietnamNow = TimeZoneInfo.ConvertTime(nowUtc, VietnamTimeZone);
            var scheduledLocal = new DateTime(vietnamNow.Year, vietnamNow.Month, vietnamNow.Day, 0, 0, 0, DateTimeKind.Unspecified);
            var scheduledUtc = TimeZoneInfo.ConvertTimeToUtc(scheduledLocal, VietnamTimeZone);
            if (nowUtc.UtcDateTime < scheduledUtc)
            {
                await Task.Delay(scheduledUtc - nowUtc.UtcDateTime, stoppingToken);
                continue;
            }

            try
            {
                await ResetTemporarilyOutItems(scheduledUtc, stoppingToken);
                var nextUtc = TimeZoneInfo.ConvertTimeToUtc(scheduledLocal.AddDays(1), VietnamTimeZone);
                await Task.Delay(nextUtc - DateTime.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not reset temporarily unavailable menu items for {BusinessDate} (Asia/Ho_Chi_Minh). Retrying shortly.", scheduledLocal.Date);
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }

    private async Task ResetTemporarilyOutItems(DateTime scheduledUtc, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("dbo.usp_ResetTemporarilyOutMenuItems", connection)
        {
            CommandType = System.Data.CommandType.StoredProcedure,
            CommandTimeout = 60
        };
        command.Parameters.AddWithValue("@ScheduledFor", scheduledUtc);
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        logger.LogInformation("Reset {Count} temporarily unavailable menu items for {BusinessDate}.", count, TimeZoneInfo.ConvertTimeFromUtc(scheduledUtc, VietnamTimeZone).Date);
    }

    private static TimeZoneInfo FindVietnamTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time"); }
    }
}
