using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Security;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Controllers;

[Authorize(Roles=AppRoles.Manager)]
public sealed class TableAdjacencyController(TableMergeService service) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct) => View(await service.GetAdjacencyAsync(ct));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(int tableId,int adjacentTableId,bool enabled,CancellationToken ct)
    {
        try
        {
            await service.SetAdjacencyAsync(tableId,adjacentTableId,enabled,User.ActorUserId(),ct);
            TempData["Success"]="Đã cập nhật cặp bàn liền nhau.";
        }
        catch(SqlException ex) when(ex.Number is >=51000 and <51500) { TempData["Error"]=ex.Message; }
        return RedirectToAction(nameof(Index));
    }
}
