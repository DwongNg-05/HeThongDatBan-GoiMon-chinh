using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Authentication;
using RestaurantManagement.Web.Models.Kitchen;
using RestaurantManagement.Web.Models.Reservations;
using RestaurantManagement.Web.Security;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Controllers;

/// <summary>
/// S1-04 Task 2: phần việc của Bếp — màn hình bếp. Quyền kiểm tra ở máy chủ: xem Quản lý/Bếp;
/// chuyển trạng thái chế biến (Kitchen.Manage) chỉ Bếp. Thủ tục SQL kiểm tra lại lần nữa.
/// S2-08 Task 1: danh sách món trong ngày (/Kitchen/Dishes) — Bếp và Quản lý bật/tắt "Tạm hết" bằng một chạm.
/// ("Báo hết" trong ngày ở Quản lý món /Dishes vẫn chỉ Quản lý.)
/// </summary>
[Route("Kitchen")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class KitchenController(IConfiguration configuration, KitchenStore store) : Controller
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Chưa cấu hình kết nối database.");

    [HttpGet("Ready")]
    [Authorize(Roles = AppRoles.Manager + "," + AppRoles.Waiter)]
    public IActionResult Ready() => View();

    [HttpGet("Snapshot"), PassiveSessionRead]
    [Authorize(Roles = AppRoles.KitchenReaders + "," + AppRoles.Waiter)]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Snapshot(CancellationToken ct)
    {
        try
        {
            var lines = await store.Read(User.ActorUserId(), ct);
            return Json(User.IsInRole(AppRoles.Waiter) ? lines.Where(i => i.Status == "Ready") : lines);
        }
        catch (SqlException ex) when (ex.Number == 51001) { return Forbid(); }
    }

    [HttpPost("Transition"), ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.KitchenWorkers)]
    public async Task<IActionResult> Transition(long id, string from, string to, string version, CancellationToken ct)
    {
        byte[] bytes;
        try { bytes = Convert.FromBase64String(version ?? ""); }
        catch (FormatException) { return BadRequest(new { message = "Phiên bản món không hợp lệ." }); }
        if (!ModelState.IsValid || id <= 0 || bytes.Length != 8)
            return BadRequest(new { message = "Dữ liệu dòng món không hợp lệ. Hãy tải lại danh sách." });
        try { await store.Transition(id, from, to, bytes, User.ActorUserId(), ct); return Ok(); }
        catch (SqlException ex) when (ex.Number == 51001) { return Forbid(); }
        catch (SqlException ex) when (ex.Number is 51029 or 51030) { return Conflict(new { message = ex.Message }); }
    }

    [HttpGet("")]
    [Authorize(Roles = AppRoles.KitchenReaders)]
    public IActionResult Index() => View();

    [HttpPost("CompleteBatch"), ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.KitchenWorkers)]
    public async Task<IActionResult> CompleteBatch(long batchId, CancellationToken ct)
    {
        if (!ModelState.IsValid || batchId<=0) return BadRequest(new { message = "Mã phiếu không hợp lệ." });
        try
        {
            var changed = await store.CompleteBatch(batchId, User.ActorUserId(), ct);
            return Ok(new { changed, message = changed == 0 ? "Phiếu đã hoàn thành, không có món nào bị cập nhật lại." : $"Đã hoàn thành {changed} dòng món trong phiếu." });
        }
        catch (SqlException ex) when (ex.Number == 51001) { return Forbid(); }
        catch (SqlException ex) when (ex.Number is 51029 or 51030) { return Conflict(new { message = ex.Message }); }
    }

    /// <summary>S2-08 Task 1: danh sách món trong ngày, mỗi món có nút bật/tắt "Tạm hết".</summary>
    [HttpGet("Dishes")]
    [Authorize(Roles = TemporaryOutRules.AllowedRoles)]
    public async Task<IActionResult> Dishes([FromServices] DailyDishStore daily, CancellationToken cancellationToken)
        => View(new DailyDishesViewModel(await daily.List(cancellationToken)));

    /// <summary>
    /// S2-08 Task 2: lịch sử bật/tắt "Tạm hết" (người thực hiện, món, trạng thái trước/sau, thời điểm), chỉ Quản lý xem.
    /// <c>?dishId=</c> lọc theo một món.
    /// </summary>
    [HttpGet("Dishes/History")]
    [Authorize(Roles = AppRoles.Manager)]
    public async Task<IActionResult> History(int? dishId, [FromServices] DailyDishStore daily, CancellationToken cancellationToken)
    {
        var dishes = await daily.List(cancellationToken);
        var entries = await daily.History(dishId, cancellationToken: cancellationToken);
        var dishName = dishId is null ? null
            : dishes.FirstOrDefault(d => d.Id == dishId)?.Name ?? entries.FirstOrDefault()?.DishName;
        return View(new TemporaryOutHistoryViewModel(dishId, dishName, dishes, entries));
    }

    /// <summary>
    /// S2-08 Task 1: một chạm bật/tắt "Tạm hết". Gọi bằng fetch (X-Requested-With) nhận JSON để cập nhật ngay dòng món;
    /// form thường (không JavaScript) được chuyển về danh sách kèm thông báo. Câu lệnh SQL kiểm tra lại vai trò (Bếp/Quản lý đang hoạt động) trong database.
    /// </summary>
    [HttpPost("Dishes/{id:int}/TemporarilyOut")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = TemporaryOutRules.AllowedRoles)]
    public async Task<IActionResult> TemporarilyOut(int id, bool isTemporarilyOut, [FromServices] DailyDishStore daily, CancellationToken cancellationToken)
    {
        var result = await daily.SetTemporarilyOut(id, isTemporarilyOut, User.ActorUserId(), cancellationToken);
        var message = result.Status switch
        {
            TemporaryOutStatus.Updated => result.IsTemporarilyOut
                ? $"Đã báo tạm hết: {result.DishName}. Món không nhận order mới."
                : $"Đã bán lại: {result.DishName}.",
            TemporaryOutStatus.Forbidden => "Tài khoản không có quyền bật/tắt món tạm hết.",
            _ => "Món không tồn tại hoặc đã ngừng bán."
        };
        if (IdleSessionEvents.IsApiRequest(Request))
        {
            return result.Status switch
            {
                TemporaryOutStatus.Updated => Ok(new { id, isTemporarilyOut = result.IsTemporarilyOut, message }),
                TemporaryOutStatus.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { id, message }),
                _ => NotFound(new { id, message })
            };
        }
        TempData[result.Status == TemporaryOutStatus.Updated ? "Success" : "Error"] = message;
        return RedirectToAction(nameof(Dishes));
    }

    /// <summary>Bếp chuyển món sang bước tiếp theo (Chờ nấu → Đang nấu → Xong).</summary>
    [HttpPost("Advance/{id:long}")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.KitchenWorkers)]
    public async Task<IActionResult> Advance(long id, string? toStatus)
    {
        // Legacy form requests must also use the timestamp-free, version-checked Task 1 workflow.
        if (toStatus is not ("Preparing" or "Ready"))
        {
            TempData["Error"] = "Trạng thái món không hợp lệ.";
            return RedirectToAction(nameof(Index));
        }
        try
        {
            var line = (await store.Read(User.ActorUserId(), HttpContext.RequestAborted)).FirstOrDefault(i => i.Id == id);
            if (line is null) { TempData["Error"] = "Món không tồn tại hoặc phiên đã đóng."; return RedirectToAction(nameof(Index)); }
            await store.Transition(id, line.Status, toStatus, Convert.FromBase64String(line.Version), User.ActorUserId(), HttpContext.RequestAborted);
            TempData["Success"] = toStatus == "Preparing" ? "Đã chuyển món sang Đang nấu." : "Món đã xong, chờ phục vụ mang ra.";
        }
        catch (SqlException ex) when (ex.Number is >= 51000 and < 51500)
        {
            TempData["Error"] = ex.Number == 51001 ? "Tài khoản không có quyền chuyển trạng thái chế biến." : ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }
}
