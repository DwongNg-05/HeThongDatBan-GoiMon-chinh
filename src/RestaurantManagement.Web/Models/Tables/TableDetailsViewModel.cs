namespace RestaurantManagement.Web.Models;

public sealed record UpcomingTableReservation(
    string CustomerName,
    string Phone,
    int GuestCount,
    DateTimeOffset StartsAtUtc);

public sealed record TableDetailsViewModel(
    string Code,
    string Area,
    int Capacity,
    string Status,
    string StatusLabel,
    string StatusClass,
    string? CurrentGuestName,
    string? CurrentGuestPhone,
    int? CurrentGuestCount,
    UpcomingTableReservation? UpcomingReservation,
    DateTimeOffset? ServiceStartedAtUtc,
    long? ServiceElapsedMinutes,
    decimal? CurrentSubtotal,
    bool HasActiveSession,
    bool IsDemoData)
{
    public int UpcomingReservationCount { get; init; }
    public TableGroupInfo? Group { get; init; }
    public DateTimeOffset SyncedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string SubtotalExplanation => "Tổng món hiện tại, chưa trừ giảm giá hoặc phụ thu.";

    // ---- Gọi thêm món giữa bữa vào đúng phiên đang mở (không mở hoá đơn thứ hai) ----

    /// <summary>Phiên phục vụ hiện tại của bàn (dbo.DiningSessions.Id); null khi bàn không có phiên.</summary>
    public long? SessionId { get; init; }

    /// <summary>"Open" | "AwaitingPayment" | null. Chỉ phiên "Open" mới nhận thêm món ngay.</summary>
    public string? SessionStatus { get; init; }

    public bool CanAddItems => HasActiveSession && SessionId is not null && SessionStatus == "Open" && !IsDemoData;

    public bool IsAwaitingPayment => HasActiveSession && SessionStatus == "AwaitingPayment";

    /// <summary>Các món đã gọi trong phiên, theo đợt gọi.</summary>
    public IReadOnlyList<TableDetailsOrderItem> OrderedItems { get; init; } = [];

    public int BatchCount => OrderedItems.Select(item => item.BatchNumber).Distinct().Count();

    /// <summary>Đường dẫn màn hình "Gọi thêm món" của nhân viên, gắn với phiên hiện tại (do controller điền).</summary>
    public string? AddItemsUrl { get; init; }

    /// <summary>Ảnh mã QR mở <see cref="AddItemsUrl"/> trên điện thoại nhân viên (do controller điền).</summary>
    public string? AddItemsQrImageUrl { get; init; }
}

public sealed record TableDetailsOrderItem(
    int BatchNumber,
    DateTimeOffset SubmittedAtUtc,
    string ItemName,
    string Unit,
    int Quantity,
    decimal LineTotal,
    string Status,
    bool Charged)
{
    public string OriginalTableCode { get; init; } = string.Empty;
    public string StatusLabel => Status switch
    {
        "Pending" => "Chờ chế biến",
        "Preparing" => "Đang chế biến",
        "Ready" => "Đã làm xong",
        "Served" => "Đã phục vụ",
        "Cancelled" => Charged ? "Đã huỷ (vẫn tính tiền)" : "Đã huỷ",
        _ => Status
    };
}
