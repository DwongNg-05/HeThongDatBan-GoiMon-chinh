using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Controllers;

[ApiController]
[Microsoft.AspNetCore.Authorization.Authorize(Roles = "Waiter")]
[Route("api/table-map")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class TableDetailsController(TableDetailsService detailsService, ILogger<TableDetailsController> logger) : ControllerBase
{
    [HttpGet("{code}")]
    [RestaurantManagement.Web.Authentication.PassiveSessionRead]
    public async Task<IActionResult> Get(string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 20)
            return BadRequest(new { message = "Mã bàn không hợp lệ." });
        try
        {
            if (!int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var actorUserId))
                return StatusCode(403, new { message = "Bạn không có quyền xem chi tiết bàn." });
            var details = await detailsService.GetForUserAsync(code, actorUserId, cancellationToken);
            return details is null ? NotFound(new { message = "Không tìm thấy bàn." }) : Ok(details);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Microsoft.Data.SqlClient.SqlException exception) when (exception.Number == 51001)
        {
            return StatusCode(403, new { message = "Bạn không có quyền xem thông tin khách và tạm tính." });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unable to load details for table {TableCode}", code);
            return StatusCode(503, new { message = "Không tải được chi tiết bàn. Vui lòng thử lại." });
        }
    }
}
