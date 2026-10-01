using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models.Reservations;
using RestaurantManagement.Web.Services;
namespace RestaurantManagement.Web.Controllers;

[AllowAnonymous]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class ReservationLookupController(ReservationConfirmationService service) : Controller
{
    [HttpGet]
    public IActionResult Index() => View(new ReservationLookup());
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(ReservationLookup model,CancellationToken ct)
    {
        if(!ModelState.IsValid) return View(model);
        try
        {
            model.Result=await service.Lookup(model.Code,model.Phone,HttpContext.Connection.RemoteIpAddress?.ToString()??"unknown",ct);
            if(model.Result is null) ModelState.AddModelError("","Không tìm thấy lượt đặt phù hợp với mã và số điện thoại đã nhập.");
        }
        catch(SqlException ex) when(ex.Number==51620)
        {
            Response.StatusCode=StatusCodes.Status429TooManyRequests;
            ModelState.AddModelError("","Bạn đã tra cứu quá nhiều lần. Vui lòng thử lại sau 15 phút.");
        }
        return View(model);
    }
}
