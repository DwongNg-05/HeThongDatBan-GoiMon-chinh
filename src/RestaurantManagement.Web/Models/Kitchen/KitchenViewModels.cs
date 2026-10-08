namespace RestaurantManagement.Web.Models.Kitchen;

/// <summary>Một món trên màn hình bếp (dbo.OrderItems của phiên chưa đóng).</summary>
public sealed record KitchenOrderItem(long Id, string TableCode, string ItemName, string Unit, int Quantity, string? Notes,
    string Status, DateTime SubmittedAt, int EstimatedPrepMinutes)
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

public sealed record KitchenScreenViewModel(IReadOnlyList<KitchenOrderItem> Items, bool CanChangeStatus);
