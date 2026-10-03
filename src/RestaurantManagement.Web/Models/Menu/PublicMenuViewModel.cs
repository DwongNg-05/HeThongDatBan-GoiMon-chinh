using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Models;

/// <summary>Dữ liệu trang thực đơn công khai (/Menu), kèm từ khoá tìm món (S2-01 Task 2).</summary>
public sealed record PublicMenuViewModel(
    string Keyword,
    IReadOnlyList<PublicMenuCategory> Categories,
    bool MenuIsEmpty)
{
    /// <summary>Khách đang tìm món (từ khoá khác rỗng).</summary>
    public bool IsSearching => Keyword.Length > 0;

    /// <summary>Số món tìm thấy (hoặc tổng số món khi không tìm).</summary>
    public int DishCount => Categories.Sum(c => c.Dishes.Count);

    /// <summary>Đang tìm nhưng không có món nào khớp.</summary>
    public bool NoResults => IsSearching && DishCount == 0;
}
