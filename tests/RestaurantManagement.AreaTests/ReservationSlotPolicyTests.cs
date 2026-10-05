using RestaurantManagement.Web.Models.Reservations;

internal static class ReservationSlotPolicyTests
{
    internal static void Run(Action<bool, string> check)
    {
        check(ReservationSchedulePolicy.MaximumAdvanceDays == 30, "Reservation slots allow booking up to 30 days ahead");
        check(ReservationSchedulePolicy.ToOpeningDay(new DateOnly(2026, 10, 5)) == 1
              && ReservationSchedulePolicy.ToOpeningDay(new DateOnly(2026, 10, 11)) == 7,
            "Reservation slots map Monday and Sunday to opening-hours days");
        var weekday = new DateOnly(2030, 1, 7);
        var slots = ReservationSchedulePolicy.CreateSlots(new TimeOnly(8, 0), new TimeOnly(10, 0), weekday);
        check(slots.SequenceEqual(["08:00", "08:30", "09:00", "09:30"]), "Reservation slots use 30-minute steps and exclude closing time");
        var shortDay = ReservationSchedulePolicy.CreateSlots(new TimeOnly(8, 15), new TimeOnly(9, 20), weekday);
        check(shortDay.SequenceEqual(["08:15", "08:45", "09:15"]), "Reservation slots respect a weekday's own opening hours");
    }
}
