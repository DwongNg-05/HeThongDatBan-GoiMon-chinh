namespace RestaurantManagement.Web.Models.Reports;

public sealed record ReportHours(DateOnly Date, TimeOnly Opens, TimeOnly Closes, bool Closed)
{
    public DateTime Start => Date.ToDateTime(Opens);
    public DateTime End => Date.ToDateTime(Closes).AddDays(Closes <= Opens ? 1 : 0);
}
public sealed record ReportInvoice(DateTime PaidAtLocal, decimal Total, int Guests);
public sealed record RevenueHour(DateTime Start, DateTime End, decimal Revenue);
public sealed record DailyOverview(DateOnly Date, DateTime CapturedAt, decimal Revenue, int InvoiceCount,
    int GuestCount, decimal AverageInvoice, int ServingTables, IReadOnlyList<RevenueHour> Hours,
    string? ScheduleMessage = null, string? LoadError = null);

public static class DailyOverviewRules
{
    public static DateOnly BusinessDate(DateTime localNow, ReportHours yesterday) =>
        !yesterday.Closed && yesterday.End.Date > yesterday.Start.Date && localNow < yesterday.End
            ? yesterday.Date : DateOnly.FromDateTime(localNow);

    public static DailyOverview Calculate(ReportHours hours, DateTime now, IEnumerable<ReportInvoice> invoices, int serving)
    {
        var paid = hours.Closed ? [] : invoices.Where(i => i.PaidAtLocal >= hours.Start && i.PaidAtLocal < hours.End).ToArray();
        var buckets = new List<RevenueHour>();
        if (!hours.Closed)
            for (var start = hours.Start; start < hours.End; start = start.AddHours(1))
            {
                var end = start.AddHours(1) < hours.End ? start.AddHours(1) : hours.End;
                buckets.Add(new(start, end, paid.Where(i => i.PaidAtLocal >= start && i.PaidAtLocal < end).Sum(i => i.Total)));
            }
        var revenue = paid.Sum(i => i.Total);
        return new(hours.Date, now, revenue, paid.Length, paid.Sum(i => i.Guests),
            paid.Length == 0 ? 0 : revenue / paid.Length, serving, buckets,
            hours.Closed ? "Nhà hàng đóng cửa hoặc chưa cấu hình giờ hoạt động cho ngày này." : null);
    }
}
