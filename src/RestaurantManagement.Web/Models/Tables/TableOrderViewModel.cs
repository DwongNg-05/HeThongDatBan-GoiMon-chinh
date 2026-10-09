using RestaurantManagement.Web.Services;
using RestaurantManagement.Web.Models.Reservations;

namespace RestaurantManagement.Web.Models.Tables;

/// <summary>S3-01 Task 1: trang gọi món của khách sau khi quét QR bàn (GET /TableOrder).</summary>
public sealed record TableOrderViewModel(GuestOrderingContext Table, IReadOnlyList<PublicMenuCategory> Categories)
{
    public bool MenuIsEmpty => Categories.All(category => category.Dishes.Count == 0);

    public string CapacityLabel => Table.MinCapacity == Table.MaxCapacity
        ? $"{Table.MaxCapacity} chỗ"
        : $"{Table.MinCapacity} – {Table.MaxCapacity} chỗ";

    public string TableTypeLabel => Table.TableType == "PrivateRoom" ? "Phòng riêng" : "Bàn thường";

    /// <summary>Giờ mở phiên theo giờ Việt Nam, ví dụ "18:42".</summary>
    public string OpenedAtLabel => VietnamTime.FromUtc(Table.OpenedAtUtc).ToString("HH:mm");

    /// <summary>Mã yêu cầu cho lần bấm "Đặt món" kế tiếp: bấm lặp / gửi lại chỉ tạo một lượt gọi.</summary>
    public Guid RequestId { get; init; } = Guid.NewGuid();

    /// <summary>Các món đã đặt của phiên (mới nhất ở cuối).</summary>
    public IReadOnlyList<GuestOrderedItem> OrderedItems { get; init; } = [];

    /// <summary>Thông báo sau khi bấm "Đặt món".</summary>
    public string? InfoMessage { get; init; }
    public string? SuccessMessage { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>Tạm tính các món đã đặt (không gồm món đã huỷ không tính tiền).</summary>
    public decimal OrderedTotal => OrderedItems.Where(item => item.Charged).Sum(item => item.LineTotal);

    public IEnumerable<IGrouping<int, GuestOrderedItem>> OrderedBatches => OrderedItems.GroupBy(item => item.BatchNumber);

    public static string Money(decimal amount) => MoneyFormat.Vnd((long)amount);

    public static string TimeLabel(DateTime utc) => VietnamTime.FromUtc(utc).ToString("HH:mm");
}
