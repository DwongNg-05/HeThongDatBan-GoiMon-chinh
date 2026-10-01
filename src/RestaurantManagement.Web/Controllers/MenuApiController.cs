using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Controllers;

/// <summary>S2-01 Task 1: danh sách món công khai theo nhóm (GET /api/menu), không cần đăng nhập.</summary>
[AllowAnonymous]
[ApiController]
[Route("api/menu")]
public sealed class MenuApiController(IMenuStore store) : ControllerBase
{
    [HttpGet]
    [Produces("application/json")]
    public ActionResult<IReadOnlyList<PublicMenuCategory>> List() => Ok(store.GetPublicMenu());
}
