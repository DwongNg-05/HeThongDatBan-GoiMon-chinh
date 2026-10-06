using Microsoft.Data.SqlClient;

namespace RestaurantManagement.Web.Services.Tables;

/// <summary>Keeps the live table map synchronized when a confirmed booking enters or leaves its 30-minute arrival window.</summary>
public sealed class ReservationTableStatusSyncWorker(
    IConfiguration configuration,
    ILogger<ReservationTableStatusSyncWorker> logger) : BackgroundService
{
    private static readonly TimeSpan SyncInterval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        using var timer = new PeriodicTimer(SyncInterval);
        do
        {
            try
            {
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync(stoppingToken);
                await using var command = new SqlCommand("EXEC dbo.usp_SyncReservationTableStatuses;", connection);
                await command.ExecuteNonQueryAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Unable to synchronize reservation table statuses for the table map.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
