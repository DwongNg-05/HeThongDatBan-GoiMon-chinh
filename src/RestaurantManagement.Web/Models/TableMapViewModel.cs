namespace RestaurantManagement.Web.Models;

public sealed record DiningTableCard(string Code, string Area, int Capacity, string Status, string StatusLabel);

public sealed class TableMapViewModel
{
    public required IReadOnlyList<DiningTableCard> Tables { get; init; }
    public required IReadOnlyList<string> Areas { get; init; }
    public string? SelectedArea { get; init; }
    public int TotalCount { get; init; }
}
