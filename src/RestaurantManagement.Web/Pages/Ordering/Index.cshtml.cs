using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models;
using RestaurantManagement.Web.Security;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Pages.Ordering;

[Authorize(Roles = AppRoles.FrontOfHouse)]
public class IndexModel(
    IMenuStore store,
    DailyDishStore? daily = null,
    SessionOrderingService? sessionOrdering = null) : PageModel
{
    public IReadOnlyList<CategoryWithDishes> Categories
    { get; private set; } = [];

    public IReadOnlySet<int> UnavailableDishIds
    { get; private set; } = new HashSet<int>();

    public SessionOrderingViewModel? CurrentSession
    { get; private set; }

    public string? SessionError { get; private set; }

    // Giữ phương thức cho các kiểm thử hiện có.
    [NonHandler]
    public void OnGet()
    {
        Categories = store.GetMenuByCategory();

        if (daily is null)
        {
            return;
        }

        try
        {
            UnavailableDishIds =
                daily.AvailabilityNow().Unavailable.ToHashSet();
        }
        catch (SqlException)
        {
            // Giữ cách hoạt động hiện có:
            // trạng thái món được trình duyệt cập nhật lại,
            // máy chủ kiểm tra món khi gửi.
        }
    }

    public async Task OnGetAsync(int? tableId, string? tableCode)
    {
        OnGet();

        // Chưa chọn bàn: hiển thị thực đơn để xem.
        if (tableId is null && string.IsNullOrWhiteSpace(tableCode))
        {
            return;
        }

        if (sessionOrdering is null)
        {
            SessionError = "Chưa cấu hình dịch vụ gọi món.";
            return;
        }

        // Sơ đồ bàn truyền mã bàn, đổi thành ID để lấy phiên.
        if (tableId is null)
        {
            tableId = await sessionOrdering.GetTableIdByCodeAsync(
                tableCode!,
                HttpContext.RequestAborted);
        }

        if (tableId is null || tableId.Value <= 0)
        {
            SessionError = "Bàn không tồn tại hoặc đã ngừng sử dụng.";
            return;
        }

        CurrentSession = await sessionOrdering.GetByTableAsync(
            tableId.Value,
            HttpContext.RequestAborted);

        if (CurrentSession is null)
        {
            SessionError =
                "Bàn không còn phiên đang mở. "
                + "Vui lòng quay lại sơ đồ bàn.";
            return;
        }

        if (!CurrentSession.CanAddItems)
        {
            SessionError =
                "Bàn đang chờ thanh toán, không thể gọi thêm món.";
        }
    }
}