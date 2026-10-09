namespace RestaurantManagement.Web.Models;

public sealed class SessionOrderingViewModel
{
    public int TableId { get; set; }
    public string TableCode { get; set; } = string.Empty;

    public long SessionId { get; set; }
    public string SessionStatus { get; set; } = string.Empty;

    public decimal Subtotal { get; set; }

    public bool CanAddItems => SessionStatus == "Open";

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