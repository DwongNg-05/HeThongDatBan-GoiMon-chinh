namespace RestaurantManagement.Web.Models.Reservations;

public static class PendingReservationLimit
{
    public const int Maximum = 3;

    public const string ReachedMessage = "Bạn đã có 3 đơn đang chờ xác nhận với số điện thoại này. Vui lòng chờ nhà hàng xác nhận hoặc gọi nhà hàng để được hỗ trợ.";

    public static bool IsReached(int pendingCount) => pendingCount >= Maximum;
}
