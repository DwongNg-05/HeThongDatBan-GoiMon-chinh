namespace RestaurantManagement.Web.Models.Cashier;

public sealed record ManagedShiftRow(
    long Id,
    string Name,
    DateTime BusinessDate,
    string Status,
    DateTime OpenedAt,
    DateTime? ClosedAt);

public sealed record ShiftHistoryRow(
    long Id,
    long ShiftId,
    string Action,
    int ActorUserId,
    string? Reason,
    DateTime OccurredAt)
{
    public string ActionLabel => Action switch
    {
        "Opened" => "Mở ca",
        "Closed" => "Chốt ca",
        "Reopened" => "Mở lại ca",
        _ => Action
    };
}

public sealed class ShiftManagementViewModel
{
    public IReadOnlyList<ManagedShiftRow> Shifts { get; init; } = [];

    public IReadOnlyList<ShiftHistoryRow> History { get; init; } = [];

    public long? SelectedShiftId { get; init; }

    public bool HasOpenShift { get; init; }
}