using RestaurantManagement.Web.Models;

namespace RestaurantManagement.Web.Services;

public sealed record TableMapArea(string Name, IReadOnlyList<DiningTableCard> Tables)
{
    public int AvailableCount => Tables.Count(table => table.Status == "Available");
}

public sealed record TableMapSnapshot(IReadOnlyList<TableMapArea> Areas, bool LoadFailed = false)
{
    public int TotalCount => Areas.Sum(area => area.Tables.Count);
    public string Cursor { get; init; } = "0";
    public DateTimeOffset SyncedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record TableMapChanges(string Cursor, IReadOnlyList<DiningTableCard> Tables, DateTimeOffset SyncedAtUtc);

public interface ITableMapReader
{
    TableMapSnapshot Read();
    Task<TableMapChanges> ReadChangesAsync(long after, CancellationToken cancellationToken)
        => throw new NotSupportedException("Change tracking is unavailable.");
}

// Uses the existing development catalog; keeps areas with zero tables in the snapshot.
public sealed class DemoTableMapReader(DemoTableCatalog catalog) : ITableMapReader
{
    public TableMapSnapshot Read()
    {
        var map = catalog.Get(null);
        return Group(map.Areas, map.Tables);
    }

    public static TableMapSnapshot Group(IReadOnlyList<string> areas, IReadOnlyList<DiningTableCard> tables)
        => new(areas.Select(area => new TableMapArea(area,
            tables.Where(table => table.Area == area).OrderBy(table => table.Code, StringComparer.Ordinal).ToArray())).ToArray());
}
