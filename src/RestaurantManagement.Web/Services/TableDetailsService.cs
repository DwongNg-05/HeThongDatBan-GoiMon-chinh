using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models;

namespace RestaurantManagement.Web.Services;

public sealed class TableDetailsService(
    IConfiguration configuration,
    DemoTableCatalog demoCatalog,
    IHostEnvironment environment,
    ILogger<TableDetailsService> logger)
{
    public Task<TableDetailsViewModel?> GetAsync(string code, CancellationToken cancellationToken)
        => GetCore(code, null, cancellationToken);

    public Task<TableDetailsViewModel?> GetForUserAsync(string code, int actorUserId, CancellationToken cancellationToken)
        => GetCore(code, actorUserId, cancellationToken);

    private async Task<TableDetailsViewModel?> GetCore(string code, int? actorUserId, CancellationToken cancellationToken)
    {
        if (actorUserId is null && environment.IsDevelopment() && configuration.GetValue<bool>("TableDetails:UseDemoData"))
        {
            logger.LogInformation("Explicit development demo requested for table {TableCode}.", code);
            return GetDemoDetails(code);
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        return await ReadFromDatabase(code, actorUserId, timeout.Token);
    }

    private async Task<TableDetailsViewModel?> ReadFromDatabase(string code, int? actorUserId, CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("SQL Server connection is not configured.");
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        const string query = """
            IF @ActorUserId IS NOT NULL AND NOT EXISTS (
                SELECT 1 FROM dbo.Users u JOIN dbo.Roles r ON r.Id=u.RoleId
                WHERE u.Id=@ActorUserId AND u.IsActive=1 AND r.Code='Waiter'
            ) THROW 51001,N'Không có quyền xem chi tiết bàn.',1;
            DECLARE @Now datetime2(3)=SYSUTCDATETIME();
            SELECT
                t.Code,a.Name,t.MaxCapacity,t.Status,session.GuestCount,session.OpenedAt,
                currentReservation.CustomerName,currentReservation.Phone,
                upcoming.CustomerName,upcoming.Phone,upcoming.GuestCount,upcoming.StartsAt,
                CASE WHEN session.Id IS NULL THEN NULL ELSE COALESCE(balance.Subtotal,0) END AS CurrentSubtotal,
                COALESCE(upcoming.ReservationCount,0)
            FROM dbo.DiningTables t
            JOIN dbo.Areas a ON a.Id=t.AreaId AND a.IsActive=1
            OUTER APPLY (
                SELECT TOP(1) s.* FROM dbo.SessionTables st
                JOIN dbo.DiningSessions s ON s.Id=st.SessionId AND s.Status IN ('Open','AwaitingPayment')
                WHERE st.TableId=t.Id AND st.ReleasedAt IS NULL AND t.Status='Serving'
                ORDER BY s.OpenedAt DESC,s.Id DESC
            ) session
            LEFT JOIN dbo.Reservations AS currentReservation ON currentReservation.Id=session.ReservationId
            OUTER APPLY (
                SELECT TOP(1) r.CustomerName,r.Phone,r.GuestCount,r.StartsAt,COUNT(*) OVER() AS ReservationCount
                FROM dbo.Reservations r
                WHERE r.TableId=t.Id AND r.Status='Confirmed' AND r.StartsAt>=@Now
                ORDER BY r.StartsAt,r.Id
            ) upcoming
            OUTER APPLY
            (
                SELECT SUM(sessionTotals.Subtotal) AS Subtotal
                FROM dbo.vw_SessionTotals AS sessionTotals
                WHERE sessionTotals.BillingSessionId=COALESCE(session.BillingSessionId,session.Id)
            ) AS balance
            WHERE t.Code=@Code AND t.IsActive=1;
            """;
        await using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@Code", code);
        command.Parameters.Add("@ActorUserId", System.Data.SqlDbType.Int).Value = (object?)actorUserId ?? DBNull.Value;
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
            false) { UpcomingReservationCount = reader.GetInt32(13) };
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
