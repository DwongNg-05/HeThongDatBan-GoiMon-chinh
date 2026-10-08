using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Services;
using System.Security.Claims;

namespace RestaurantManagement.Web.Controllers;

[Authorize,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public class OrdersController(IConfiguration configuration) : Controller
{
    private OrderWorkflowStore Store => new(configuration.GetConnectionString("DefaultConnection")!);
    private int Actor => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet,Authorize(Roles="Waiter,Manager,Cashier,Kitchen")]
    public IActionResult Index() => View("Workflow",false);
    [HttpGet,Authorize(Roles="Kitchen,Manager,Waiter")]
    public IActionResult Kitchen() => View("Workflow",true);
    [HttpGet]
    public async Task<IActionResult> Snapshot(bool kitchen,CancellationToken ct)
    {
        try { return Json(await Store.Read(Actor,kitchen,ct)); }
        catch (SqlException ex) { return Failure(ex); }
    }
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Waiter,Manager")]
    public async Task<IActionResult> Cancel(long id,string? reason,CancellationToken ct)
    {
        if (id<=0 || !OrderWorkflowStore.ValidReason(reason)) return BadRequest(new { message="Vui lòng chọn lý do huỷ hợp lệ." });
        try { var changed=await Store.Cancel(id,reason!,Actor,ct); return Json(new { message=changed ? "Đã huỷ toàn bộ dòng món. Món không tính tiền." : "Dòng món đã được huỷ trước đó." }); }
        catch (SqlException ex) { return Failure(ex); }
    }
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Kitchen")]
    public async Task<IActionResult> Start(long id,CancellationToken ct)
    {
        try { await Store.Start(id,Actor,ct); return Json(new { message="Bếp đã bắt đầu chế biến." }); }
        catch (SqlException ex) { return Failure(ex); }
    }
    private IActionResult Failure(SqlException ex) => StatusCode(ex.Number==51001 ? 403 : ex.Number is >=51000 and <51600 ? 409 : 503,
        new { message=ex.Number is >=51000 and <51600 ? ex.Message : "Không thể kết nối dữ liệu. Vui lòng thử lại." });
}
