namespace RestaurantManagement.Web.Models.Reservations;

public static class ReservationSchedulePolicy
{
    public const int MaximumAdvanceDays = 30;

    public static DateOnly Today => DateOnly.FromDateTime(VietnamTime.Now);

    public static DateOnly LastReservableDate => Today.AddDays(MaximumAdvanceDays);

    public static int ToOpeningDay(DateOnly date) => date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;

    public static IReadOnlyList<string> CreateSlots(TimeOnly opensAt, TimeOnly closesAt, DateOnly date)
    {
        var slots = new List<string>();
        var now = VietnamTime.Now;
        for (var time = opensAt; time < closesAt; time = time.AddMinutes(30))
        {
            var localStart = date.ToDateTime(time);
            if (date != DateOnly.FromDateTime(now) || localStart > now)
                slots.Add(time.ToString("HH:mm"));
        }
        return slots;
    }
}

public sealed record ReservationSlotsResult(DateOnly Date, IReadOnlyList<string> Slots, string? Message = null)
{
    public bool IsAvailable => Slots.Count > 0;
}

public sealed record TableReservationInterval(int TableId, DateTime StartsAtUtc, DateTime EndsAtUtc);

public static class ReservationCapacityPolicy
{
    public const int CleanupMinutes = 15;

    public static bool HasAvailableTable(IEnumerable<int> tableIds, IEnumerable<TableReservationInterval> reservations, DateTime startsAtUtc, DateTime endsAtUtc) =>
        tableIds.Any(tableId => !reservations.Any(reservation => reservation.TableId == tableId
            && reservation.StartsAtUtc < endsAtUtc.AddMinutes(CleanupMinutes)
            && startsAtUtc < reservation.EndsAtUtc.AddMinutes(CleanupMinutes)));
}
