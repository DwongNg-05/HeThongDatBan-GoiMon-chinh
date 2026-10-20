namespace RestaurantManagement.Web.Models.Cashier;

/// <summary>Ca đang mở hoặc vừa chốt (dbo.Shifts). Tiền tính bằng VND.</summary>
public sealed record ShiftSummary(long Id, string Name, DateTime BusinessDate, DateTime OpenedAt, decimal OpeningCash, string Status,
    int InvoiceCount, decimal CashRevenue, decimal TransferRevenue, DateTime? ClosedAt, decimal? CountedCash, decimal? ExpectedCash,
    decimal? CashDifference, string? Explanation)
{
    public decimal Revenue => CashRevenue + TransferRevenue;
    public decimal DiscountTotal { get; init; }
    public int ServedTableCount { get; init; }
    public decimal ExpectedCashNow => OpeningCash + CashRevenue;
}

/// <summary>Một bàn (phiên phục vụ) chờ thanh toán.</summary>
public sealed record PayableSession(long SessionId, string Tables, int GuestCount, string Status, DateTime OpenedAt,
    int ItemCount, int UnservedCount, decimal Subtotal)
{
    public string SessionVersion { get; init; } = string.Empty;
    public bool CanCheckout => UnservedCount == 0 && ItemCount > 0;
    public string StatusLabel => Status == "AwaitingPayment" ? "Đã yêu cầu thanh toán" : "Đang phục vụ";
}

public sealed record PaymentsViewModel(ShiftSummary? OpenShift, IReadOnlyList<PayableSession> Sessions);

/// <summary>
/// S4-02 Task 1: thông tin chỉ đọc trước khi thu ngân chốt tiền mặt. Thành tiền
/// luôn lấy từ database; trình duyệt không có trường nào để sửa giá hoặc tổng tiền.
/// </summary>
public sealed record CashierPaymentLine(string ItemName, string Unit, int Quantity, decimal UnitPrice,
    decimal LineTotal, string? Notes, string OriginalTableCode, bool IsChargedCancellation);

/// <summary>Giảm giá đang áp dụng cho một phiên, được máy chủ tính lại trước khi chốt.</summary>
public sealed record SessionDiscount(string Type, decimal Value, decimal Amount, string Reason, DateTime UpdatedAt)
{
    public string TypeLabel => Type == "Percent" ? $"{Value:0.##}%" : "Theo số tiền";
}

/// <summary>Bản ghi lịch sử thay đổi giảm giá; chỉ dùng để minh bạch khi đối soát.</summary>
public sealed record DiscountHistoryEntry(string Action, string? Reason, DateTime OccurredAt)
{
    public string ActionLabel => Action switch
    {
        "SessionDiscountApplied" => "Đã áp dụng giảm giá",
        "SessionDiscountChanged" => "Đã thay đổi giảm giá",
        "SessionDiscountRemoved" => "Đã gỡ giảm giá",
        _ => Action
    };
}

/// <summary>Thông tin QR chuyển khoản được sinh theo đúng tổng tiền của hóa đơn đang chốt.</summary>
public sealed record BankTransferQr(string BankCode, string AccountNumber, string AccountName, string Reference, string ImageUrl);

public sealed record CashierPaymentViewModel(
    PayableSession Session,
    IReadOnlyList<CashierPaymentLine> Lines,
    string Method,
    decimal CashReceived,
    string? TransferReference,
    Guid RequestId,
    SessionDiscount? Discount,
    IReadOnlyList<DiscountHistoryEntry> DiscountHistory,
    BankTransferQr? TransferQr,
    string? Error = null)
{
    public decimal Total => Session.Subtotal - (Discount?.Amount ?? 0);
    public decimal Change => Math.Max(0, CashReceived - Total);
    public bool HasEnoughCash => CashReceived >= Total;
}

/// <summary>Hoá đơn tiền mặt đã chốt. Giá và số lượng là bản chụp tại thời điểm chốt.</summary>
public sealed record CashierReceiptViewModel(
    long InvoiceId,
    string InvoiceNumber,
    string Tables,
    int GuestCount,
    DateTime IssuedAt,
    string CashierName,
    decimal Subtotal,
    decimal Total,
    string Method,
    decimal? CashReceived,
    decimal? ChangeAmount,
    string? TransferReference,
    IReadOnlyList<CashierPaymentLine> Lines);

/// <summary>Một hoá đơn trong ngày (dbo.Invoices + dbo.Payments).</summary>
public sealed record InvoiceRow(long Id, string InvoiceNumber, string Tables, int GuestCount, DateTime IssuedAt, decimal Subtotal,
    decimal DiscountAmount, decimal Total, string Method, string Status, string? VoidReason, string? VoidedBy, DateTime? VoidedAt)
{
    public string MethodLabel => Method == "Cash" ? "Tiền mặt" : "Chuyển khoản";
    public string StatusLabel => Status == "Paid" ? "Đã thanh toán" : "Đã huỷ";
}

public sealed record InvoicesViewModel(DateOnly BusinessDate, IReadOnlyList<InvoiceRow> Invoices)
{
    public decimal PaidTotal => Invoices.Where(i => i.Status == "Paid").Sum(i => i.Total);
}

/// <summary>
/// Chi tiết hóa đơn trong lịch sử. Khác với biên lai vừa thanh toán, màn hình này
/// vẫn hiển thị được cả hóa đơn đã bị hủy để Quản lý/Thu ngân đối soát minh bạch.
/// </summary>
public sealed record InvoiceHistoryDetailsViewModel(
    long InvoiceId,
    string InvoiceNumber,
    string Tables,
    int GuestCount,
    DateTime IssuedAt,
    DateOnly BusinessDate,
    string CashierName,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal Total,
    string Method,
    decimal? CashReceived,
    decimal? ChangeAmount,
    string? TransferReference,
    string Status,
    string? VoidReason,
    string? VoidedBy,
    DateTime? VoidedAt,
    IReadOnlyList<CashierPaymentLine> Lines)
{
    public bool IsVoided => Status == "Voided";
    public string MethodLabel => Method == "Transfer" ? "Chuyển khoản" : "Tiền mặt";
    public string StatusLabel => IsVoided ? "Đã huỷ" : "Đã thanh toán";
}

/// <summary>Phiên hoặc hoá đơn khiến ca chưa được phép chốt.</summary>
public sealed record ShiftBlocker(long SessionId, string Tables, string Status, string? InvoiceNumber, string Reason)
{
    public string StatusLabel => Status switch
    {
        "Open" => "Đang phục vụ",
        "AwaitingPayment" => "Chờ thanh toán",
        "Closed" => "Đã kết thúc phiên",
        _ => Status
    };
}

public sealed record ShiftViewModel(ShiftSummary? OpenShift, ShiftSummary? LastClosed, int OpenSessionCount)
{
    public IReadOnlyList<ShiftBlocker> Blockers { get; init; } = Array.Empty<ShiftBlocker>();
    public bool CanClose => OpenShift is not null && OpenSessionCount == 0 && Blockers.Count == 0;
}

public static class Vnd
{
    public static string Format(decimal value) => value.ToString("#,0", System.Globalization.CultureInfo.GetCultureInfo("vi-VN")) + " ₫";
}
