using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models;
using RestaurantManagement.Web.Models.Reservations;
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

    public bool CanReopen =>
        CurrentSession?.IsAwaitingPayment == true
        && User.IsInRole(AppRoles.FrontOfHouse);

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
            // Trình duyệt cập nhật trạng thái món;
            // máy chủ kiểm tra lại khi gửi món.
        }
    }

    public async Task OnGetAsync(int? tableId, string? tableCode)
    {
        OnGet();

        if (tableId is null && string.IsNullOrWhiteSpace(tableCode))
        {
            return;
        }

        if (sessionOrdering is null)
        {
            SessionError = "Chưa cấu hình dịch vụ gọi món.";
            return;
        }

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
                "Bàn đang chờ thanh toán. "
                + "Hãy mở lại bàn trước khi gọi thêm món.";
        }
    }

    public async Task<IActionResult> OnPostReopenAsync(
        int tableId,
        long sessionId)
    {
        if (sessionOrdering is null)
        {
            TempData["OrderingError"] =
                "Chưa cấu hình dịch vụ gọi món.";

            return RedirectToPage(new { tableId });
        }

        try
        {
            await sessionOrdering.ReopenAsync(
                tableId,
                sessionId,
                User.ActorUserId(),
                HttpContext.RequestAborted);

            TempData["OrderingSuccess"] =
                "Bàn đã được mở lại trong cùng phiên. "
                + "Bạn có thể gọi thêm món.";
        }
        catch (SqlException ex)
            when (ex.Number is >= 51000 and < 51500)
        {
            TempData["OrderingError"] = ex.Number == 51001
                ? "Tài khoản không có quyền mở lại bàn."
                : ex.Message;
        }
        catch (ArgumentException ex)
        {
            TempData["OrderingError"] = ex.Message;
        }

        return RedirectToPage(new { tableId });
    }
}