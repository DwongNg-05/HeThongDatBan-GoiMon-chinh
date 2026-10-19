using Microsoft.AspNetCore.Authorization;
using RestaurantManagement.Web.Security;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Controllers;

[ApiController]
[Route("api/table-status")]
public sealed class TableStatusController(TableMapStore tableMap, TableMapEventBroker eventBroker) : ControllerBase
{
    // S1-04 Task 2/4: sơ đồ bàn và đổi trạng thái bàn — Quản lý, Phục vụ (Sessions.Manage); Bếp, Thu ngân bị chặn.
    // Dữ liệu lấy từ "Khu vực & bàn" (dbo.Areas, dbo.DiningTables).
    [HttpGet]
    [Authorize(Roles = AppRoles.FrontOfHouse)]
    public async Task<IActionResult> Snapshot(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await tableMap.GetAllAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Trình duyệt đã huỷ yêu cầu (chuyển trang, đăng xuất, đóng tab): không phải lỗi.
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    // Trạng thái bàn là kết quả của quy trình nghiệp vụ (quét QR/mở phiên, xác nhận đặt bàn,
    // thanh toán và báo dọn xong). Không cho sửa trực tiếp vì có thể tạo bàn "Đang phục vụ"
    // nhưng không có DiningSession, khiến cả khách và phục vụ đều không thể gọi thêm món.
    [HttpPost("{code}")]
    [Authorize(Roles = AppRoles.FrontOfHouse)]
    public IActionResult Update(string code, [FromBody] UpdateTableStatusRequest request)
    {
        return Conflict(new
        {
            message = "Không thể đổi trạng thái bàn thủ công. Hãy quét QR để mở phiên, xác nhận đặt bàn, thanh toán hoặc báo đã dọn xong theo đúng quy trình."
        });
    }

    [HttpGet("stream")]
    [Authorize(Roles = AppRoles.FrontOfHouse)]
    public async Task Stream(CancellationToken cancellationToken)
    {
        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache, no-store";
        Response.Headers.Connection = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";

        using var subscription = eventBroker.Subscribe();
        await Response.WriteAsync("retry: 1000\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            using var waitTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            waitTimeout.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                if (!await subscription.Reader.WaitToReadAsync(waitTimeout.Token)) break;
                while (subscription.Reader.TryRead(out var update))
                {
                    var json = JsonSerializer.Serialize(update, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    await Response.WriteAsync($"event: statusChanged\ndata: {json}\n\n", cancellationToken);
                }
                await Response.Body.FlushAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Response.WriteAsync(": keep-alive\n\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // EventSource closes and reconnects routinely; RequestAborted is the normal end of this stream.
                break;
            }
        }
    }
}

public sealed record UpdateTableStatusRequest(string Status);
