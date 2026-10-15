namespace RestaurantManagement.Web.Models;

public sealed record DiningTableCard(
    string Code,
    string Area,
    int Capacity,
    string Status,
    string StatusLabel,
    string StatusClass,
    DateTimeOffset ChangedAtUtc,
    string? UpcomingGuestName = null,
    DateTimeOffset? UpcomingStartsAtUtc = null)
{
    public TableGroupInfo? Group { get; init; }
}

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
    public static TableStatusTransition From(
        DiningTableCard previous,
        DiningTableCard current)
        => From(
            current.Code,
            current.Area,
            current.Capacity,
            previous.Status,
            current.Status,
            current.ChangedAtUtc);

    public static TableStatusTransition From(
        string code,
        string area,
        int capacity,
        string previousStatus,
        string status,
        DateTimeOffset changedAtUtc)
    {
        var previousDisplay = TableStatusDisplay.From(previousStatus);
        var currentDisplay = TableStatusDisplay.From(status);

        var reason = (previousStatus, status) switch
        {
            ("Available", "Reserved") => "ReservationConfirmed",
            ("Available" or "Reserved", "Serving") => "ServiceStarted",
            ("Serving", "AwaitingPayment") => "PaymentRequested",
            ("AwaitingPayment", "Serving") => "ServiceReopened",
            ("Serving" or "AwaitingPayment", "Cleaning") => "ServiceClosed",
            ("Cleaning", "Available" or "Reserved") => "CleaningCompleted",
            ("Reserved", "Available") => "ReservationReleased",
            _ => "StatusChanged"
        };

        return new TableStatusTransition(
            code,
            area,
            capacity,
            previousStatus,
            previousDisplay.Label,
            status,
            currentDisplay.Label,
            currentDisplay.CssClass,
            reason,
            changedAtUtc);
    }
}

public sealed record TableStatusDisplay(string Label, string CssClass)
{
    public static TableStatusDisplay From(string? status)
    {
        if (!TryNormalize(status, out var normalized))
        {
            return new("Không xác định", "unknown");
        }

        return normalized switch
        {
            "Available" => new("Trống", "available"),
            "Reserved" => new("Đã đặt trước", "reserved"),
            "Serving" => new("Đang phục vụ", "serving"),

            // Dùng màu đang phục vụ hiện có.
            "AwaitingPayment" => new("Chờ thanh toán", "serving"),

            "Cleaning" => new("Đang dọn", "cleaning"),
            _ => new("Không xác định", "unknown")
        };
    }

    public static bool TryNormalize(
        string? status,
        out string normalized)
    {
        normalized = status?.Trim().ToUpperInvariant() switch
        {
            "AVAILABLE" => "Available",
            "RESERVED" => "Reserved",
            "SERVING" => "Serving",
            "AWAITINGPAYMENT" => "AwaitingPayment",
            "CLEANING" => "Cleaning",
            _ => string.Empty
        };

        return normalized.Length > 0;
    }
}

public sealed record TableMapArea(int Id, string Name, int TableCount);

public sealed class TableMapViewModel
{
    public required IReadOnlyList<DiningTableCard> Tables { get; init; }

    public required IReadOnlyList<TableMapArea> Areas { get; init; }

    public int? SelectedAreaId { get; init; }

    public int TotalCount { get; init; }

    public string? LoadError { get; init; }
}
