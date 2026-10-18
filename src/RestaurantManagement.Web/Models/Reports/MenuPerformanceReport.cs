namespace RestaurantManagement.Web.Models.Reports;

/// <summary>Một món trong bảng xếp hạng theo số lượng hoặc doanh thu gộp (trước giảm giá hóa đơn).</summary>
public sealed record MenuPerformanceItem(int MenuItemId, string Name, bool IsActive, long Quantity, decimal GrossRevenue);

/// <summary>Một món bị hủy; lý do là lý do xuất hiện nhiều nhất trong khoảng đang xem.</summary>
public sealed record CancelledMenuItem(int MenuItemId, string Name, long Quantity, string? MostCommonReason)
{
    public string ReasonLabel => MostCommonReason switch
    {
        "ChangedMind" => "Khách đổi ý",
        "Mistake" => "Gọi nhầm món",
        "SoldOut" => "Tạm hết món",
        "ManagerOverride" => "Quản lý yêu cầu",
        null or "" => "Chưa có lý do",
        _ => MostCommonReason
    };
}

/// <summary>S4-06: số liệu chỉ đọc, được database tổng hợp trong usp_ReportTopItems.</summary>
public sealed record MenuPerformanceReport(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<MenuPerformanceItem> BestByQuantity,
    IReadOnlyList<MenuPerformanceItem> BestByRevenue,
    IReadOnlyList<CancelledMenuItem> MostCancelled);

public sealed record MenuPerformancePage(MenuPerformanceReport? Report, string FromInput, string ToInput, string? Error = null);
