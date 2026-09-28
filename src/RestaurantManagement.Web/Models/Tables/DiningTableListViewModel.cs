using Microsoft.AspNetCore.Mvc.Rendering;

namespace RestaurantManagement.Web.Models.Tables;

public sealed class DiningTableListViewModel
{
    public List<DiningTableListItemViewModel> Tables { get; } = [];
    public IReadOnlyList<SelectListItem> Areas { get; set; } = [];
}

public sealed class DiningTableListItemViewModel
{
    public int Id { get; init; }
    public required string Code { get; init; }
    public required string AreaName { get; init; }
    public int MinCapacity { get; init; }
    public int MaxCapacity { get; init; }
    public required string TableType { get; init; }
    public required string Status { get; init; }
    public bool IsActive { get; init; }

    public string TableTypeLabel => TableType == "PrivateRoom" ? "Phòng riêng" : "Bàn thường";
    public string StatusLabel => Status switch
    {
        "Available" => "Trống",
        "Reserved" => "Đã đặt trước",
        "Serving" => "Đang phục vụ",
        "Cleaning" => "Đang dọn",
        _ => Status
    };
}
