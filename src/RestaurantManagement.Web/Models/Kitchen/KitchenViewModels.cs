namespace RestaurantManagement.Web.Models.Kitchen;

/// <summary>
/// Một món trên màn hình bếp (dbo.OrderItems của phiên chưa đóng). Mỗi dòng đơn là một dòng bếp: hai dòng cùng món
/// nhưng khác ghi chú hiển thị riêng. S3-03 Task 3: <paramref name="QuickNotes"/> là lựa chọn nhanh đã lưu
/// (dbo.OrderItems.QuickNotes, ví dụ "Ít cay, Không hành"), hiển thị cùng ghi chú tự do ngay dưới tên món.
/// </summary>
public sealed record KitchenOrderItem(long Id, string TableCode, string ItemName, string Unit, int Quantity, string? Notes,
    string Status, DateTime SubmittedAt, int EstimatedPrepMinutes, string? QuickNotes = null)
{
    /// <summary>Yêu cầu riêng của dòng — cùng cách đọc với hoá đơn tạm tính (S3-04 Task 4) để hai nơi luôn khớp.</summary>
    public RestaurantManagement.Web.Models.Orders.OrderItemRequests Requests =>
        RestaurantManagement.Web.Models.Orders.OrderItemRequests.From(QuickNotes, Notes);

    /// <summary>Các lựa chọn nhanh, mỗi lựa chọn một nhãn riêng ("Ít cay", "Không hành"…).</summary>
    public IReadOnlyList<string> QuickNoteLabels => Requests.QuickLabels;

    /// <summary>Ghi chú tự do đầy đủ (không cắt), hoặc null.</summary>
    public string? FreeNote => Requests.FreeNote;

    /// <summary>Dòng có yêu cầu riêng của khách (lựa chọn nhanh hoặc ghi chú) cần bếp đọc.</summary>
    public bool HasRequests => Requests.HasAny;

    public long SessionId { get; init; }
    public long BatchId { get; init; }
    public int BatchNumber { get; init; }
    public DateTime BatchSubmittedAt { get; init; }

    public string StatusLabel => Status switch
    {
        "Pending" => "Chờ nấu",
        "Preparing" => "Đang nấu",
        "Ready" => "Xong, chờ mang ra",
        _ => Status
    };

    /// <summary>Bước tiếp theo bếp được làm: Chờ nấu → Đang nấu → Xong. Mang món ra là việc của Phục vụ.</summary>
    public string? NextStatus => Status switch { "Pending" => "Preparing", "Preparing" => "Ready", _ => null };

    public string? NextLabel => Status switch { "Pending" => "Bắt đầu nấu", "Preparing" => "Đã xong", _ => null };
}

/// <summary>S2-08 Task 1: danh sách món trong ngày, gom theo nhóm món.</summary>
public sealed record KitchenOrderBatch(
    long SessionId,
    long BatchId,
    int BatchNumber,
    DateTime SubmittedAt,
    IReadOnlyList<KitchenOrderItem> Items)
{
    public string Label => $"Đợt {BatchNumber}";

    public string TableCodes =>
        string.Join(", ", Items.Select(i => i.TableCode).Distinct());
}

public sealed record KitchenScreenViewModel(
    IReadOnlyList<KitchenOrderItem> Items,
    bool CanChangeStatus)
{
    public IReadOnlyList<KitchenOrderBatch> Batches => Items
        .GroupBy(i => new
        {
            i.SessionId,
            i.BatchId,
            i.BatchNumber,
            i.BatchSubmittedAt
        })
        .Select(g => new KitchenOrderBatch(
            g.Key.SessionId,
            g.Key.BatchId,
            g.Key.BatchNumber,
            g.Key.BatchSubmittedAt,
            g.OrderBy(i => i.Id).ToArray()))
        .OrderBy(b => b.SubmittedAt)
        .ThenBy(b => b.SessionId)
        .ThenBy(b => b.BatchNumber)
        .ThenBy(b => b.BatchId)
        .ToArray();
}

public sealed record DailyDishesViewModel(
    IReadOnlyList<RestaurantManagement.Web.Services.DailyDish> Dishes)
{
    public int UnavailableCount => Dishes.Count(d => d.IsUnavailable);

    public IEnumerable<IGrouping<string,
        RestaurantManagement.Web.Services.DailyDish>> ByCategory =>
        Dishes.GroupBy(d => d.CategoryName);
}

public sealed record TemporaryOutHistoryViewModel(
    int? DishId,
    string? DishName,
    IReadOnlyList<RestaurantManagement.Web.Services.DailyDish> Dishes,
    IReadOnlyList<RestaurantManagement.Web.Services.TemporaryOutLogEntry> Entries);