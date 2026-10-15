using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Models.Orders;
using RestaurantManagement.Web.Security;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Controllers;

/// <summary>
/// S4-01 Task 1: điểm truy vấn dữ liệu tạm tính của một bàn đang phục vụ (docs/S4-01-Task1.md).
/// GET /api/tables/{code}/provisional-bill → các dòng món (tên, đơn vị, số lượng, đơn giá, thành tiền, ghi chú),
/// nhóm "Đã huỷ – có tính tiền" (món huỷ sau khi bếp chế biến) và tổng cộng; món huỷ trước khi bếp chế biến bị loại.
/// Tiền trả về là số nguyên đồng kèm chuỗi đã định dạng VND ("1.250.000 đ").
/// Chỉ đọc: không đổi bàn, phiên hay đơn gọi món (TableBillPreviewStore luôn rollback). Quyền: Quản lý, Phục vụ.
/// 404: bàn không tồn tại, hoặc bàn không có phiên đang phục vụ.
/// </summary>
[ApiController]
[Route("api/tables/{code}/provisional-bill")]
[Authorize(Roles = AppRoles.FrontOfHouse)]
public sealed class ProvisionalBillApiController(TableBillPreviewStore store) : ControllerBase
{
    [HttpGet("")]
    [Produces("application/json")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Get(string code, CancellationToken cancellationToken)
    {
        var result = await store.GetAsync(code, cancellationToken);
        return result.Preview is null
            ? NotFound(new { message = result.Message, outcome = result.Outcome.ToString() })
            : Ok(TableBillPreviewResponse.From(result.Preview));
    }
}
