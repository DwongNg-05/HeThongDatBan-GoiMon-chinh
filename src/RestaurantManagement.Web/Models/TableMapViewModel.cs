namespace RestaurantManagement.Web.Models;

public sealed record DiningTableCard(string Code, string Area, int Capacity, string Status, string StatusLabel, string StatusClass, DateTimeOffset ChangedAtUtc);

public sealed record TableStatusDisplay(string Label, string CssClass)
{
    public static TableStatusDisplay From(string? status) => TryNormalize(status, out var normalized) ? normalized switch
    {
        "Available" => new("Trống", "available"),
        "Reserved" => new("Đã đặt trước", "reserved"),
        "Serving" => new("Đang phục vụ", "serving"),
        "Cleaning" => new("Đang dọn", "cleaning"),
        _ => new("Không xác định", "unknown")
    } : new("Không xác định", "unknown");

    public static bool TryNormalize(string? status, out string normalized)
    {
        normalized = status?.Trim().ToUpperInvariant() switch
        {
            "AVAILABLE" => "Available",
            "RESERVED" => "Reserved",
            "SERVING" => "Serving",
            "CLEANING" => "Cleaning",
            _ => string.Empty
        };
        return normalized.Length > 0;
    }
}

public sealed class TableMapViewModel
{
    public required IReadOnlyList<DiningTableCard> Tables { get; init; }
    public required IReadOnlyList<string> Areas { get; init; }
    public string? SelectedArea { get; init; }
    public int TotalCount { get; init; }
}
