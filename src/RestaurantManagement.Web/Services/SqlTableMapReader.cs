using System.Data;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models;

namespace RestaurantManagement.Web.Services;

public sealed class SqlTableMapReader(
    IConfiguration configuration) : ITableMapReader
{
    private string ConnectionString =>
        Environment.GetEnvironmentVariable("RM_CONNECTION_STRING")
        ?? configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException(
            "Chưa cấu hình kết nối SQL Server.");

    public async Task<TableMapSnapshot> ReadAsync(
        CancellationToken cancellationToken)
    {
        await using var connection =
            new SqlConnection(ConnectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand("""
            DECLARE @now datetime2(3)=SYSUTCDATETIME();
            SELECT COALESCE(MAX(Id), 0)
            FROM dbo.TableStatusChangeEvents;

            SELECT
                a.Id,
                a.Name,
                t.Code,
                t.MaxCapacity,
                CASE
                    WHEN t.Status = 'Serving'
                        AND payment.IsAwaiting = 1
                    THEN 'AwaitingPayment'
                    WHEN t.Status = 'Available'
                        AND reservation.StartsAt IS NOT NULL
                    THEN 'Reserved'
                    ELSE t.Status
                END AS DisplayStatus,
                CASE
                    WHEN t.Status = 'Serving'
                        AND payment.IsAwaiting = 1
                    THEN COALESCE(
                        payment.PaymentRequestedAt,
                        t.StatusChangedAt)
                    ELSE t.StatusChangedAt
                END AS DisplayChangedAt,
                reservation.CustomerName,reservation.StartsAt
            FROM dbo.Areas a
            LEFT JOIN dbo.DiningTables t
                ON t.AreaId = a.Id AND t.IsActive = 1
            OUTER APPLY (
                SELECT TOP (1)
                    CASE
                        WHEN s.Status = 'AwaitingPayment'
                            OR root.Status = 'AwaitingPayment'
                        THEN 1 ELSE 0
                    END AS IsAwaiting,
                    COALESCE(
                        root.PaymentRequestedAt,
                        s.PaymentRequestedAt) AS PaymentRequestedAt
                FROM dbo.SessionTables st
                JOIN dbo.DiningSessions s
                    ON s.Id = st.SessionId
                JOIN dbo.DiningSessions root
                    ON root.Id = COALESCE(s.BillingSessionId, s.Id)
                WHERE st.TableId = t.Id
                    AND st.ReleasedAt IS NULL
                    AND s.Status <> 'Closed'
                    AND root.Status <> 'Closed'
                ORDER BY st.SessionId DESC
            ) payment
            OUTER APPLY dbo.fn_NextTableReservation(t.Id,@now) reservation
            WHERE a.IsActive = 1
            ORDER BY a.SortOrder, a.Id, t.SortOrder, t.Code;
            """, connection);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        await reader.ReadAsync(cancellationToken);
        var cursor = reader.GetInt64(0);

        await reader.NextResultAsync(cancellationToken);

        var areas = new List<TableMapArea>();
        List<DiningTableCard>? tables = null;
        int? previousAreaId = null;

        while (await reader.ReadAsync(cancellationToken))
        {
            var areaId = reader.GetInt32(0);
            var areaName = reader.GetString(1);

            if (previousAreaId != areaId)
            {
                tables = [];
                areas.Add(new TableMapArea(areaName, tables));
                previousAreaId = areaId;
            }

            if (reader.IsDBNull(2))
            {
                continue;
            }

            var status = reader.GetString(4);
            var display = TableStatusDisplay.From(status);

            tables!.Add(new DiningTableCard(
                reader.GetString(2),
                areaName,
                reader.GetInt32(3),
                status,
                display.Label,
                display.CssClass,
                new DateTimeOffset(DateTime.SpecifyKind(
                    reader.GetDateTime(5),
                    DateTimeKind.Utc)),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(7),DateTimeKind.Utc))));
        }

        return new TableMapSnapshot(areas)
        {
            Cursor = cursor.ToString(
                System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    public async Task<TableMapChanges> ReadChangesAsync(
        long after,
        CancellationToken cancellationToken)
    {
        await using var connection =
            new SqlConnection(ConnectionString);

        await connection.OpenAsync(cancellationToken);

        // Trả trạng thái hiện tại của các bàn để nhận cả thay đổi
        // thanh toán chỉ xảy ra trên DiningSessions.
        await using var command = new SqlCommand("""
            DECLARE @now datetime2(3)=SYSUTCDATETIME();
            DECLARE @cursor bigint = (
                SELECT COALESCE(MAX(Id), 0)
                FROM dbo.TableStatusChangeEvents
            );

            IF @After > @cursor
                THROW 52001,
                    N'Dữ liệu đồng bộ đã được đặt lại.',
                    1;

            SELECT @cursor;

            SELECT
                t.Code,
                a.Name,
                t.MaxCapacity,
                CASE
                    WHEN t.Status = 'Serving'
                        AND payment.IsAwaiting = 1
                    THEN 'AwaitingPayment'
                    WHEN t.Status = 'Available'
                        AND reservation.StartsAt IS NOT NULL
                    THEN 'Reserved'
                    ELSE t.Status
                END AS DisplayStatus,
                CASE
                    WHEN t.Status = 'Serving'
                        AND payment.IsAwaiting = 1
                    THEN COALESCE(
                        payment.PaymentRequestedAt,
                        t.StatusChangedAt)
                    ELSE t.StatusChangedAt
                END AS DisplayChangedAt,
                reservation.CustomerName,reservation.StartsAt
            FROM dbo.DiningTables t
            JOIN dbo.Areas a ON a.Id = t.AreaId
            OUTER APPLY (
                SELECT TOP (1)
                    CASE
                        WHEN s.Status = 'AwaitingPayment'
                            OR root.Status = 'AwaitingPayment'
                        THEN 1 ELSE 0
                    END AS IsAwaiting,
                    COALESCE(
                        root.PaymentRequestedAt,
                        s.PaymentRequestedAt) AS PaymentRequestedAt
                FROM dbo.SessionTables st
                JOIN dbo.DiningSessions s
                    ON s.Id = st.SessionId
                JOIN dbo.DiningSessions root
                    ON root.Id = COALESCE(s.BillingSessionId, s.Id)
                WHERE st.TableId = t.Id
                    AND st.ReleasedAt IS NULL
                    AND s.Status <> 'Closed'
                    AND root.Status <> 'Closed'
                ORDER BY st.SessionId DESC
            ) payment
            OUTER APPLY dbo.fn_NextTableReservation(t.Id,@now) reservation
            WHERE t.IsActive = 1 AND a.IsActive = 1
            ORDER BY a.SortOrder, a.Id, t.SortOrder, t.Code;
            """, connection);

        command.Parameters.Add("@After", SqlDbType.BigInt)
            .Value = after;

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        await reader.ReadAsync(cancellationToken);
        var cursor = reader.GetInt64(0);

        await reader.NextResultAsync(cancellationToken);

        var tables = new List<DiningTableCard>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var status = reader.GetString(3);
            var display = TableStatusDisplay.From(status);

            tables.Add(new DiningTableCard(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                status,
                display.Label,
                display.CssClass,
                new DateTimeOffset(DateTime.SpecifyKind(
                    reader.GetDateTime(4),
                    DateTimeKind.Utc)),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(6),DateTimeKind.Utc))));
        }

        return new TableMapChanges(
            cursor.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            tables,
            DateTimeOffset.UtcNow);
    }
}
