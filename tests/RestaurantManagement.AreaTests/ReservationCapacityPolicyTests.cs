using RestaurantManagement.Web.Models.Reservations;

internal static class ReservationCapacityPolicyTests
{
    internal static void Run(Action<bool, string> check)
    {
        var start = new DateTime(2030, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var end = start.AddMinutes(90);
        var held = new[] { new TableReservationInterval(1, start, end) };
        check(!ReservationCapacityPolicy.HasAvailableTable([1], held, start, end),
            "Reservation capacity hides a slot when its last table is held");
        check(ReservationCapacityPolicy.HasAvailableTable([1, 2], held, start, end),
            "Reservation capacity keeps a slot when another suitable table remains");
        check(!ReservationCapacityPolicy.HasAvailableTable([1], held, end, end.AddMinutes(90)),
            "Reservation capacity keeps a table unavailable during the 15-minute cleanup");
        check(ReservationCapacityPolicy.HasAvailableTable([1], held, end.AddMinutes(15), end.AddMinutes(105)),
            "Reservation capacity releases a table exactly at the cleanup boundary");
        check(!ReservationCapacityPolicy.HasAvailableTable([1], held, start.AddMinutes(30), end.AddMinutes(30)),
            "Reservation capacity rejects overlapping holds on the same table");
    }
}
