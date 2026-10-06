using System.Data;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models;

namespace RestaurantManagement.Web.Services;

/// <summary>Reads the persisted table map without taking table-wide locks.</summary>
public sealed class SqlTableMapReader(IConfiguration configuration) : ITableMapReader
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("SQL Server connection is not configured.");

    public async Task<TableMapSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        // Read the watermark first. A change after this read is fetched by the next
        // incremental poll; a change before the table read is already in this snapshot.
        await using var command = new SqlCommand("""
            SELECT COALESCE(MAX(Id),0) FROM dbo.TableStatusChangeEvents;
            SELECT a.Id,a.Name,t.Code,t.MaxCapacity,t.Status,t.StatusChangedAt
            FROM dbo.Areas a
            LEFT JOIN dbo.DiningTables t ON t.AreaId=a.Id AND t.IsActive=1
            WHERE a.IsActive=1
            ORDER BY a.SortOrder,a.Id,t.SortOrder,t.Code;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
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
            if (reader.IsDBNull(2)) continue;
            var status = reader.GetString(4);
            var display = TableStatusDisplay.From(status);
            tables!.Add(new DiningTableCard(reader.GetString(2), areaName, reader.GetInt32(3), status,
                display.Label, display.CssClass,
                new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc))));
        }
        return new TableMapSnapshot(areas)
        {
            Cursor = cursor.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    public async Task<TableMapChanges> ReadChangesAsync(long after, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            DECLARE @cursor bigint=(SELECT COALESCE(MAX(Id),0) FROM dbo.TableStatusChangeEvents);
            IF @After>@cursor THROW 52001,N'Dữ liệu đồng bộ đã được đặt lại.',1;
            SELECT @cursor;
            WITH changed AS (
                SELECT TableId,MAX(Id) AS Id FROM dbo.TableStatusChangeEvents
                WHERE Id>@After AND Id<=@cursor GROUP BY TableId
            )
            SELECT e.TableCode,e.AreaName,e.Capacity,e.Status,e.ChangedAtUtc
            FROM changed c JOIN dbo.TableStatusChangeEvents e ON e.Id=c.Id
            ORDER BY c.Id;
            """, connection);
        command.Parameters.Add("@After", SqlDbType.BigInt).Value = after;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var cursor = reader.GetInt64(0);
        await reader.NextResultAsync(cancellationToken);
        var tables = new List<DiningTableCard>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var status = reader.GetString(3);
            var display = TableStatusDisplay.From(status);
            tables.Add(new DiningTableCard(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), status,
                display.Label, display.CssClass,
                new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Utc))));
        }
        return new TableMapChanges(cursor.ToString(System.Globalization.CultureInfo.InvariantCulture), tables, DateTimeOffset.UtcNow);
    }
}
