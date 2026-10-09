using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Models.Tables;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Controllers;

/// <summary>
/// Public, anonymous endpoint encoded in printed table QR codes.
/// S3-01 Task 1: GET chỉ kiểm tra mã và hiển thị bàn (an toàn khi ứng dụng chat/trình duyệt tải trước đường dẫn);
/// trình duyệt của khách tự gửi POST cùng địa chỉ để mở phiên gọi món rồi chuyển tới /TableOrder.
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
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return View("NotFound");
        }

        if (qr.IsRevoked)
        {
            Response.StatusCode = StatusCodes.Status410Gone;
            return View("Changed");
        }

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

        switch (result.Outcome)
        {
            case QrStartOutcome.InvalidQr:
                Response.StatusCode = StatusCodes.Status404NotFound;
                return View("NotFound");
            case QrStartOutcome.QrChanged:
                Response.StatusCode = StatusCodes.Status410Gone;
                return View("Changed");
            default:
                var model = TableQrUnavailableViewModel.For(result.Outcome) with { Token = token };
                Response.StatusCode = model.StatusCode;
                Response.Headers.CacheControl = "no-store";
                return View("Unavailable", model);
        }
    }
}
