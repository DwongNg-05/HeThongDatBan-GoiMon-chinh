namespace RestaurantManagement.Web.Models.Tables;

/// <summary>
/// Thông tin an toàn hiển thị cho khách khi quét mã QR của bàn.
/// </summary>
public sealed class TableQrScanViewModel
{
    public required string QrToken { get; init; }
    public required string TableCode { get; init; }
    public required string AreaName { get; init; }
    public int MinCapacity { get; init; }
    public int MaxCapacity { get; init; }
    public required string TableType { get; init; }

    public string TableTypeLabel => TableType switch
    {
        "PrivateRoom" => "Phòng riêng",
        _ => "Bàn thường"
    };

    public string CapacityLabel => MinCapacity == MaxCapacity
        ? $"{MaxCapacity} chỗ"
        : $"{MinCapacity} – {MaxCapacity} chỗ";
}
