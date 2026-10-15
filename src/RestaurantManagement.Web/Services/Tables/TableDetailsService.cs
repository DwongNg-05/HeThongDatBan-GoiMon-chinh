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
                m.Code,m.AreaName,m.MaxCapacity,
                CASE WHEN m.Status='Available' AND reservationWindow.Id IS NOT NULL THEN 'Reserved' ELSE m.Status END AS DisplayStatus,
                m.GuestCount,m.OpenedAt,
                currentReservation.CustomerName,currentReservation.Phone,
                reservationWindow.CustomerName,reservationWindow.Phone,reservationWindow.GuestCount,reservationWindow.StartsAt,
                CASE WHEN m.SessionId IS NULL THEN NULL ELSE COALESCE(balance.Subtotal,0) END AS CurrentSubtotal,
                session.Id AS SessionId,
                CASE WHEN session.Id IS NULL THEN NULL
                     WHEN session.Status='Open' AND billingRoot.Status='Open' THEN 'Open'
                     WHEN session.Status='Closed' OR billingRoot.Status='Closed' THEN 'Closed'
                     ELSE 'AwaitingPayment' END AS SessionStatus
            FROM dbo.vw_TableMap AS m
            LEFT JOIN dbo.DiningSessions AS session ON session.Id=m.SessionId
            LEFT JOIN dbo.DiningSessions AS billingRoot ON billingRoot.Id=COALESCE(session.BillingSessionId,session.Id)
            LEFT JOIN dbo.Reservations AS currentReservation ON currentReservation.Id=session.ReservationId
            OUTER APPLY
            (
                SELECT TOP(1) r.Id,r.CustomerName,r.Phone,r.GuestCount,r.StartsAt
                FROM dbo.Reservations r
                WHERE r.TableId=m.Id
                  AND r.Status IN ('Pending','Confirmed')
                  AND r.StartsAt<=DATEADD(minute,30,SYSUTCDATETIME())
                  AND DATEADD(minute,15,r.EndsAt)>SYSUTCDATETIME()
                ORDER BY r.StartsAt,r.Id
            ) AS reservationWindow
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
        TableDetailsViewModel details;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
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

            long? sessionId = reader.IsDBNull(13) ? null : Convert.ToInt64(reader.GetValue(13));
            string? sessionStatus = reader.IsDBNull(14) ? null : reader.GetString(14);

            details = new TableDetailsViewModel(
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
                false)
            {
                UpcomingReservationCount = reservation is null ? 0 : 1,
                SessionId = hasActiveSession && sessionStatus != "Closed" ? sessionId : null,
                SessionStatus = hasActiveSession && sessionStatus != "Closed" ? sessionStatus : null
            };
        }

        if (details.SessionId is null)
            return details;

        return details with { OrderedItems = await ReadOrderedItems(connection, details.SessionId.Value, cancellationToken),
            Group = await TableGroupReader.GetAsync(connection, details.Code, cancellationToken) };
    }

    /// <summary>Các món của phiên (cùng nguồn với màn hình Gọi món: dbo.OrderBatches + dbo.OrderItems).</summary>
    private static async Task<IReadOnlyList<TableDetailsOrderItem>> ReadOrderedItems(
        SqlConnection connection, long sessionId, CancellationToken cancellationToken)
    {
        const string query = """
            SELECT b.BatchNumber, i.SubmittedAt, i.ItemName, i.Unit, i.Quantity, i.LineTotal, i.Status, i.ChargeWhenCancelled, t.Code
            FROM dbo.OrderBatches AS b
            JOIN dbo.OrderItems AS i ON i.BatchId=b.Id
            JOIN dbo.DiningTables t ON t.Id=i.OriginalTableId
            WHERE b.SessionId=(SELECT COALESCE(BillingSessionId,Id) FROM dbo.DiningSessions WHERE Id=@SessionId)
            ORDER BY b.BatchNumber, i.Id;
            """;
        await using var command = new SqlCommand(query, connection);
        command.Parameters.Add("@SessionId", System.Data.SqlDbType.BigInt).Value = sessionId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<TableDetailsOrderItem>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var itemStatus = reader.GetString(6);
            var chargeWhenCancelled = reader.GetBoolean(7);
            items.Add(new TableDetailsOrderItem(
                reader.GetInt32(0),
                AsUtc(reader.GetDateTime(1)),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetDecimal(5),
                itemStatus,
                itemStatus != "Cancelled" || chargeWhenCancelled) { OriginalTableCode = reader.GetString(8) });
        }
        return items;
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
            true)
        {
            UpcomingReservationCount = upcoming is null ? 0 : 1,
            SessionStatus = serving ? "Open" : null,
            OrderedItems = serving
                ? (IReadOnlyList<TableDetailsOrderItem>)new List<TableDetailsOrderItem>
                {
                    new(1, now.AddMinutes(-40), "Lẩu thái (mẫu)", "nồi", 1, 199000, "Served", true),
                    new(2, now.AddMinutes(-12), "Trà đá (mẫu)", "ly", 2, 46000, "Pending", true)
                }
                : Array.Empty<TableDetailsOrderItem>()
        };
    }

    private static DateTimeOffset AsUtc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
