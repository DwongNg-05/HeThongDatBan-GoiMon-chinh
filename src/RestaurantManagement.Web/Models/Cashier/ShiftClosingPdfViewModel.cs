namespace RestaurantManagement.Web.Models.Cashier;

/// <summary>
/// Dữ liệu đọc từ bản chốt đã lưu trong ShiftEvents.SnapshotJson.
/// Không lấy số liệu hiện tại để thay thế số liệu bản chốt cũ.
/// </summary>
public sealed class ShiftClosingSnapshot
{
    public int SnapshotVersion { get; set; }

    public long ShiftId { get; set; }
    public string ShiftName { get; set; } = string.Empty;
    public DateTime BusinessDate { get; set; }
    public DateTime OpenedAt { get; set; }

    public long ClosingNumber { get; set; }
    public long ClosingEventId { get; set; }

    public DateTime ClosedAt { get; set; }
    public int ClosedBy { get; set; }
    public string ClosedByName { get; set; } = string.Empty;

    public string StatusAtClosing { get; set; } = string.Empty;

    public string RestaurantName { get; set; } = string.Empty;
    public string RestaurantAddress { get; set; } = string.Empty;
    public string RestaurantPhone { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = string.Empty;

    public decimal OpeningCash { get; set; }

    public int InvoiceCount { get; set; }
    public decimal Revenue { get; set; }
    public decimal CashRevenue { get; set; }
    public decimal TransferRevenue { get; set; }
    public decimal DiscountTotal { get; set; }
    public int ServedTableCount { get; set; }

    public decimal CountedCash { get; set; }
    public decimal ExpectedCash { get; set; }
    public decimal CashDifference { get; set; }

    public string? Explanation { get; set; }
}

/// <summary>Một lần chốt trong danh sách bản chốt ca.</summary>
public sealed record ShiftClosingListItem(
    long EventId,
    long ShiftId,
    long ClosingNumber,
    DateTime ClosedAt,
    string ClosedByName,
    bool HasCompleteSnapshot,
    bool WasReopened);

/// <summary>Màn hình xem một bản chốt đã lưu.</summary>
public sealed class ShiftClosingPdfViewModel
{
    public long ShiftId { get; init; }

    public IReadOnlyList<ShiftClosingListItem> Closings
    {
        get;
        init;
    } = [];

    public ShiftClosingSnapshot? SelectedClosing { get; init; }

    public bool WasReopened { get; init; }

    public string? ErrorMessage { get; init; }

    public string ClosingStatusLabel => WasReopened
        ? "Bản chốt lưu trữ — ca đã được mở lại sau lần chốt này"
        : "Bản chốt đã lưu";
}