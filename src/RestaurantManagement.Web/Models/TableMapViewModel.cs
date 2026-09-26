namespace RestaurantManagement.Web.Models;

public sealed record DiningTableCard(string Code, string Area, int Capacity, string Status, string StatusLabel, string StatusClass);

public sealed record TableStatusDisplay(string Label, string CssClass)
{
    public static TableStatusDisplay From(string? status) => status?.Trim().ToUpperInvariant() switch
    {
        "AVAILABLE" => new("Trống", "available"),
        "RESERVED" => new("Đã đặt trước", "reserved"),
        "SERVING" => new("Đang phục vụ", "serving"),
        "CLEANING" => new("Đang dọn", "cleaning"),
        _ => new("Không xác định", "unknown")
    };
}

public sealed class TableMapViewModel
{
    public required IReadOnlyList<DiningTableCard> Tables { get; init; }
    public required IReadOnlyList<string> Areas { get; init; }
    public string? SelectedArea { get; init; }
    public int TotalCount { get; init; }
}
