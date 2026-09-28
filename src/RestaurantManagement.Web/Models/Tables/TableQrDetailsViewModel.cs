namespace RestaurantManagement.Web.Models.Tables;

public sealed class TableQrDetailsViewModel
{
    public int TableId { get; init; }
    public required string TableCode { get; init; }
    public required string AreaName { get; init; }
    public int MinCapacity { get; init; }
    public int MaxCapacity { get; init; }
    public required string TableType { get; init; }
    public required string Status { get; init; }
    public bool IsActive { get; init; }
    public bool HasQr { get; init; }
    public string? QrUrl { get; init; }
    public DateTime? CreatedAt { get; init; }

    public string CapacityLabel => MinCapacity == MaxCapacity
        ? $"{MaxCapacity} chỗ"
        : $"{MinCapacity} – {MaxCapacity} chỗ";

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
