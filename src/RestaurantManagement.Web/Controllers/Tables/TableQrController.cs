using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Models.Tables;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Controllers;

/// <summary>
/// Public, anonymous endpoint encoded in printed table QR codes.
/// S3-01 Task 1: GET chỉ kiểm tra mã và hiển thị bàn (an toàn khi ứng dụng chat/trình duyệt tải trước đường dẫn);
/// trình duyệt của khách tự gửi POST cùng địa chỉ để mở phiên gọi món rồi chuyển tới /TableOrder.
/// S3-01 Task 3: mã đã bị sinh lại hoặc bàn đang dọn → không mở phiên, hiện thông báo kèm hướng dẫn gọi phục vụ.
/// </summary>
[AllowAnonymous]
[Route("q")]
public sealed class TableQrController(TableQrService qrService, GuestTableSessionService guestSessions) : Controller
{
    [HttpGet("{token}")]
    public async Task<IActionResult> Scan(string token)
    {
        var qr = await qrService.FindByPublicTokenAsync(token);
        if (qr is null || !qr.IsActive)
            return await NotFoundPage();

        if (qr.IsReplaced)
            return await ChangedPage(qr);

        // S3-01 Task 3: kiểm tra trạng thái bàn ngay khi mở đường dẫn — bàn đang dọn thì báo luôn, không tự gửi yêu cầu mở phiên.
        if (qr.IsCleaning)
            return await UnavailablePage(QrStartOutcome.TableCleaning, token, qr);

        // Trang chứa mã chống giả mạo và quyết định theo người đang xem: không lưu đệm.
        Response.Headers.CacheControl = "no-store";
        return View("Scan", new TableQrScanViewModel
        {
            TableCode = qr.TableCode,
            AreaName = qr.AreaName,
            MinCapacity = qr.MinCapacity,
            MaxCapacity = qr.MaxCapacity,
            TableType = qr.TableType,
            Token = token,
            AutoStart = User.Identity?.IsAuthenticated != true
        });
    }

    /// <summary>
    /// S3-01 Task 1: mở (hoặc vào lại) phiên gọi món của bàn. Bàn trống → tạo đúng một phiên mới và chuyển
    /// "Đang phục vụ" trong cùng giao dịch SQL. Quét/bấm lặp không tạo thêm phiên.
    /// S3-01 Task 3: thủ tục SQL đối chiếu lại mã với phiên bản hiện tại và trạng thái bàn trong cùng giao dịch,
    /// nên mã cũ / bàn đang dọn bị chặn kể cả khi trang GET đã mở từ trước.
    /// </summary>
    [HttpPost("{token}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(string token, CancellationToken cancellationToken)
    {
        Request.Cookies.TryGetValue(GuestTableSessionService.CookieName, out var existingGuestToken);
        var result = await guestSessions.StartAsync(token, existingGuestToken, cancellationToken);

        if (result.OpensOrdering)
        {
            if (result.IssuesCookie)
                Response.Cookies.Append(GuestTableSessionService.CookieName, result.GuestToken!,
                    GuestTableSessionService.CreateCookieOptions(Request, result.ExpiresAtUtc!.Value));
            if (result.Outcome == QrStartOutcome.Joined)
                TempData[TableOrderController.JoinedKey] = "Bàn đang được phục vụ: bạn đã vào chung phiên gọi món của bàn. Các món bàn đã gọi hiện ở mục “Món bàn đã gọi”.";
            return RedirectToAction("Index", "TableOrder");
        }

        var qr = result.Outcome == QrStartOutcome.InvalidQr ? null : await qrService.FindByPublicTokenAsync(token);
        return result.Outcome switch
        {
            QrStartOutcome.InvalidQr => await NotFoundPage(),
            QrStartOutcome.QrChanged => await ChangedPage(qr),
            _ => await UnavailablePage(result.Outcome, token, qr)
        };
    }

    private async Task<IActionResult> NotFoundPage()
    {
        Response.StatusCode = StatusCodes.Status404NotFound;
        Response.Headers.CacheControl = "no-store";
        ViewData[CallStaffViewModel.ViewDataKey] = new CallStaffViewModel(null, await qrService.GetRestaurantPhoneAsync());
        return View("NotFound");
    }

    private async Task<IActionResult> ChangedPage(TableQrLookup? qr)
    {
        Response.StatusCode = StatusCodes.Status410Gone;
        Response.Headers.CacheControl = "no-store";
        ViewData[CallStaffViewModel.ViewDataKey] = new CallStaffViewModel(qr?.TableCode, await qrService.GetRestaurantPhoneAsync());
        return View("Changed");
    }

    private async Task<IActionResult> UnavailablePage(QrStartOutcome outcome, string token, TableQrLookup? qr)
    {
        var model = TableQrUnavailableViewModel.For(outcome) with
        {
            Token = token,
            TableCode = qr?.TableCode,
            RestaurantPhone = await qrService.GetRestaurantPhoneAsync()
        };
        Response.StatusCode = model.StatusCode;
        Response.Headers.CacheControl = "no-store";
        return View("Unavailable", model);
    }
}
