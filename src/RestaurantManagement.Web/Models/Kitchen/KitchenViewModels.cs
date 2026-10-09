namespace RestaurantManagement.Web.Models.Kitchen;

public sealed record KitchenOrderItem(
    long Id,
    string TableCode,
    string ItemName,
    string Unit,
    int Quantity,
    string? Notes,
    string Status,
    DateTime SubmittedAt,
    int EstimatedPrepMinutes)
{
    public long SessionId { get; init; }
    public long BatchId { get; init; }
    public int BatchNumber { get; init; }
    public DateTime BatchSubmittedAt { get; init; }

    public string StatusLabel => Status switch
    {
        "Pending" => "Chờ nấu",
        "Preparing" => "Đang nấu",
        "Ready" => "Xong, chờ mang ra",
        "Served" => "Đã phục vụ",
        "Cancelled" => "Đã huỷ",
        _ => Status
    };

    public string? NextStatus => Status switch
    {
        "Pending" => "Preparing",
        "Preparing" => "Ready",
        _ => null
    };

    public string? NextLabel => Status switch
    {
        "Pending" => "Bắt đầu nấu",
        "Preparing" => "Đã xong",
        _ => null
    };
}

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