using RestaurantManagement.DbTool;
using RestaurantManagement.Web.Models.Reports;
using RestaurantManagement.Web.Security;

/// <summary>S4-06: báo cáo món đọc từ cùng thủ tục SQL, chỉ Quản lý, và xuất CSV.</summary>
internal static class MenuPerformanceReportTests
{
    internal static void Run(Action<bool, string> check)
    {
        var cancelled = new CancelledMenuItem(10, "Cơm chiên", 3, "SoldOut");
        check(cancelled.ReasonLabel == "Tạm hết món" && (cancelled with { MostCommonReason = "ChangedMind" }).ReasonLabel == "Khách đổi ý"
              && (cancelled with { MostCommonReason = null }).ReasonLabel == "Chưa có lý do",
            "S4-06 report: cancellation reasons have clear Vietnamese labels");

        var report = new MenuPerformanceReport(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 10),
            [new MenuPerformanceItem(1, "Phở bò", true, 12, 720000m)],
            [new MenuPerformanceItem(2, "Lẩu", false, 4, 1000000m)], [cancelled]);
        check(report.BestByQuantity.Single().Quantity == 12 && report.BestByRevenue.Single().GrossRevenue == 1000000m
              && report.MostCancelled.Single().Quantity == 3,
            "S4-06 report: the model keeps quantity, gross revenue and cancellation counts separate");

        var page = ApiAccessMatrix.All.Single(e => e.Key == "MenuPerformance.Index GET");
        var export = ApiAccessMatrix.All.Single(e => e.Key == "MenuPerformance.Export GET");
        check(ApiAccessMatrix.IsAllowed(page, "Manager") && ApiAccessMatrix.IsAllowed(export, "Manager")
              && new[] { "Waiter", "Kitchen", "Cashier" }.All(role => !ApiAccessMatrix.IsAllowed(page, role) && !ApiAccessMatrix.IsAllowed(export, role)),
            "S4-06 report: only the manager may view or export the report");
        check(RoleNavigation.Items.Single(i => i.Key == "menu-performance") is { Path: "/Reports/MenuPerformance", Title: "Báo cáo món bán chạy" },
            "S4-06 report: the manager navigation includes the menu performance report");

        var root = DatabaseTool.Root;
        var sql = File.ReadAllText(Path.Combine(root, "database", "migrations", "004_ViewsAndGuards.sql"));
        var controller = File.ReadAllText(Path.Combine(root, "src", "RestaurantManagement.Web", "Controllers", "Reports", "MenuPerformanceController.cs"));
        var view = File.ReadAllText(Path.Combine(root, "src", "RestaurantManagement.Web", "Views", "MenuPerformance", "Index.cshtml"));
        check(sql.Contains("CREATE OR ALTER PROCEDURE dbo.usp_ReportTopItems") && sql.Contains("TOP(10)")
              && sql.Contains("IsChargedCancellation=0") && sql.Contains("MostCommonReason"),
            "S4-06 SQL: the report has top ten sales and cancelled dishes without charged cancellation revenue");
        check(controller.Contains("[Authorize(Roles = AppRoles.Manager)]") && controller.Contains("Export") && controller.Contains("text/csv"),
            "S4-06 report: manager-only controller exports UTF-8 CSV");
        check(view.Contains("Top 10 theo số lượng") && view.Contains("Top 10 theo doanh thu gộp") && view.Contains("Top 10 món bị hủy nhiều"),
            "S4-06 report: the page shows all three required rankings");
    }
}
