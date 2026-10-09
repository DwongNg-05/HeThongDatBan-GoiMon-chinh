namespace RestaurantManagement.Web.Models.Tables;

/// <summary>
/// Thông tin an toàn hiển thị cho khách khi quét mã QR của bàn.
/// </summary>
public sealed class TableQrScanViewModel
{
    public required string TableCode { get; init; }
    public required string AreaName { get; init; }
    public int MinCapacity { get; init; }
    public int MaxCapacity { get; init; }
    public required string TableType { get; init; }

    /// <summary>S3-01 Task 1: mã QR công khai, dùng cho nút/biểu mẫu "Bắt đầu gọi món" (POST /q/{token}).</summary>
    public string Token { get; init; } = string.Empty;

    /// <summary>
    /// S3-01 Task 1: khách (chưa đăng nhập) được đưa thẳng vào trang gọi món — trình duyệt tự gửi biểu mẫu.
    /// Nhân viên đang đăng nhập (ví dụ bấm "Mở thử QR") phải tự bấm nút, để không vô tình mở bàn.
    /// </summary>
    public bool AutoStart { get; init; }

    public string TableTypeLabel => TableType switch
    {
        "PrivateRoom" => "Phòng riêng",
        _ => "Bàn thường"
    };

    public string CapacityLabel => MinCapacity == MaxCapacity
        ? $"{MaxCapacity} chỗ"
        : $"{MinCapacity} – {MaxCapacity} chỗ";
}
