namespace RestaurantManagement.Web.Models;

public sealed record UpcomingTableReservation(
    string CustomerName,
    string Phone,
    int GuestCount,
    DateTimeOffset StartsAtUtc);

public sealed record TableDetailsViewModel(
    string Code,
    string Area,
    int Capacity,
    string Status,
    string StatusLabel,
    string StatusClass,
    string? CurrentGuestName,
    string? CurrentGuestPhone,
    int? CurrentGuestCount,
    UpcomingTableReservation? UpcomingReservation,
    DateTimeOffset? ServiceStartedAtUtc,
    long? ServiceElapsedMinutes,
    decimal? CurrentSubtotal,
    bool HasActiveSession,
    bool IsDemoData)
{
    public int UpcomingReservationCount { get; init; }
    public DateTimeOffset SyncedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string SubtotalExplanation => "Tổng món hiện tại, chưa trừ giảm giá hoặc phụ thu.";
}
