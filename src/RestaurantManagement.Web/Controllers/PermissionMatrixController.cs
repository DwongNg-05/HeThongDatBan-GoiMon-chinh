using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Security;

namespace RestaurantManagement.Web.Controllers;

[Route("PermissionMatrix")]
[Authorize(Roles = AppRoles.Manager)]
public sealed class PermissionMatrixController : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View();

    [HttpGet("/api/permissions/matrix")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Data() => Ok(RolePermissionMatrix.Get());
}
