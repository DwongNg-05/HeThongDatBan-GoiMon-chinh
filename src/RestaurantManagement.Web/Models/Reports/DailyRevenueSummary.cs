using RestaurantManagement.Web.Models.Reservations;

namespace RestaurantManagement.Web.Models.Reports;

/// <summary>Snapshot các hoá đơn trong ngày; trạng thái ngoài Paid bị loại khỏi chỉ số doanh thu.</summary>
public sealed record DailyRevenueInvoice(string Status, decimal Total, int GuestCount);

public sealed record DailyRevenueInvoiceTime(string Status, decimal Total, DateTime IssuedAtUtc);

public sealed record HourlyRevenueSlot(DateTime StartsAtLocal, decimal Revenue)
{
    public string Label => StartsAtLocal.ToString("HH:mm");
}

public sealed record RevenueDateRange(DateOnly FromDate, DateOnly ToDate)
{
    public const int MaximumDays = 92;
    public int Days => ToDate.DayNumber - FromDate.DayNumber + 1;

    public static bool TryCreate(DateOnly? fromDate, DateOnly? toDate, DateOnly today,
        out RevenueDateRange? range, out string? error)
    {
        var from = fromDate ?? today;
        var to = toDate ?? today;
        if (from > to)
        {
            range = null;
            error = "Ngày bắt đầu không được sau ngày kết thúc.";
            return false;
        }
        if (to.DayNumber - from.DayNumber + 1 > MaximumDays)
        {
            range = null;
            error = "Khoảng ngày tối đa là 92 ngày. Vui lòng chọn khoảng ngắn hơn.";
            return false;
        }
        range = new RevenueDateRange(from, to);
        error = null;
        return true;
    }
}

/// <summary>
/// S4-06 Task 1: doanh thu là tổng Total sau giảm giá của hoá đơn Paid; lượt khách là tổng GuestCount đã chốt trên các hoá đơn đó.
/// Bản phát hành hiện chỉ có Paid/Voided, vì vậy hoá đơn huỷ/hoàn toàn phần phải ở trạng thái Voided để không tính.
/// Bàn đã checkout mang trạng thái Cleaning và không được tính là đang phục vụ.
/// </summary>
public sealed record DailyRevenueSummary(DateOnly FromDate, DateOnly ToDate, long InvoiceCount, long GuestCount, decimal Revenue,
    decimal AverageInvoice, long ServingTableCount, IReadOnlyList<HourlyRevenueSlot> HourlyRevenue)
{
    public bool HasTransactions => InvoiceCount > 0;

    public static DailyRevenueSummary Calculate(DateOnly fromDate, DateOnly toDate, IEnumerable<DailyRevenueInvoice> invoices, long servingTableCount)
    {
        var paid = invoices.Where(invoice => invoice.Status == "Paid").ToArray();
        var revenue = paid.Sum(invoice => invoice.Total);
        var invoiceCount = (long)paid.Length;
        var guestCount = paid.Sum(invoice => (long)invoice.GuestCount);
        return new DailyRevenueSummary(fromDate, toDate, invoiceCount, guestCount, revenue,
            invoiceCount == 0 ? 0 : revenue / invoiceCount, servingTableCount, []);
    }

    public static IReadOnlyList<HourlyRevenueSlot> BuildHourlyRevenueRange(DateOnly from, IEnumerable<DailyRevenueInvoiceTime> invoices)
    {
        var paid = invoices.Where(invoice => invoice.Status == "Paid")
            .Select(invoice => (At: VietnamTime.FromUtc(DateTime.SpecifyKind(invoice.IssuedAtUtc, DateTimeKind.Utc)), invoice.Total))
            .ToArray();
        return Enumerable.Range(0, 24).Select(hour => new HourlyRevenueSlot(from.ToDateTime(new TimeOnly(hour, 0)),
            paid.Where(invoice => invoice.At.Hour == hour).Sum(invoice => invoice.Total))).ToArray();
    }

    public static IReadOnlyList<HourlyRevenueSlot> BuildHourlyRevenue(DateOnly day, TimeOnly? opensAt, TimeOnly? closesAt,
        bool isClosed, IEnumerable<DailyRevenueInvoiceTime> invoices)
    {
        if (isClosed || opensAt is null || closesAt is null || opensAt == closesAt) return [];

        var opens = day.ToDateTime(opensAt.Value);
        var start = day.ToDateTime(new TimeOnly(opensAt.Value.Hour, 0));
        var close = day.ToDateTime(closesAt.Value);
        if (close <= opens) close = close.AddDays(1);
        var paid = invoices.Where(invoice => invoice.Status == "Paid")
            .Select(invoice => (At: VietnamTime.FromUtc(DateTime.SpecifyKind(invoice.IssuedAtUtc, DateTimeKind.Utc)), invoice.Total))
            .ToArray();
        var slots = new List<HourlyRevenueSlot>();
        for (var at = start; at < close; at = at.AddHours(1))
        {
            var from = at < opens ? opens : at;
            var until = at.AddHours(1) < close ? at.AddHours(1) : close;
            var revenue = paid.Where(invoice => invoice.At >= from && invoice.At < until).Sum(invoice => invoice.Total);
            slots.Add(new HourlyRevenueSlot(at, revenue));
        }
        return slots;
    }
}

public sealed record DailyRevenuePageModel(DateOnly FromDate, DateOnly ToDate, DailyRevenueSummary? Summary, string? ValidationError);
