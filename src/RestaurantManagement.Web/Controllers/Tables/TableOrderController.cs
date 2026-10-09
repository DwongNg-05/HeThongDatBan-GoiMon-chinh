using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Models.Tables;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Controllers;

/// <summary>
/// S3-01 Task 1: trang gọi món của khách sau khi quét QR bàn. Không cần đăng nhập: khách được nhận ra bằng cookie
/// phiên khách (HttpOnly) cấp khi quét QR. Trang hiển thị đúng bàn để khách xác nhận, kèm thực đơn đang bán,
/// giỏ món với nút "Đặt món" và danh sách "Món đã đặt" của bàn.
/// </summary>
[AllowAnonymous]
[Route("TableOrder")]
public sealed class TableOrderController(GuestTableSessionService guestSessions, IMenuStore menu) : Controller
{
    private const string SuccessKey = "TableOrderSuccess";
    private const string ErrorKey = "TableOrderError";
    /// <summary>S3-01 Task 2: TableQrController ghi khi khách vào chung phiên đang mở của bàn.</summary>
    public const string JoinedKey = "TableOrderJoined";
    private static readonly JsonSerializerOptions CartJsonOptions = new() { PropertyNameCaseInsensitive = true };

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        // Nội dung theo từng khách (bàn của phiên): không lưu đệm ở trình duyệt hay proxy.
        Response.Headers.CacheControl = "no-store";
        Request.Cookies.TryGetValue(GuestTableSessionService.CookieName, out var guestToken);
        var table = await guestSessions.GetContextAsync(guestToken, cancellationToken);
        if (table is null)
            return View("NoSession");

        return View(new TableOrderViewModel(table, menu.GetPublicMenu())
        {
            OrderedItems = await guestSessions.GetOrderedItemsAsync(table.SessionId, table.GuestSessionId, cancellationToken),
            InfoMessage = TempData[JoinedKey] as string,
            SuccessMessage = TempData[SuccessKey] as string,
            ErrorMessage = TempData[ErrorKey] as string
        });
    }

    /// <summary>Khách bấm "Đặt món": gửi giỏ món xuống bếp rồi quay lại trang gọi món (PRG), cuộn tới "Món đã đặt".</summary>
    [HttpPost("Submit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(string? cartJson, Guid requestId, CancellationToken cancellationToken)
    {
        Request.Cookies.TryGetValue(GuestTableSessionService.CookieName, out var guestToken);
        var table = await guestSessions.GetContextAsync(guestToken, cancellationToken);
        if (table is null)
            return RedirectToAction(nameof(Index));

        var lines = ParseCart(cartJson);
        var result = lines is null
            ? new GuestOrderResult(false, "Giỏ món đang trống hoặc không hợp lệ. Vui lòng chọn món rồi bấm “Đặt món”.")
            : await guestSessions.SubmitOrderAsync(guestToken, table.SessionId, requestId, lines, cancellationToken);

        TempData[result.Succeeded ? SuccessKey : ErrorKey] = result.Message;
        return Redirect(Url.Action(nameof(Index))! + (result.Succeeded ? "#da-dat" : "#gio-mon"));
    }

    /// <summary>Đọc giỏ món dạng [{"dishId":1,"quantity":2,"notes":"ít cay"}]; null khi rỗng hoặc sai định dạng.</summary>
    public static IReadOnlyList<GuestCartLine>? ParseCart(string? cartJson)
    {
        if (string.IsNullOrWhiteSpace(cartJson) || cartJson.Length > 50_000) return null;
        try
        {
            var lines = JsonSerializer.Deserialize<List<GuestCartLine>>(cartJson, CartJsonOptions);
            return lines is { Count: > 0 } ? lines : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
