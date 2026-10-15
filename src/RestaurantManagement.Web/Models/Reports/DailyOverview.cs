namespace RestaurantManagement.Web.Models.Reports;

public sealed record ReportHours(DateOnly Date, TimeOnly Opens, TimeOnly Closes, bool Closed)
{
    public DateTime Start => Date.ToDateTime(Opens);
    public DateTime End => Date.ToDateTime(Closes).AddDays(Closes <= Opens ? 1 : 0);
}
public sealed record ReportInvoice(DateTime PaidAtLocal, decimal Total, int Guests);
public sealed record RevenueHour(DateTime Start, DateTime End, decimal Revenue);
public sealed record RevenueDay(DateOnly Date, decimal Revenue, int InvoiceCount, int GuestCount);
public sealed record DailyOverview(DateOnly Date, DateTime CapturedAt, decimal Revenue, int InvoiceCount,
    int GuestCount, decimal AverageInvoice, int ServingTables, IReadOnlyList<RevenueHour> Hours,
    string? ScheduleMessage = null, string? LoadError = null,
    DateOnly? RangeEnd = null, IReadOnlyList<RevenueDay>? Days = null,
    string? FromInput = null, string? ToInput = null, string? ValidationError = null)
{
    public DateOnly EndDate => RangeEnd ?? Date;
    public bool IsMultiDay => EndDate > Date;
}

public static class ReportRangeRules
{
    public const int MaximumDays = 92;
    public static string? Validate(DateOnly? from, DateOnly? to)
    {
        if (from is null || to is null) return "Vui lòng chọn đầy đủ ngày bắt đầu và ngày kết thúc.";
        if (from.Value.Year < 1900 || to.Value.Year > 9998) return "Vui lòng chọn ngày trong khoảng năm 1900 đến 9998.";
        if (from > to) return "Ngày bắt đầu phải nhỏ hơn hoặc bằng ngày kết thúc.";
        if (to.Value.DayNumber - from.Value.DayNumber + 1 > MaximumDays)
            return "Chỉ được chọn tối đa 92 ngày, bao gồm cả ngày bắt đầu và ngày kết thúc.";
        return null;
    }
}

public static class DailyOverviewRules
{
    public static DailyOverview CalculateRange(IReadOnlyList<ReportHours> schedules, DateTime now,
        IEnumerable<ReportInvoice> invoices, int serving)
    {
        if (schedules.Count == 0) throw new ArgumentException("A date range must contain a schedule for every day.", nameof(schedules));
        var ordered = schedules.OrderBy(s => s.Date).ToArray();
        var error = ReportRangeRules.Validate(ordered[0].Date, ordered[^1].Date);
        if (error is not null) throw new ArgumentException(error, nameof(schedules));
        var assigned = ordered.ToDictionary(s => s.Date, _ => new List<ReportInvoice>());
        foreach (var invoice in invoices)
        {
            // An overlapping overnight schedule must never count the same payment twice.
            var owner = ordered.FirstOrDefault(s => !s.Closed && invoice.PaidAtLocal >= s.Start && invoice.PaidAtLocal < s.End);
            if (owner is not null) assigned[owner.Date].Add(invoice);
        }
        var daily = ordered.Select(s => Calculate(s, now, assigned[s.Date], serving)).ToArray();
        var revenue = daily.Sum(d => d.Revenue);
        var count = daily.Sum(d => d.InvoiceCount);
        return daily[0] with
        {
            RangeEnd = ordered[^1].Date,
            Revenue = revenue, InvoiceCount = count, GuestCount = daily.Sum(d => d.GuestCount),
            AverageInvoice = count == 0 ? 0 : revenue / count,
            Hours = daily.Length == 1 ? daily[0].Hours : [],
            Days = daily.Select(d => new RevenueDay(d.Date, d.Revenue, d.InvoiceCount, d.GuestCount)).ToArray(),
            ScheduleMessage = daily.Length == 1 ? daily[0].ScheduleMessage : null
        };
    }
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
