using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Security;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Pages.Ordering;

[Authorize(Roles=AppRoles.FrontOfHouse)]
public sealed class UnmergeModel(TableUnmergeService service,SessionOrderingService ordering) : PageModel
{
    public int TableId { get; private set; }
    public UnmergePreview? Preview { get; private set; }

    public async Task OnGetAsync(int? tableId,string? tableCode)
    {
        TableId=tableId ?? (string.IsNullOrWhiteSpace(tableCode) ? 0 : await ordering.GetTableIdByCodeAsync(tableCode,HttpContext.RequestAborted) ?? 0);
        if(TableId>0) Preview=await service.PreviewAsync(TableId,HttpContext.RequestAborted);
        Response.Headers.CacheControl="no-store";
    }

    public async Task<IActionResult> OnPostAsync(int tableId,long childSessionId,long mergeId,string mainVersion,string childVersion,bool confirmed)
    {
        if(!confirmed)
        {
            TempData["UnmergeError"]="Hãy kiểm tra các bàn được tách và tích xác nhận huỷ gộp.";
            return RedirectToPage(new { tableId });
        }
        try
        {
            await service.UnmergeAsync(tableId,childSessionId,mergeId,mainVersion,childVersion,User.ActorUserId(),HttpContext.RequestAborted);
            TempData["OrderingSuccess"]="Đã huỷ gộp. Hai bàn trở lại Đang phục vụ, nhận đúng món theo bàn gốc và có tạm tính riêng. Yêu cầu thanh toán chung đã được huỷ.";
            return RedirectToPage("/Ordering/Index",new { tableId });
        }
        catch(SqlException ex) when(ex.Number is >=51000 and <51500) { TempData["UnmergeError"]=ex.Number==51000 ? "Hệ thống đang bận. Thông tin đã được tải lại; vui lòng xác nhận lại." : ex.Message; }
        catch(ArgumentException ex) { TempData["UnmergeError"]=ex.Message; }
        return RedirectToPage(new { tableId });
    }
}
