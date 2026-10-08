using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Authentication;
using RestaurantManagement.Web.Security;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Controllers;

[Authorize(Roles = AppRoles.Manager)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ShiftReportsController(IConfiguration configuration) : Controller
{
    [HttpGet]
    public IActionResult Index() => View();

    [HttpGet]
    [PassiveSessionRead]
    public async Task<IActionResult> Cancellations(long? shiftId, CancellationToken ct)
    {
        if (!ModelState.IsValid || shiftId is <= 0)
            return BadRequest(new { message = "Ca được chọn không hợp lệ." });
        try
        {
            var store = new ShiftCancellationReportStore(configuration.GetConnectionString("DefaultConnection")!);
            return Json(await store.Read(User.ActorUserId(), shiftId, ct));
        }
        catch (SqlException ex)
        {
            return StatusCode(ex.Number == 51510 ? 403 : ex.Number == 51511 ? 404 : 503,
                new { message = ex.Number is 51510 or 51511 ? ex.Message : "Không tải được báo cáo cuối ca. Vui lòng thử lại." });
        }
    }
}
