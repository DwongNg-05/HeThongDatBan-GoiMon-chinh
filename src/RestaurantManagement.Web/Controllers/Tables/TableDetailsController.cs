using Microsoft.AspNetCore.Authorization;
using RestaurantManagement.Web.Security;
using Microsoft.AspNetCore.Mvc;
using QRCoder;
using RestaurantManagement.Web.Authentication;
using RestaurantManagement.Web.Models;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Controllers;

// S1-04 Task 2/4: chi tiết bàn trên sơ đồ — Quản lý, Phục vụ.
// Gọi thêm món giữa bữa: chi tiết bàn kèm đường dẫn + mã QR mở màn hình "Gọi món" của nhân viên cho ĐÚNG phiên
// đang mở của bàn. Món gọi thêm được ghi thành một đợt mới trong cùng phiên (dbo.usp_SubmitOrder), nên vẫn chỉ có
// một hoá đơn cho bàn. Mã QR mang theo mã phiên: khi bàn đã thanh toán và mở phiên mới, mã cũ bị từ chối.
[Authorize(Roles = AppRoles.FrontOfHouse)]
[ApiController]
[Authorize(Roles = "Manager,Waiter")]
[Route("api/table-map")]
public sealed class TableDetailsController(TableDetailsService detailsService, IConfiguration configuration) : ControllerBase
{
    [HttpGet("{code}")]
    [PassiveSessionRead]
    public async Task<IActionResult> Get(string code, CancellationToken cancellationToken)
    {
        TableDetailsViewModel? details;
        try
        {
            details = await detailsService.GetAsync(code, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Trình duyệt đã huỷ yêu cầu (đóng hộp chi tiết, chuyển trang, đăng xuất): không phải lỗi.
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
        if (details is null)
            return NotFound(new { message = "Không tìm thấy bàn." });

        Response.Headers.CacheControl = "no-store";
        return Ok(WithAddItemsLinks(details));
    }

    /// <summary>
    /// Ảnh PNG mã QR "Gọi thêm món" của phiên đang mở. Nhân viên quét bằng điện thoại (đã đăng nhập) để thêm món
    /// vào phiên này. Từ chối khi phiên đã đổi / đã đóng để không in nhầm mã của phiên khác.
    /// </summary>
    [HttpGet("{code}/add-items-qr")]
    [PassiveSessionRead]
    public async Task<IActionResult> AddItemsQr(string code, long sessionId, CancellationToken cancellationToken)
    {
        TableDetailsViewModel? details;
        try
        {
            details = await detailsService.GetAsync(code, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
        if (details is null)
            return NotFound(new { message = "Không tìm thấy bàn." });
        if (details.IsDemoData || details.SessionId is null || details.SessionId != sessionId)
            return Conflict(new { message = "Bàn không còn phiên này. Vui lòng tải lại chi tiết bàn." });

        var url = BuildAddItemsUrl(details.Code, details.SessionId.Value, absolute: true);
        var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(10);

        Response.Headers.CacheControl = "no-store";
        return File(png, "image/png");
    }

    private TableDetailsViewModel WithAddItemsLinks(TableDetailsViewModel details)
    {
        if (details.IsDemoData || details.SessionId is null)
            return details;

        var sessionId = details.SessionId.Value;
        return details with
        {
            AddItemsUrl = BuildAddItemsUrl(details.Code, sessionId, absolute: false),
            AddItemsQrImageUrl = Url.Content("~/api/table-map/")
                + Uri.EscapeDataString(details.Code) + "/add-items-qr?sessionId=" + sessionId
        };
    }

    /// <summary>/Ordering?tableCode=…&amp;sessionId=… — absolute khi đưa vào mã QR (điện thoại cần địa chỉ đầy đủ).</summary>
    private string BuildAddItemsUrl(string tableCode, long sessionId, bool absolute)
    {
        var path = Url.Content("~/Ordering") + "?tableCode=" + Uri.EscapeDataString(tableCode) + "&sessionId=" + sessionId;
        if (!absolute)
            return path;

        // Cùng cấu hình với mã QR in trên bàn: đặt TableQr:PublicBaseUrl = địa chỉ LAN (vd. http://192.168.1.10:5000)
        // để điện thoại trong nhà hàng mở được; để trống thì dùng địa chỉ của yêu cầu hiện tại.
        var configuredBaseUrl = configuration["TableQr:PublicBaseUrl"]?.Trim();
        if (string.IsNullOrWhiteSpace(configuredBaseUrl))
            return $"{Request.Scheme}://{Request.Host}{path}";

        var pathBase = Request.PathBase.HasValue ? Request.PathBase.Value! : string.Empty;
        return configuredBaseUrl.TrimEnd('/') + path[pathBase.Length..];
    }
}
