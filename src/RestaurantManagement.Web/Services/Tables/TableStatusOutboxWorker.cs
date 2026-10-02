using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models;

namespace RestaurantManagement.Web.Services;

/// <summary>Polls the transactional SQL outbox so changes from every stored procedure reach SSE clients.</summary>
public sealed class TableStatusOutboxWorker(
    IConfiguration configuration,
    DemoTableCatalog catalog,
    TableMapEventBroker eventBroker,
    ILogger<TableStatusOutboxWorker> logger) : BackgroundService
{
    private const int PollIntervalMilliseconds = 500;
    private const int RetryIntervalMilliseconds = 5000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            logger.LogInformation("Table status SQL event polling is disabled because no default connection string is configured.");
            return;
        }

        long lastEventId = 0;
        var initialized = false;
        logger.LogInformation("Polling SQL table status changes every {PollInterval} ms.", PollIntervalMilliseconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = PollIntervalMilliseconds;
            try
            {
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync(stoppingToken);

                if (!initialized)
                {
                    await using var latest = new SqlCommand("SELECT COALESCE(MAX(Id),0) FROM dbo.TableStatusChangeEvents;", connection);
                    lastEventId = Convert.ToInt64(await latest.ExecuteScalarAsync(stoppingToken));
                    initialized = true;
                }

                var changes = await ReadChanges(connection, lastEventId, stoppingToken);
                foreach (var change in changes)
                {
                    catalog.ApplyDatabaseStatus(change.Code, change.Status, change.ChangedAtUtc);
                    var transition = TableStatusTransition.From(change.Code, change.Area, change.Capacity,
                        change.PreviousStatus, change.Status, change.ChangedAtUtc);
                    eventBroker.Publish(transition);
                    lastEventId = change.Id;
                }

                if (changes.Count == 100)
                    delay = 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Could not poll table status changes. Check the database connection and apply migrations.");
                delay = RetryIntervalMilliseconds;
            }

            if (delay > 0)
            {
                try { await Task.Delay(delay, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }

    private static async Task<List<OutboxChange>> ReadChanges(SqlConnection connection, long afterId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP (100) Id,TableCode,AreaName,Capacity,PreviousStatus,Status,ChangedAtUtc
            FROM dbo.TableStatusChangeEvents
            WHERE Id>@AfterId
            ORDER BY Id;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@AfterId", afterId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var changes = new List<OutboxChange>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var changedAt = DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc);
            changes.Add(new OutboxChange(reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                reader.GetInt32(3), reader.GetString(4), reader.GetString(5), new DateTimeOffset(changedAt)));
        }
        return changes;
    }

    private sealed record OutboxChange(long Id, string Code, string Area, int Capacity,
        string PreviousStatus, string Status, DateTimeOffset ChangedAtUtc);
}
