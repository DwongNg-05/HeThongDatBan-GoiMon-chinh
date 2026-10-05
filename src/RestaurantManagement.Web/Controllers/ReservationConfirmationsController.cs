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
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeTable(long id,int? newTableId,int? expectedTableId,string? changeReason,CancellationToken ct)
    {
        if(newTableId is null or <=0 || expectedTableId is null or <=0)
            ModelState.AddModelError("","Vui lòng chọn bàn thay thế hợp lệ.");
        if(changeReason?.Length>500) ModelState.AddModelError("","Lý do đổi bàn tối đa 500 ký tự.");
        try
        {
            if(ModelState.IsValid)
            {
                await service.ChangeTable(Actor,id,newTableId!.Value,expectedTableId!.Value,changeReason,ct);
                TempData["Success"]="Đã đổi bàn và ghi lại lịch sử. Bàn cũ đã được giải phóng cho khung giờ của lượt đặt.";
                return RedirectToAction(nameof(Details),new {id});
            }
        }
        catch(SqlException ex) when(ex.Number==51001) { return Forbid(); }
        catch(SqlException ex) when(ex.Number==51701) { return NotFound(); }
        catch(SqlException ex) when(ex.Number is 51702 or 51703 or 51704 or 51705 or 51706 or 51008 or 51009 or 51060 or 51100)
        { ModelState.AddModelError("",ex.Number==51100?"Bàn vừa được giữ. Vui lòng chọn bàn khác.":ex.Message); }
        try
        {
            var model=await service.Details(Actor,id,ct);
            return model is null?NotFound():View("Details",model);
        }
        catch(SqlException ex) when(ex.Number==51001) { return Forbid(); }
    }
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
        catch(SqlException ex) when(ex.Number is 51502 or 51503 or 51504 or 51008 or 51009 or 51060 or 51100)
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
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(long id,string? reason,CancellationToken ct)
    {
        if(reason is null || !RejectionReasons.Labels.ContainsKey(reason))
            ModelState.AddModelError("","Vui lòng chọn một trong ba lý do từ chối hợp lệ.");
        try
        {
            if(ModelState.IsValid)
            {
                await service.Reject(Actor,id,reason,ct);
                TempData["Success"]="Đã từ chối lượt đặt. Khách có thể tra cứu lý do bằng mã đặt bàn và số điện thoại.";
                return RedirectToAction(nameof(Index));
            }
        }
        catch(SqlException ex) when(ex.Number==51001) { return Forbid(); }
        catch(SqlException ex) when(ex.Number is 51010 or 51011) { ModelState.AddModelError("",ex.Message); }
        try
        {
            var model=await service.Details(Actor,id,ct);
            return model is null?NotFound():View("Details",model);
        }
        catch(SqlException ex) when(ex.Number==51001) { return Forbid(); }
    }
}
