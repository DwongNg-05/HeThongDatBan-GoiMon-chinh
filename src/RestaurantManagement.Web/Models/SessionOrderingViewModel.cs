namespace RestaurantManagement.Web.Models;

public sealed class SessionOrderingViewModel
{
    public int TableId { get; set; }
    public string TableCode { get; set; } = string.Empty;

    // Phiên chứa các đợt gọi món của bàn.
    public long SessionId { get; set; }

    // Phiên thanh toán chính; có thể khác SessionId khi ghép bàn.
    public long BillingSessionId { get; set; }

    public string SessionStatus { get; set; } = string.Empty;

    public decimal Subtotal { get; set; }

    public bool CanAddItems => SessionStatus == "Open";

    // Đây là điều kiện trạng thái; quyền nhân viên kiểm tra riêng.
    public bool IsAwaitingPayment =>
        SessionStatus == "AwaitingPayment";

    public List<SessionOrderItemViewModel> Items { get; set; } = [];
}

public sealed class SessionOrderItemViewModel
{
    public long Id { get; set; }
    public int BatchNumber { get; set; }
    public DateTime SubmittedAt { get; set; }

    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;

    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }

    public string Status { get; set; } = string.Empty;
    public bool ChargeWhenCancelled { get; set; }
}