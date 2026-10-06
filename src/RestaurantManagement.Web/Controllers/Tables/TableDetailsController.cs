using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Authentication;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Controllers;

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
