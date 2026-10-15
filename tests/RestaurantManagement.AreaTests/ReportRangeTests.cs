using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantManagement.Web.Controllers;
using RestaurantManagement.Web.Models.Reports;
using RestaurantManagement.Web.Services;

internal static class ReportRangeTests
{
    internal static void Run(Action<bool, string> check)
    {
        var first = new DateOnly(2026, 7, 16);
        check(ReportRangeRules.Validate(first, first) is null, "Range: one day is valid");
        check(ReportRangeRules.Validate(first, first.AddDays(91)) is null, "Range: exactly 92 inclusive days is valid");
        check(ReportRangeRules.Validate(first, first.AddDays(92))?.Contains("92") == true, "Range: 93 inclusive days is rejected");
        check(ReportRangeRules.Validate(first.AddDays(1), first) is not null, "Range: reversed dates rejected");
        check(ReportRangeRules.Validate(null, first) is not null && ReportRangeRules.Validate(first, null) is not null && ReportRangeRules.Validate(null, null) is not null, "Range: either or both missing dates rejected");
        var schedules = Enumerable.Range(0, 92).Select(i => new ReportHours(first.AddDays(i), new(8, 0), new(23, 0), false)).ToArray();
        var report = DailyOverviewRules.CalculateRange(schedules, first.ToDateTime(new(12, 0)),
            [new(schedules[0].Start.AddTicks(-1), 999, 99), new(schedules[0].Start, 100_000, 2),
             new(schedules[^1].End.AddTicks(-1), 300_000, 4), new(schedules[^1].End, 999, 99)], 7);
        check(report.InvoiceCount == 2 && report.Revenue == 400_000 && report.GuestCount == 6 && report.AverageInvoice == 200_000, "Range: includes both first and last day payments and excludes outer boundaries");
        check(report.Days?.Count == 92 && report.Days[0].Revenue == 100_000 && report.Days[^1].Revenue == 300_000 && report.Days[1].Revenue == 0, "Range: daily chart retains all 92 days including zero days");
        check(report.ServingTables == 7 && report.Days!.Sum(d => d.Revenue) == report.Revenue && report.Hours.Count == 0, "Range: current table snapshot and daily chart reconcile");
        var empty = DailyOverviewRules.CalculateRange(schedules, report.CapturedAt, [], 4);
        check(empty.Revenue == 0 && empty.AverageInvoice == 0 && empty.InvoiceCount == 0 && empty.Days?.Count == 92 && empty.ServingTables == 4, "Range: empty period has zero revenue and current serving tables");
        var one = DailyOverviewRules.CalculateRange([schedules[0]], report.CapturedAt, [new(schedules[0].Start, 50, 1)], 0);
        check(!one.IsMultiDay && one.Hours.Count == 15 && one.Revenue == 50, "Range: single day keeps hourly chart");
        var night = new ReportHours(first, new(20, 0), new(2, 0), false);
        var next = new ReportHours(first.AddDays(1), new(1, 0), new(23, 0), false);
        var overlapping = DailyOverviewRules.CalculateRange([night, next], report.CapturedAt, [new(first.AddDays(1).ToDateTime(new(1, 30)), 100, 1)], 0);
        check(overlapping.InvoiceCount == 1 && overlapping.Days![0].Revenue == 100 && overlapping.Days[1].Revenue == 0, "Range: overnight overlap never double-counts a payment");
        var closed = DailyOverviewRules.CalculateRange([schedules[0] with { Closed = true }, schedules[1]], report.CapturedAt,
            [new(schedules[0].Start, 1000, 9), new(schedules[1].Start, 200, 2)], 1);
        check(closed.Revenue == 200 && closed.Days![0].Revenue == 0, "Range: closed days stay zero and do not count transactions");
    }

    internal static async Task ControllerAsync(Action<bool, string> check)
    {
        // No connection configured: invalid requests must return before accessing SQL.
        var config = new ConfigurationBuilder().Build();
        foreach (var (query, from, to) in new (string, string?, string?)[]
        {
            ("?from=2026-01-01", "2026-01-01", null),
            ("?to=2026-01-01", null, "2026-01-01"),
            ("?from=&to=", "", ""),
            ("?from=wrong&to=2026-01-01", "wrong", "2026-01-01"),
            ("?from=2026-02-30&to=2026-03-01", "2026-02-30", "2026-03-01"),
            ("?from=2026-01-02&to=2026-01-01", "2026-01-02", "2026-01-01"),
            ("?from=2026-01-01&to=2026-04-03", "2026-01-01", "2026-04-03")
        })
        {
            var context = new DefaultHttpContext();
            context.Request.QueryString = new QueryString(query);
            var controller = new DailyOverviewController(new DailyOverviewStore(config, TimeProvider.System), TimeProvider.System, NullLogger<DailyOverviewController>.Instance)
            { ControllerContext = new ControllerContext { HttpContext = context } };
            var result = (ViewResult)await controller.Index(CancellationToken.None, from, to);
            var model = (DailyOverview)result.Model!;
            check(context.Response.StatusCode == 400 && model.ValidationError is not null && model.FromInput == (from ?? "") && model.ToInput == (to ?? ""), "Range controller rejects invalid query without SQL and preserves inputs: " + query);
        }
    }
}
