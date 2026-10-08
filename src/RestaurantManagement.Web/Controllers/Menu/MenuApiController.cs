using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Controllers;

/// <summary>
/// S2-01 Task 1: danh sách món công khai theo nhóm (GET /api/menu), không cần đăng nhập.
/// S2-01 Task 2: <c>?q=</c> lọc theo tên món, không phân biệt hoa/thường và dấu; chỉ trả về nhóm có món khớp.
/// </summary>
[AllowAnonymous]
[ApiController]
[Route("api/menu")]
public sealed class MenuApiController(IMenuStore store) : ControllerBase
{
    [HttpGet]
    [Produces("application/json")]
    public ActionResult<IReadOnlyList<PublicMenuCategory>> List([FromQuery] string? q = null)
        => Ok(MenuSearch.Filter(store.GetPublicMenu(), q));

    /// <summary>
    /// S2-08 Task 1: id các món đang không nhận order (tạm hết / hết trong ngày). Thực đơn công khai, màn hình gọi món
    /// và danh sách món trong ngày gọi lại mỗi 3 giây, nên nhãn "Tạm hết" cập nhật trong tối đa 5 giây sau thao tác.
    /// </summary>
    [HttpGet("availability")]
    [Produces("application/json")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Availability([FromServices] DailyDishStore daily, CancellationToken cancellationToken)
    {
        try
        {
            var availability = await daily.Availability(cancellationToken);
            return Ok(new
            {
                unavailable = availability.Unavailable,
                temporarilyOut = availability.TemporarilyOut,
                soldOutToday = availability.SoldOutToday,
                checkedAt = DateTime.UtcNow
            });
        }
        catch (SqlException)
        {
            return Problem("Không thể tải trạng thái món lúc này.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
