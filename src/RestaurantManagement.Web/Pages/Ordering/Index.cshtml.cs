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
    SessionOrderingService? sessionOrdering = null,
    TableMergeService? tableMerge = null) : PageModel
{
    public IReadOnlyList<CategoryWithDishes> Categories
    { get; private set; } = [];

    public IReadOnlySet<int> UnavailableDishIds
    { get; private set; } = new HashSet<int>();

    public SessionOrderingViewModel? CurrentSession
    { get; private set; }

    public string? SessionError { get; private set; }
    public MergeSelection? MergeSelection { get; private set; }

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

    /// <summary>Có khi mở từ nút/mã QR "Gọi thêm món" trong chi tiết bàn: phiên mà mã được tạo cho.</summary>
    public long? ExpectedSessionId { get; private set; }

    public async Task OnGetAsync(int? tableId, string? tableCode, long? sessionId = null)
    {
        ExpectedSessionId = sessionId is > 0 ? sessionId : null;

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

        // Mã QR "Gọi thêm món" gắn với một phiên: bàn đã thanh toán và mở phiên mới thì mã cũ không còn dùng được,
        // tránh ghi nhầm món của bữa trước sang khách mới.
        if (ExpectedSessionId is not null
            && ExpectedSessionId != CurrentSession.SessionId
            && ExpectedSessionId != CurrentSession.BillingSessionId)
        {
            SessionError =
                $"Mã gọi thêm món này thuộc phiên cũ của bàn {CurrentSession.TableCode}. "
                + "Vui lòng mở lại chi tiết bàn trên sơ đồ để lấy mã mới.";
            CurrentSession = null;
            return;
        }

        if (tableMerge is not null && CurrentSession.CanAddItems)
            MergeSelection = await tableMerge.GetSelectionAsync(tableId.Value, HttpContext.RequestAborted);

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
    public async Task<IActionResult> OnPostMergeAsync(int tableId, long currentSessionId, long otherSessionId,
        string currentVersion, string otherVersion, bool keepCurrent, bool confirmed)
    {
        if (tableMerge is null) return StatusCode(503);
        if (!confirmed || currentSessionId<=0 || otherSessionId<=0)
        {
            TempData["OrderingError"]="Hãy chọn hai bàn và xác nhận phiên chính trước khi gộp.";
            return RedirectToPage(new { tableId });
        }
        try
        {
            await tableMerge.MergeAsync(keepCurrent ? currentSessionId : otherSessionId,
                keepCurrent ? otherSessionId : currentSessionId,
                keepCurrent ? currentVersion : otherVersion, keepCurrent ? otherVersion : currentVersion,
                User.ActorUserId(),HttpContext.RequestAborted);
            TempData["OrderingSuccess"]="Đã gộp hai bàn vào phiên chính. Các món giữ nguyên bàn gốc và thanh toán chung một hoá đơn.";
        }
        catch (SqlException ex) when(ex.Number is >=51000 and <51500)
        { TempData["OrderingError"]=ex.Number==51000 ? "Hệ thống đang bận. Thông tin bàn đã được tải lại; vui lòng xác nhận lại." : ex.Message; }
        catch (ArgumentException ex) { TempData["OrderingError"]=ex.Message; }
        return RedirectToPage(new { tableId });
    }

    public async Task<IActionResult> OnPostRequestPaymentAsync(int tableId, long sessionId)
    {
        if (sessionOrdering is null) return StatusCode(503);
        try
        {
            await sessionOrdering.RequestPaymentAsync(tableId,sessionId,User.ActorUserId(),HttpContext.RequestAborted);
            TempData["OrderingSuccess"]="Đã chuyển cả nhóm bàn sang chờ thanh toán. Thu ngân sẽ thanh toán một hoá đơn từ phiên chính.";
        }
        catch(SqlException ex) when(ex.Number is >=51000 and <51500) { TempData["OrderingError"]=ex.Message; }
        return RedirectToPage(new { tableId });
    }

}