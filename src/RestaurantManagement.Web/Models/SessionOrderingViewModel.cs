namespace RestaurantManagement.Web.Models;

public sealed class SessionOrderingViewModel
{
    public TableGroupInfo? Group { get; set; }
    public int TableId { get; set; }
    public string TableCode { get; set; } = string.Empty;

    // Thông tin hiển thị ở đầu màn hình gọi món (giống màn hình gọi món tại bàn của khách).
    public string AreaName { get; set; } = string.Empty;
    public int MinCapacity { get; set; }
    public int MaxCapacity { get; set; }
    public string TableType { get; set; } = string.Empty;
    public DateTime? OpenedAtUtc { get; set; }

    public string CapacityLabel => MinCapacity <= 0 || MinCapacity == MaxCapacity
        ? $"{MaxCapacity} chỗ"
        : $"{MinCapacity} – {MaxCapacity} chỗ";

    public string TableTypeLabel => TableType == "PrivateRoom" ? "Phòng riêng" : "Bàn thường";

    /// <summary>Giờ mở phiên theo giờ Việt Nam (UTC+7), ví dụ "13:34".</summary>
    public string? OpenedAtLabel => OpenedAtUtc is null
        ? null
        : new DateTimeOffset(DateTime.SpecifyKind(OpenedAtUtc.Value, DateTimeKind.Utc))
            .ToOffset(TimeSpan.FromHours(7)).ToString("HH:mm");

    public int BatchCount => Items.Select(item => item.BatchNumber).Distinct().Count();

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
    public string OriginalTableCode { get; set; } = string.Empty;
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
