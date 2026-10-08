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
    Task<TableMapSnapshot> ReadAsync(CancellationToken cancellationToken);
    Task<TableMapChanges> ReadChangesAsync(long after, CancellationToken cancellationToken);
}
