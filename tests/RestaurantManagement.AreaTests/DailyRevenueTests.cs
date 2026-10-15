using System.Security.Claims;
using RestaurantManagement.Web.Models.Reports;
using RestaurantManagement.Web.Security;

internal static class DailyRevenueTests
{
    internal static void Run(Action<bool, string> check)
    {
        var day = new DateOnly(2026, 10, 15);
        var summary = DailyRevenueSummary.Calculate(day,
        [
            new("Paid", 300_000m, 2), new("Paid", 450_000m, 3),
            new("Voided", 900_000m, 4), new("Refunded", 100_000m, 1)
        ], 7);
        check(summary is { InvoiceCount: 2, GuestCount: 5, Revenue: 750_000m, AverageInvoice: 375_000m, ServingTableCount: 7, HasTransactions: true },
            "Daily summary counts paid invoices and excludes voided or unsupported refund statuses");

        var empty = DailyRevenueSummary.Calculate(day, [], 2);
        check(empty is { InvoiceCount: 0, GuestCount: 0, Revenue: 0m, AverageInvoice: 0m, ServingTableCount: 2, HasTransactions: false },
            "Empty day reports zero transaction metrics and retains live serving-table count");

        var sourceRoot = FindRepositoryRoot();
        var controller = File.ReadAllText(Path.Combine(sourceRoot, "src/RestaurantManagement.Web/Controllers/Orders/DailyRevenueController.cs"));
        check(controller.Contains("[Authorize(Roles = AppRoles.Manager)]") && controller.Contains("VietnamTime.Now")
              && controller.Contains("BusinessDate=@BusinessDate") && controller.Contains("t.Status='Serving'")
              && controller.Contains("t.IsActive=1 AND a.IsActive=1"),
            "Daily report is Manager-only, uses Vietnam business date and counts active serving tables");

        var view = File.ReadAllText(Path.Combine(sourceRoot, "src/RestaurantManagement.Web/Views/DailyRevenue/Index.cshtml"));
        check(new[] { "revenue", "invoices", "guests", "average-invoice", "serving-tables" }.All(key => view.Contains($"data-metric=\"{key}\""))
              && view.Contains("daily-revenue-empty") && view.Contains("chưa có giao dịch"),
            "Daily report view shows all five indicators and a clear empty-day notice");

        var item = RoleNavigation.Items.Single(link => link.Key == "daily-revenue");
        check(item.Path == "/Reports/Daily" && item.Roles.SequenceEqual([AppRoles.Manager]),
            "Daily revenue navigation is limited to Manager");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src/RestaurantManagement.Web/Controllers/Orders/DailyRevenueController.cs")))
                return directory.FullName;
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src/RestaurantManagement.Web/Controllers/Orders/DailyRevenueController.cs")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Repository root was not found for daily revenue tests.");
    }
}
