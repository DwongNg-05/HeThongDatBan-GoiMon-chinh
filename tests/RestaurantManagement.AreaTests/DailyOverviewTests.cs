using RestaurantManagement.Web.Models.Reports;
using RestaurantManagement.Web.Security;
using RestaurantManagement.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

internal static class DailyOverviewTests
{
    internal static async Task RenderAsync(Action<bool, string> check)
    {
        var root = Path.GetFullPath("src/RestaurantManagement.Web");
        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(new Microsoft.AspNetCore.Builder.WebApplicationOptions
        {
            ApplicationName = typeof(DailyOverviewController).Assembly.GetName().Name,
            ContentRootPath = root,
            EnvironmentName = "Development"
        });
        builder.Services.AddControllersWithViews();
        await using var app = builder.Build();
        app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
        using var scope = app.Services.CreateScope();
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.Request.Scheme = "http";
        http.Request.Host = new("localhost");
        http.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(), "daily-overview-render-test"));
        var action = new Microsoft.AspNetCore.Mvc.ActionContext(http, new Microsoft.AspNetCore.Routing.RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
        var engine = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Mvc.ViewEngines.ICompositeViewEngine>();
        var found = engine.GetView(null, "/Views/DailyOverview/Index.cshtml", true);
        check(found.Success, "Compiled daily overview Razor view is discoverable");
        async Task<string> Render(DailyOverview model)
        {
            using var writer = new StringWriter();
            var data = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary<DailyOverview>(new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider(), new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary()) { Model = model };
            var temp = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(http, scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider>());
            var view = engine.GetView(null, "/Views/DailyOverview/Index.cshtml", true).View
                ?? throw new InvalidOperationException("Missing daily overview view.");
            await view.RenderAsync(new Microsoft.AspNetCore.Mvc.Rendering.ViewContext(action, view, data, temp, writer, new Microsoft.AspNetCore.Mvc.ViewFeatures.HtmlHelperOptions()));
            return System.Net.WebUtility.HtmlDecode(writer.ToString());
        }
        var date = new DateOnly(2026, 10, 15);
        var hours = new ReportHours(date, new(9, 0), new(12, 0), false);
        var empty = DailyOverviewRules.Calculate(hours, hours.Start, [], 0);
        var html = await Render(empty);
        check(html.Contains("Chưa có dữ liệu") && html.Contains("15/10/2026") && html.Contains("Bàn đang phục vụ"), "Rendered empty page has today's business date and metrics");
        var populated = await Render(DailyOverviewRules.Calculate(hours, hours.Start, [new(hours.Start, 125_000, 2)], 3));
        check(populated.Contains("revenue-bar") && populated.Contains("height:100%") && !populated.Contains("Chưa có dữ liệu"), "Rendered populated page has revenue columns and no empty message");
        var error = await Render(empty with { LoadError = "Không tải được số liệu." });
        check(error.Contains("Không tải được số liệu.") && !error.Contains("Chưa có dữ liệu") && !error.Contains("overview-metrics\""), "Rendered load failure never masquerades as zero revenue");
        check(html.Contains("name=\"from\"") && html.Contains("name=\"to\"") && html.Contains("value=\"2026-10-15\""), "Range form defaults both inputs to reported today");
        var range = DailyOverviewRules.CalculateRange([hours, hours with { Date = date.AddDays(1) }], hours.Start, [new(hours.Start, 250_000, 3)], 1);
        var rangeHtml = await Render(range);
        check(rangeHtml.Contains("Doanh thu theo ngày") && rangeHtml.Contains("16/10/2026") && rangeHtml.Contains("height:100%"), "Multi-day Razor shows daily columns and selected period");
        var invalidHtml = await Render(empty with { ValidationError = "Chỉ được chọn tối đa 92 ngày.", FromInput = "2026-01-01", ToInput = "2026-04-03" });
        check(invalidHtml.Contains("Chỉ được chọn tối đa 92 ngày.") && invalidHtml.Contains("value=\"2026-04-03\"") && !invalidHtml.Contains("overview-metrics\""), "Invalid range keeps inputs and shows limit instead of misleading metrics");
        check(html.Contains("daily-overview.js") && html.Contains("data-report-updated") && html.Contains("data-report-panel") && html.Contains("data-enabled=\"true\""), "Valid report has live script, refresh target and last-success timestamp");
        check(invalidHtml.Contains("data-enabled=\"false\""), "Invalid range never starts background requests");
        check(!error.Contains("data-report-updated"), "Initial SQL error never displays a false successful-update timestamp");
    }
    internal static async Task RunDatabaseAsync(Action<bool, string> check)
    {
        var connection = Environment.GetEnvironmentVariable("RM_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Set RM_CONNECTION_STRING for read-only database checks.");
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = connection }).Build();
        var store = new RestaurantManagement.Web.Services.DailyOverviewStore(config, TimeProvider.System);
        var result = await store.TodayAsync(CancellationToken.None);
        check(result.LoadError is null, "SQL-backed overview loads against migrated database");
        check(result.Hours.Sum(h => h.Revenue) == result.Revenue, "SQL-backed hourly totals reconcile");
        await using var cn = new Microsoft.Data.SqlClient.SqlConnection(connection);
        await cn.OpenAsync();
        await using var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
            SELECT COUNT(*),COALESCE(SUM(i.Total),0),COALESCE(SUM(i.GuestCount),0)
            FROM dbo.Invoices i JOIN dbo.Payments p ON p.InvoiceId=i.Id
            WHERE i.Status='Paid' AND p.ReceivedAt>=@Start AND p.ReceivedAt<@End;
            SELECT COUNT(*) FROM dbo.DiningTables WHERE Status='Serving';
            """, cn);
        var start = result.Hours.FirstOrDefault()?.Start ?? result.Date.ToDateTime(TimeOnly.MinValue);
        var end = result.Hours.LastOrDefault()?.End ?? start;
        cmd.Parameters.AddWithValue("@Start", start.AddHours(-7));
        cmd.Parameters.AddWithValue("@End", end.AddHours(-7));
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        check(reader.GetInt32(0) == result.InvoiceCount && reader.GetDecimal(1) == result.Revenue && reader.GetInt32(2) == result.GuestCount,
            "SQL-backed headline agrees with independent paid invoice aggregation");
        check(result.AverageInvoice == (result.InvoiceCount == 0 ? 0 : result.Revenue / result.InvoiceCount), "SQL-backed average is correct");
        await reader.NextResultAsync();
        await reader.ReadAsync();
        check(reader.GetInt32(0) == result.ServingTables, "SQL-backed serving tables agrees with physical table count");
        Console.WriteLine($"Business date {result.Date:yyyy-MM-dd}; {result.InvoiceCount} invoices; {result.Hours.Count} hourly buckets.");
        var single = await store.RangeAsync(result.Date, result.Date, CancellationToken.None);
        check(single.Date == result.Date && single.EndDate == result.Date && single.Revenue == result.Revenue && single.InvoiceCount == result.InvoiceCount,
            "SQL range: explicit one day agrees with default today");
        var wide = await store.RangeAsync(result.Date.AddDays(-91), result.Date, CancellationToken.None);
        check(wide.Days?.Count == 92 && wide.Date == result.Date.AddDays(-91) && wide.EndDate == result.Date,
            "SQL range: 92-day schedule query includes both endpoints");
        check(wide.Days!.Sum(d => d.Revenue) == wide.Revenue && wide.Days!.Sum(d => d.InvoiceCount) == wide.InvoiceCount,
            "SQL range: daily columns reconcile with range totals");
        var rejected = false;
        try { await store.RangeAsync(result.Date.AddDays(-92), result.Date, CancellationToken.None); }
        catch (ArgumentException ex) { rejected = ex.Message.Contains("92"); }
        check(rejected, "SQL range: store rejects 93-day requests before querying");
    }
    internal static void Run(Action<bool, string> check)
    {
        var date = new DateOnly(2026, 10, 15);
        var hours = new ReportHours(date, new(9, 30), new(12, 0), false);
        var report = DailyOverviewRules.Calculate(hours, hours.Start,
        [new(hours.Start.AddTicks(-1), 999, 99), new(hours.Start, 100_000, 2),
         new(hours.Start.AddHours(1), 200_000, 3), new(hours.End, 999, 99)], 4);
        check(report.Revenue == 300_000 && report.InvoiceCount == 2 && report.GuestCount == 5 && report.AverageInvoice == 150_000 && report.ServingTables == 4, "All five metrics use paid invoice totals and current serving count");
        check(report.Hours.Count == 3 && report.Hours[0].Revenue == 100_000 && report.Hours[1].Revenue == 200_000, "Opening inclusive, closing exclusive, exact hour assigned once");
        check(report.Hours[2].Revenue == 0 && report.Hours[2].End == hours.End, "Zero hour retained and final partial hour clipped");
        check(report.Hours.Sum(h => h.Revenue) == report.Revenue, "Hourly revenue reconciles with headline");
        var empty = DailyOverviewRules.Calculate(hours, hours.Start, [], 2);
        check(empty.Revenue == 0 && empty.InvoiceCount == 0 && empty.GuestCount == 0 && empty.AverageInvoice == 0 && empty.Hours.Count == 3 && empty.ServingTables == 2, "Empty day keeps zero metrics and hours without losing live table count");
        var night = new ReportHours(date, new(20, 0), new(2, 0), false);
        var overnight = DailyOverviewRules.Calculate(night, night.Start, [new(date.AddDays(1).ToDateTime(new(0, 0)), 50_000, 1), new(night.End, 90_000, 9)], 0);
        check(overnight.Revenue == 50_000 && overnight.Hours.Count == 6 && overnight.Hours[4].Revenue == 50_000, "Overnight includes next-day midnight and excludes closing boundary");
        check(DailyOverviewRules.BusinessDate(date.AddDays(1).ToDateTime(new(1, 59)), night) == date, "After midnight before closing belongs to previous business day");
        check(DailyOverviewRules.BusinessDate(night.End, night) == date.AddDays(1), "At closing default moves to current calendar day");
        var closed = DailyOverviewRules.Calculate(hours with { Closed = true }, hours.Start, [new(hours.Start, 100, 1)], 1);
        check(closed.InvoiceCount == 0 && closed.Hours.Count == 0 && closed.ScheduleMessage is not null, "Closed day has readable empty state");
        check(DailyOverviewRules.BusinessDate(date.AddDays(1).ToDateTime(new(1, 0)), night with { Closed = true }) == date.AddDays(1), "Closed previous day never captures today's business date");
        var auth = (AuthorizeAttribute?)Attribute.GetCustomAttribute(typeof(DailyOverviewController), typeof(AuthorizeAttribute));
        check(auth?.Roles == AppRoles.Manager && RoleNavigation.Items.Single(i => i.Key == "daily-overview").Roles.SequenceEqual([AppRoles.Manager]), "Report and navigation restricted to Manager");
        check(Attribute.IsDefined(typeof(DailyOverviewController).GetMethod(nameof(DailyOverviewController.Snapshot))!, typeof(RestaurantManagement.Web.Authentication.PassiveSessionReadAttribute)),
            "Automatic snapshots do not extend idle login sessions");
    }
}
