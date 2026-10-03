using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
}
