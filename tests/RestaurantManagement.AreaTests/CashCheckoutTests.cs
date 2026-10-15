using RestaurantManagement.Web.Models.Cashier;

namespace RestaurantManagement.AreaTests;

/// <summary>S4-02 Task 1: phép tính hiển thị tiền thối chỉ là tiện ích giao diện; SQL vẫn là nguồn chốt cuối cùng.</summary>
internal static class CashCheckoutTests
{
    public static void Run(Action<bool, string> check)
    {
        var session = new PayableSession(15, "A05", 2, "AwaitingPayment", DateTime.UtcNow,
            2, 0, 125_000);
        var lines = new[] { new CashierPaymentLine("Món demo", "phần", 1, 125_000, 125_000, null, "A05", false) };
        var paid = new CashierPaymentViewModel(session, lines, 200_000, Guid.NewGuid());
        var shortCash = new CashierPaymentViewModel(session, lines, 100_000, Guid.NewGuid());

        check(paid.Total == 125_000 && paid.Change == 75_000 && paid.HasEnoughCash,
            "Cash checkout calculates VND change from server-owned total");
        check(shortCash.Change == 0 && !shortCash.HasEnoughCash,
            "Cash checkout marks insufficient cash and never displays negative change");
    }
}
