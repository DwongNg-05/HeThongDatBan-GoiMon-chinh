using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Controllers;

/// <summary>S2-01 Task 1: danh sách món công khai theo nhóm (GET /api/thuc-don), không cần đăng nhập.</summary>
[AllowAnonymous]
[ApiController]
[Route("api/thuc-don")]
public sealed class ThucDonApiController(IQuanLyMonStore store) : ControllerBase
{
    [HttpGet]
    [Produces("application/json")]
    public ActionResult<IReadOnlyList<NhomThucDonCongKhai>> DanhSach() => Ok(store.LayThucDonCongKhai());
}
