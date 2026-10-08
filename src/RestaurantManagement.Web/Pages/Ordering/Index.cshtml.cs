using Microsoft.AspNetCore.Authorization;
using RestaurantManagement.Web.Security;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Pages.Ordering;

// S1-04 Task 4: kiểm tra quyền ở máy chủ theo vai trò (docs/S1-04-Task4.md).
[Authorize(Roles = AppRoles.FrontOfHouse)]
// daily = null: dùng trong kiểm thử với store bộ nhớ (không có SQL Server) — khi đó không có món tạm hết.
public class IndexModel(IMenuStore store, DailyDishStore? daily = null) : PageModel
{
    public IReadOnlyList<CategoryWithDishes> Categories { get; private set; } = [];

    /// <summary>S2-08 Task 1: món đang tạm hết / hết trong ngày — hiện nhãn "Tạm hết" và khoá nút thêm vào giỏ.</summary>
    public IReadOnlySet<int> UnavailableDishIds { get; private set; } = new HashSet<int>();

    public void OnGet()
    {
        Categories = store.GetMenuByCategory();
        if (daily is null) return;
        try { UnavailableDishIds = daily.AvailabilityNow().Unavailable.ToHashSet(); }
        catch (SqlException) { /* Trang vẫn mở được; trình duyệt tự cập nhật trạng thái món mỗi 3 giây, máy chủ kiểm tra lại khi gọi món. */ }
    }
}
