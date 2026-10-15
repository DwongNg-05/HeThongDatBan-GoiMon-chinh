namespace RestaurantManagement.Web.Models.Reports;

/// <summary>Snapshot các hoá đơn trong ngày; trạng thái ngoài Paid bị loại khỏi chỉ số doanh thu.</summary>
public sealed record DailyRevenueInvoice(string Status, decimal Total, int GuestCount);

/// <summary>
/// S4-06 Task 1: doanh thu là tổng Total sau giảm giá của hoá đơn Paid; lượt khách là tổng GuestCount đã chốt trên các hoá đơn đó.
/// Bản phát hành hiện chỉ có Paid/Voided, vì vậy hoá đơn huỷ/hoàn toàn phần phải ở trạng thái Voided để không tính.
/// Bàn đã checkout mang trạng thái Cleaning và không được tính là đang phục vụ.
/// </summary>
public sealed record DailyRevenueSummary(DateOnly BusinessDate, long InvoiceCount, long GuestCount, decimal Revenue,
    decimal AverageInvoice, long ServingTableCount)
{
    public bool HasTransactions => InvoiceCount > 0;

    public static DailyRevenueSummary Calculate(DateOnly day, IEnumerable<DailyRevenueInvoice> invoices, long servingTableCount)
    {
        var paid = invoices.Where(invoice => invoice.Status == "Paid").ToArray();
        var revenue = paid.Sum(invoice => invoice.Total);
        var invoiceCount = (long)paid.Length;
        var guestCount = paid.Sum(invoice => (long)invoice.GuestCount);
        return new DailyRevenueSummary(day, invoiceCount, guestCount, revenue,
            invoiceCount == 0 ? 0 : revenue / invoiceCount, servingTableCount);
    }
}
