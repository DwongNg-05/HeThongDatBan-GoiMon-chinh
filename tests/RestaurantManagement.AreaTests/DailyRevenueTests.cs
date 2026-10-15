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

        var hourly = DailyRevenueSummary.BuildHourlyRevenue(day, new TimeOnly(8, 0), new TimeOnly(11, 0), false,
        [
            new("Paid", 100_000m, new DateTime(2026, 10, 15, 1, 59, 0, DateTimeKind.Utc)),
            new("Paid", 200_000m, new DateTime(2026, 10, 15, 2, 0, 0, DateTimeKind.Utc)),
            new("Paid", 400_000m, new DateTime(2026, 10, 15, 4, 0, 0, DateTimeKind.Utc)),
            new("Voided", 900_000m, new DateTime(2026, 10, 15, 2, 30, 0, DateTimeKind.Utc))
        ]);
        check(hourly.Count == 3 && hourly.Select(slot => slot.Label).SequenceEqual(["08:00", "09:00", "10:00"])
              && hourly.Select(slot => slot.Revenue).SequenceEqual([100_000m, 200_000m, 0m]),
            "Hourly buckets use Vietnam issue time, include the exact boundary in the next hour and preserve zero hours");

        var overnight = DailyRevenueSummary.BuildHourlyRevenue(day, new TimeOnly(22, 0), new TimeOnly(2, 0), false,
        [new("Paid", 50_000m, new DateTime(2026, 10, 15, 17, 30, 0, DateTimeKind.Utc))]);
        check(overnight.Select(slot => slot.Label).SequenceEqual(["22:00", "23:00", "00:00", "01:00"])
              && overnight[2].Revenue == 50_000m,
            "Overnight opening hours continue with next-day clock labels");
        check(DailyRevenueSummary.BuildHourlyRevenue(day, null, null, true, []).Count == 0,
            "Closed or missing special-date hours produce no chart buckets");

        var sourceRoot = FindRepositoryRoot();
        var controller = File.ReadAllText(Path.Combine(sourceRoot, "src/RestaurantManagement.Web/Controllers/Orders/DailyRevenueController.cs"));
        check(controller.Contains("[Authorize(Roles = AppRoles.Manager)]") && controller.Contains("VietnamTime.Now")
              && controller.Contains("BusinessDate=@BusinessDate") && controller.Contains("t.Status='Serving'")
              && controller.Contains("t.IsActive=1 AND a.IsActive=1"),
            "Daily report is Manager-only, uses Vietnam business date and counts active serving tables");

        var view = File.ReadAllText(Path.Combine(sourceRoot, "src/RestaurantManagement.Web/Views/DailyRevenue/Index.cshtml"));
        check(new[] { "revenue", "invoices", "guests", "average-invoice", "serving-tables" }.All(key => view.Contains($"data-metric=\"{key}\""))
              && view.Contains("daily-revenue-empty") && view.Contains("chưa có giao dịch")
              && view.Contains("hourly-revenue") && view.Contains("hourly-revenue-empty"),
            "Daily report view shows all five indicators, hourly chart and clear empty-day notices");

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
