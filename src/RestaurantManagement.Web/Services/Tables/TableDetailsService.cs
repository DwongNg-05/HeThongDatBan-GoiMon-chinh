using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models;

namespace RestaurantManagement.Web.Services;

public sealed class TableDetailsService(
    IConfiguration configuration,
    DemoTableCatalog demoCatalog,
    IHostEnvironment environment,
    ILogger<TableDetailsService> logger)
{
    public async Task<TableDetailsViewModel?> GetAsync(string code, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            return await ReadFromDatabase(code, timeout.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (environment.IsDevelopment())
        {
            logger.LogWarning(exception, "Falling back to sample details for table {TableCode}; SQL Server is unavailable or its schema is not migrated.", code);
            return GetDemoDetails(code);
        }
    }

    private async Task<TableDetailsViewModel?> ReadFromDatabase(string code, CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("SQL Server connection is not configured.");
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        const string query = """
            SELECT TOP (1)
                m.Code,m.AreaName,m.MaxCapacity,m.Status,m.GuestCount,m.OpenedAt,
                currentReservation.CustomerName,currentReservation.Phone,
                upcoming.CustomerName,upcoming.Phone,upcoming.GuestCount,upcoming.StartsAt,
                CASE WHEN m.SessionId IS NULL THEN NULL ELSE COALESCE(balance.Subtotal,0) END AS CurrentSubtotal
            FROM dbo.vw_TableMap AS m
            LEFT JOIN dbo.DiningSessions AS session ON session.Id=m.SessionId
            LEFT JOIN dbo.Reservations AS currentReservation ON currentReservation.Id=session.ReservationId
            LEFT JOIN dbo.Reservations AS upcoming ON upcoming.Id=m.UpcomingReservationId
            OUTER APPLY
            (
                SELECT SUM(sessionTotals.Subtotal) AS Subtotal
                FROM dbo.vw_SessionTotals AS sessionTotals
                WHERE sessionTotals.BillingSessionId=COALESCE(session.BillingSessionId,session.Id)
            ) AS balance
            WHERE m.Code=@Code;
            """;
        await using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@Code", code);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var status = reader.GetString(3);
        var display = TableStatusDisplay.From(status);
        var hasActiveSession = !reader.IsDBNull(4) && !reader.IsDBNull(5);
        DateTimeOffset? startedAt = hasActiveSession ? AsUtc(reader.GetDateTime(5)) : null;
        int? currentGuestCount = reader.IsDBNull(4) ? null : reader.GetInt32(4);
        UpcomingTableReservation? reservation = reader.IsDBNull(8) || reader.IsDBNull(11)
            ? null
            : new UpcomingTableReservation(reader.GetString(8), reader.GetString(9), reader.GetInt32(10), AsUtc(reader.GetDateTime(11)));
        decimal? subtotal = reader.IsDBNull(12) ? null : reader.GetDecimal(12);

        return new TableDetailsViewModel(
            reader.GetString(0), reader.GetString(1), reader.GetInt32(2), status,
            display.Label, display.CssClass,
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            currentGuestCount,
            reservation,
            startedAt,
            startedAt is null ? null : (long)Math.Max(0, (DateTimeOffset.UtcNow - startedAt.Value).TotalMinutes),
            subtotal,
            hasActiveSession,
            false) { UpcomingReservationCount = reservation is null ? 0 : 1 };
    }

    private TableDetailsViewModel? GetDemoDetails(string code)
    {
        var table = demoCatalog.GetAll().FirstOrDefault(item => string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase));
        if (table is null)
            return null;

        var now = DateTimeOffset.UtcNow;
        var serving = table.Status == "Serving";
        var reserved = table.Status == "Reserved";
        var upcoming = reserved
            ? new UpcomingTableReservation("Khách mẫu " + table.Code, "0900000000", Math.Min(table.Capacity, 4), now.AddMinutes(35))
            : null;

        return new TableDetailsViewModel(
            table.Code, table.Area, table.Capacity, table.Status, table.StatusLabel, table.StatusClass,
            serving ? "Khách mẫu " + table.Code : null,
            serving ? "0900000000" : null,
            serving ? Math.Min(table.Capacity, 3) : null,
            upcoming,
            serving ? now.AddMinutes(-42) : null,
            serving ? 42 : null,
            serving ? 245000 : null,
            serving,
            true) { UpcomingReservationCount = upcoming is null ? 0 : 1 };
    }

    private static DateTimeOffset AsUtc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
