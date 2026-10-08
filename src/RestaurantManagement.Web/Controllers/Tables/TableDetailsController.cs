using Microsoft.AspNetCore.Authorization;
using RestaurantManagement.Web.Security;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Authentication;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Controllers;

// S1-04 Task 2/4: chi tiết bàn trên sơ đồ — Quản lý, Phục vụ.
[Authorize(Roles = AppRoles.FrontOfHouse)]
[ApiController]
[Authorize(Roles = "Manager,Waiter")]
[Route("api/table-map")]
public sealed class TableDetailsController(TableDetailsService detailsService) : ControllerBase
{
    [HttpGet("{code}")]
    [PassiveSessionRead]
    public async Task<IActionResult> Get(string code, CancellationToken cancellationToken)
    {
        var details = await detailsService.GetAsync(code, cancellationToken);
        return details is null ? NotFound(new { message = "Không tìm thấy bàn." }) : Ok(details);
    }
}
