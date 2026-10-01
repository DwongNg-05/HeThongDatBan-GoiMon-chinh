using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models;

namespace RestaurantManagement.Web.Services;

/// <summary>Reads active areas and tables from the same database as table management.</summary>
public sealed class SqlTableMapReader(IConfiguration configuration) : ITableMapReader
{
    public TableMapSnapshot Read()
    {
        using var connection = new SqlConnection(configuration.GetConnectionString("DefaultConnection"));
        connection.Open();
        using var transaction = connection.BeginTransaction();
        // Writers lock tables before inserting events; use the same order for the initial snapshot.
        using var tableLock = new SqlCommand("SELECT COUNT_BIG(*) FROM dbo.DiningTables WITH (TABLOCK,HOLDLOCK);", connection, transaction);
        tableLock.ExecuteScalar();
        // A shared table lock waits for pending event inserts before choosing the watermark.
        // This prevents an earlier uncommitted identity being skipped by a later committed event.
        using var watermark = new SqlCommand("SELECT COALESCE(MAX(Id),0) FROM dbo.TableStatusChangeEvents WITH (TABLOCK,HOLDLOCK);", connection, transaction);
        var cursor = Convert.ToInt64(watermark.ExecuteScalar());
        using var command = new SqlCommand("""
            SELECT a.Id, a.Name, t.Code, t.MaxCapacity, t.Status, t.StatusChangedAt
            FROM dbo.Areas a
            LEFT JOIN dbo.DiningTables t ON t.AreaId = a.Id AND t.IsActive = 1
            WHERE a.IsActive = 1
            ORDER BY a.SortOrder, a.Id, t.SortOrder, t.Code;
            """, connection, transaction);
        using var reader = command.ExecuteReader();
        var areas = new List<TableMapArea>();
        List<DiningTableCard>? tables = null;
        int? previousAreaId = null;
        while (reader.Read())
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
        reader.Close();
        transaction.Commit();
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
