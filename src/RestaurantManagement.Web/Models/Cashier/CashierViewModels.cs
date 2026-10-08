namespace RestaurantManagement.Web.Models.Cashier;

/// <summary>Ca đang mở hoặc vừa chốt (dbo.Shifts). Tiền tính bằng VND.</summary>
public sealed record ShiftSummary(long Id, string Name, DateTime BusinessDate, DateTime OpenedAt, decimal OpeningCash, string Status,
    int InvoiceCount, decimal CashRevenue, decimal TransferRevenue, DateTime? ClosedAt, decimal? CountedCash, decimal? ExpectedCash,
    decimal? CashDifference, string? Explanation)
{
    public decimal ExpectedCashNow => OpeningCash + CashRevenue;
}

/// <summary>Một bàn (phiên phục vụ) chờ thanh toán.</summary>
public sealed record PayableSession(long SessionId, string Tables, int GuestCount, string Status, DateTime OpenedAt,
    int ItemCount, int UnservedCount, decimal Subtotal)
{
    public bool CanCheckout => UnservedCount == 0 && ItemCount > 0;
    public string StatusLabel => Status == "AwaitingPayment" ? "Đã yêu cầu thanh toán" : "Đang phục vụ";
}

public sealed record PaymentsViewModel(ShiftSummary? OpenShift, IReadOnlyList<PayableSession> Sessions);

/// <summary>Một hoá đơn trong ngày (dbo.Invoices + dbo.Payments).</summary>
public sealed record InvoiceRow(long Id, string InvoiceNumber, string Tables, int GuestCount, DateTime IssuedAt, decimal Subtotal,
    decimal DiscountAmount, decimal Total, string Method, string Status)
{
    public string MethodLabel => Method == "Cash" ? "Tiền mặt" : "Chuyển khoản";
    public string StatusLabel => Status == "Paid" ? "Đã thanh toán" : "Đã huỷ";
}

public sealed record InvoicesViewModel(DateOnly BusinessDate, IReadOnlyList<InvoiceRow> Invoices)
{
    public decimal PaidTotal => Invoices.Where(i => i.Status == "Paid").Sum(i => i.Total);
}

public sealed record ShiftViewModel(ShiftSummary? OpenShift, ShiftSummary? LastClosed, int OpenSessionCount);

public static class Vnd
{
    public static string Format(decimal value) => value.ToString("#,0", System.Globalization.CultureInfo.GetCultureInfo("vi-VN")) + " ₫";
}

public sealed record InvoiceLineDetail(long OrderItemId, string ItemName, string Unit, int Quantity,
    decimal UnitPrice, decimal LineTotal, string TableCode, bool IsChargedCancellation);
public sealed record InvoiceDetailsViewModel(InvoiceRow Invoice, IReadOnlyList<InvoiceLineDetail> Lines);
