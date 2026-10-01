using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models.Reservations;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Controllers;

[Authorize(Roles = "Manager,Waiter")]
public sealed class ReservationConfirmationsController(ReservationConfirmationService service) : Controller
{
    private int Actor => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier),out var id)?id:0;
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        try { return View(await service.Pending(Actor,ct)); }
        catch(SqlException ex) when(ex.Number==51001) { return Forbid(); }
    }
    [HttpGet]
    public async Task<IActionResult> Details(long id,CancellationToken ct)
    {
        try { var model=await service.Details(Actor,id,ct); return model is null?NotFound():View(model); }
        catch(SqlException ex) when(ex.Number==51001) { return Forbid(); }
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(long id, int? selectedTableId,CancellationToken ct)
    {
        if(!selectedTableId.HasValue || selectedTableId<=0)
            ModelState.AddModelError(nameof(ReservationConfirmation.SelectedTableId),"Vui lòng chọn một bàn trong danh sách gợi ý.");
        try
        {
            if(ModelState.IsValid)
            {
                await service.Confirm(Actor,id,selectedTableId!.Value,ct);
                TempData["Success"]="Đã xác nhận lượt đặt và giữ bàn trong khung giờ hẹn.";
                return RedirectToAction(nameof(Details),new {id});
            }
        }
        catch(SqlException ex) when(ex.Number==51001) { return Forbid(); }
        catch(SqlException ex) when(ex.Number==51501) { return NotFound(); }
        catch(SqlException ex) when(ex.Number is 51502 or 51503 or 51008 or 51009 or 51100)
        {
            ModelState.AddModelError("",ex.Number==51100?"Bàn vừa được giữ. Vui lòng chọn bàn khác trong danh sách mới.":ex.Message);
        }
        try
        {
            var model=await service.Details(Actor,id,ct);
            if(model is null) return NotFound();
            // Do not leave a stale/forged table selected after reloading availability.
            ModelState.Remove(nameof(ReservationConfirmation.SelectedTableId));
            if(!selectedTableId.HasValue || selectedTableId<=0)
                ModelState.AddModelError("","Vui lòng chọn một bàn trong danh sách gợi ý.");
            return View("Details",model);
        }
        catch(SqlException ex) when(ex.Number==51001) { return Forbid(); }
    }
    [HttpGet]
    public async Task<IActionResult> Schedule(DateTime? day,CancellationToken ct)
    {
        if(!ModelState.IsValid) return BadRequest();
        var date=(day??VietnamTime.Now).Date;
        if(date.Year<1900 || date.Year>9998) return BadRequest();
        ViewData["Day"]=date;
        try { return View(await service.Slots(Actor,date,ct)); }
        catch(SqlException ex) when(ex.Number==51001) { return Forbid(); }
    }
}
