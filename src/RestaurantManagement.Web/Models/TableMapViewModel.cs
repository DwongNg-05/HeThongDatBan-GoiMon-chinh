namespace RestaurantManagement.Web.Models;

public sealed record DiningTableCard(string Code, string Area, int Capacity, string Status, string StatusLabel, string StatusClass, DateTimeOffset ChangedAtUtc);

public sealed record TableStatusTransition(
    string Code,
    string Area,
    int Capacity,
    string PreviousStatus,
    string PreviousStatusLabel,
    string Status,
    string StatusLabel,
    string StatusClass,
    string ChangeReason,
    DateTimeOffset ChangedAtUtc)
{
    public static TableStatusTransition From(DiningTableCard previous, DiningTableCard current)
        => From(current.Code, current.Area, current.Capacity, previous.Status, current.Status, current.ChangedAtUtc);

    public static TableStatusTransition From(string code, string area, int capacity,
        string previousStatus, string status, DateTimeOffset changedAtUtc)
    {
        var previousDisplay = TableStatusDisplay.From(previousStatus);
        var currentDisplay = TableStatusDisplay.From(status);
        var reason = (previousStatus, status) switch
        {
            ("Available", "Reserved") => "ReservationConfirmed",
            ("Available" or "Reserved", "Serving") => "ServiceStarted",
            ("Serving", "Cleaning") => "ServiceClosed",
            ("Cleaning", "Available" or "Reserved") => "CleaningCompleted",
            ("Reserved", "Available") => "ReservationReleased",
            _ => "StatusChanged"
        };

        return new TableStatusTransition(code, area, capacity,
            previousStatus, previousDisplay.Label, status, currentDisplay.Label,
            currentDisplay.CssClass, reason, changedAtUtc);
    }
}

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
