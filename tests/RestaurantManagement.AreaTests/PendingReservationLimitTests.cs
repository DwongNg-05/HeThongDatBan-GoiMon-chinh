using RestaurantManagement.Web.Models.Reservations;

internal static class PendingReservationLimitTests
{
    internal static void Run(Action<bool, string> check)
    {
        check(PendingReservationLimit.Maximum == 3, "Pending reservation limit is three");
        check(!PendingReservationLimit.IsReached(0) && !PendingReservationLimit.IsReached(1) && !PendingReservationLimit.IsReached(2),
            "Pending counts below three are accepted");
        check(PendingReservationLimit.IsReached(3) && PendingReservationLimit.IsReached(4),
            "Third pending reservation blocks the next request");
        check(PendingReservationLimit.ReachedMessage.Contains("3 đơn đang chờ xác nhận")
              && PendingReservationLimit.ReachedMessage.Contains("gọi nhà hàng"),
            "Pending limit message explains the reason and next step");
    }
}
