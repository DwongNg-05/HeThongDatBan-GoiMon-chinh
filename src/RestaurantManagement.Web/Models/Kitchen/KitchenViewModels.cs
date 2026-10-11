namespace RestaurantManagement.Web.Models.Kitchen;

/// <summary>Một món trong phiếu bếp.</summary>
public sealed record KitchenOrderItem(long Id, string ItemName, string Unit, int Quantity, string? Notes,
    string Status, int EstimatedPrepMinutes)
{
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
public sealed record DailyDishesViewModel(IReadOnlyList<RestaurantManagement.Web.Services.DailyDish> Dishes)
{
    public int UnavailableCount => Dishes.Count(d => d.IsUnavailable);
    public IEnumerable<IGrouping<string, RestaurantManagement.Web.Services.DailyDish>> ByCategory => Dishes.GroupBy(d => d.CategoryName);
}

/// <summary>S2-08 Task 2: lịch sử bật/tắt "Tạm hết" (mới nhất trước), có thể lọc theo một món.</summary>
public sealed record TemporaryOutHistoryViewModel(int? DishId, string? DishName,
    IReadOnlyList<RestaurantManagement.Web.Services.DailyDish> Dishes,
    IReadOnlyList<RestaurantManagement.Web.Services.TemporaryOutLogEntry> Entries);

/// <summary>Một đợt gọi món tại một bàn; BatchNumber là số đợt trong phiên phục vụ.</summary>
public sealed record KitchenTicket(long BatchId, string TableCode, int BatchNumber, DateTime ReceivedAt,
    IReadOnlyList<KitchenOrderItem> Items)
{
    public string StatusLabel => Items.All(item => item.Status == "Ready") ? "Đã chế biến xong"
        : Items.All(item => item.Status == "Pending") ? "Chờ chế biến"
        : Items.All(item => item.Status == "Preparing") ? "Đang chế biến"
        : "Đang xử lý";
}

public sealed record KitchenScreenViewModel(IReadOnlyList<KitchenTicket> Tickets, bool CanChangeStatus);

public sealed record KitchenTicketRow(long BatchId, string TableCode, int BatchNumber, DateTime ReceivedAt, KitchenOrderItem Item);

public static class KitchenTicketOrdering
{
    /// <summary>Groups an order submission into one table ticket and orders oldest first, using batch ID to break timestamp ties.</summary>
    public static IReadOnlyList<KitchenTicket> Group(IEnumerable<KitchenTicketRow> rows) => rows
        .GroupBy(row => row.BatchId)
        .Select(group => new KitchenTicket(group.Key, group.First().TableCode, group.First().BatchNumber,
            group.First().ReceivedAt, group.Select(row => row.Item).ToArray()))
        .OrderBy(ticket => ticket.ReceivedAt).ThenBy(ticket => ticket.BatchId).ToArray();
}
