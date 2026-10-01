using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models;

namespace RestaurantManagement.Web.Services;

/// <summary>Reads active areas and tables from the same database as table management.</summary>
public sealed class SqlTableMapReader(IConfiguration configuration) : ITableMapReader
{
    public TableMapSnapshot Read() => ReadAsync(CancellationToken.None).GetAwaiter().GetResult();

    public async Task<TableMapSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        // Lock in writer order and read watermark + tiles in one SQL round trip.
        await using var command = new SqlCommand("""
            DECLARE @TableCount bigint;
            SELECT @TableCount=COUNT_BIG(*) FROM dbo.DiningTables WITH (TABLOCK,HOLDLOCK);
            SELECT COALESCE(MAX(Id),0) FROM dbo.TableStatusChangeEvents WITH (TABLOCK,HOLDLOCK);
            SELECT a.Id, a.Name, t.Code, t.MaxCapacity, t.Status, t.StatusChangedAt
            FROM dbo.Areas a
            LEFT JOIN dbo.DiningTables t ON t.AreaId = a.Id AND t.IsActive = 1
            WHERE a.IsActive = 1
            ORDER BY a.SortOrder, a.Id, t.SortOrder, t.Code;
            """, connection, transaction);
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
            tables!.Add(new DiningTableCard(reader.GetString(2), areaName, reader.GetInt32(3),
                status, display.Label, display.CssClass,
                new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc))));
        }
        await reader.CloseAsync();
        await transaction.CommitAsync(cancellationToken);
        return new TableMapSnapshot(areas) { Cursor = cursor.ToString(System.Globalization.CultureInfo.InvariantCulture) };
    }

    public async Task<TableMapChanges> ReadChangesAsync(long after, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var watermark = new SqlCommand("SELECT COALESCE(MAX(Id),0) FROM dbo.TableStatusChangeEvents WITH (TABLOCK,HOLDLOCK);", connection, transaction);
        var cursor = Convert.ToInt64(await watermark.ExecuteScalarAsync(cancellationToken));
        if (after > cursor) throw new ArgumentOutOfRangeException(nameof(after), "Cursor exceeds current event history.");
        await using var command = new SqlCommand("""
            WITH changed AS (
                SELECT TableId, MAX(Id) AS Id FROM dbo.TableStatusChangeEvents
                WHERE Id > @After AND Id <= @Cursor GROUP BY TableId
            )
            SELECT e.TableCode,e.AreaName,e.Capacity,e.Status,e.ChangedAtUtc
            FROM changed c
            JOIN dbo.TableStatusChangeEvents e ON e.Id=c.Id
            ORDER BY c.Id;
            """, connection, transaction);
        command.Parameters.Add("@After", System.Data.SqlDbType.BigInt).Value = after;
        command.Parameters.Add("@Cursor", System.Data.SqlDbType.BigInt).Value = cursor;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var tables = new List<DiningTableCard>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var status = reader.GetString(3);
            var display = TableStatusDisplay.From(status);
            tables.Add(new DiningTableCard(reader.GetString(0), reader.GetString(1), reader.GetInt32(2),
                status, display.Label, display.CssClass,
                new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Utc))));
        }
        await reader.CloseAsync();
        await transaction.CommitAsync(cancellationToken);
        return new TableMapChanges(cursor.ToString(System.Globalization.CultureInfo.InvariantCulture), tables, DateTimeOffset.UtcNow);
    }
}
