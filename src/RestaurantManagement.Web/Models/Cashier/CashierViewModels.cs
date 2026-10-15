namespace RestaurantManagement.Web.Models.Cashier;

/// <summary>Ca đang mở hoặc vừa chốt. Tiền tính bằng VND.</summary>
public sealed record ShiftSummary(
    long Id,
    string Name,
    DateTime BusinessDate,
    DateTime OpenedAt,
    decimal OpeningCash,
    string Status,
    int InvoiceCount,
    decimal CashRevenue,
    decimal TransferRevenue,
    DateTime? ClosedAt,
    decimal? CountedCash,
    decimal? ExpectedCash,
    decimal? CashDifference,
    string? Explanation)
{
    public decimal Revenue => CashRevenue + TransferRevenue;

    public decimal DiscountTotal { get; init; }

    public int ServedTableCount { get; init; }

    public decimal ExpectedCashNow => OpeningCash + CashRevenue;
}

/// <summary>Một phiên phục vụ chờ thanh toán.</summary>
public sealed record PayableSession(
    long SessionId,
    string Tables,
    int GuestCount,
    string Status,
    DateTime OpenedAt,
    int ItemCount,
    int UnservedCount,
    decimal Subtotal)
{
    public bool CanCheckout => UnservedCount == 0 && ItemCount > 0;

    public string StatusLabel => Status == "AwaitingPayment"
        ? "Đã yêu cầu thanh toán"
        : "Đang phục vụ";
}

public sealed record PaymentsViewModel(
    ShiftSummary? OpenShift,
    IReadOnlyList<PayableSession> Sessions);

/// <summary>Một hoá đơn trong ngày.</summary>
public sealed record InvoiceRow(
    long Id,
    string InvoiceNumber,
    string Tables,
    int GuestCount,
    DateTime IssuedAt,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal Total,
    string Method,
    string Status)
{
    public string MethodLabel => Method == "Cash"
        ? "Tiền mặt"
        : "Chuyển khoản";

    public string StatusLabel => Status == "Paid"
        ? "Đã thanh toán"
        : "Đã huỷ";
}

public sealed record InvoicesViewModel(
    DateOnly BusinessDate,
    IReadOnlyList<InvoiceRow> Invoices)
{
    public decimal PaidTotal => Invoices
        .Where(invoice => invoice.Status == "Paid")
        .Sum(invoice => invoice.Total);
}

/// <summary>Phiên hoặc hoá đơn khiến ca chưa được phép chốt.</summary>
public sealed record ShiftBlocker(
    long SessionId,
    string Tables,
    string Status,
    string? InvoiceNumber,
    string Reason)
{
    public string StatusLabel => Status switch
    {
        "Open" => "Đang phục vụ",
        "AwaitingPayment" => "Chờ thanh toán",
        "Closed" => "Đã kết thúc phiên",
        _ => Status
    };
}

public sealed record ShiftViewModel(
    ShiftSummary? OpenShift,
    ShiftSummary? LastClosed,
    int OpenSessionCount)
{
    public IReadOnlyList<ShiftBlocker> Blockers { get; init; }
        = Array.Empty<ShiftBlocker>();

    public bool CanClose =>
        OpenShift is not null
        && OpenSessionCount == 0
        && Blockers.Count == 0;
}

public static class Vnd
{
    public static string Format(decimal value) =>
        value.ToString(
            "#,0",
            System.Globalization.CultureInfo.GetCultureInfo("vi-VN"))
        + " ₫";
}