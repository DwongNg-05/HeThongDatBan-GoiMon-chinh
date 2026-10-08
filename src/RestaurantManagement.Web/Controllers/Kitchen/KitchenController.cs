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
public sealed class KitchenController(IConfiguration configuration) : Controller
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Chưa cấu hình kết nối database.");

    [HttpGet("")]
    [Authorize(Roles = AppRoles.KitchenReaders)]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var items = new List<KitchenOrderItem>();
        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand("""
            SELECT i.Id,t.Code AS TableCode,i.ItemName,i.Unit,i.Quantity,i.Notes,i.Status,i.SubmittedAt,i.EstimatedPrepMinutes
            FROM dbo.OrderItems i
            JOIN dbo.OrderBatches b ON b.Id=i.BatchId
            JOIN dbo.DiningSessions s ON s.Id=b.SessionId
            JOIN dbo.DiningTables t ON t.Id=i.OriginalTableId
            WHERE s.Status<>'Closed' AND i.Status IN ('Pending','Preparing','Ready')
            ORDER BY CASE i.Status WHEN 'Pending' THEN 0 WHEN 'Preparing' THEN 1 ELSE 2 END, i.SubmittedAt, i.Id;
            """, connection);
        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            items.Add(new KitchenOrderItem(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetString(6), VietnamTime.FromUtc(reader.GetDateTime(7)), reader.GetInt32(8)));
        return View(new KitchenScreenViewModel(items, User.IsInRole(AppRoles.Kitchen)));
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
        if (toStatus is not ("Preparing" or "Ready"))
        {
            TempData["Error"] = "Trạng thái món không hợp lệ.";
            return RedirectToAction(nameof(Index));
        }
        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await using var command = new SqlCommand("dbo.usp_TransitionOrderItem", connection) { CommandType = CommandType.StoredProcedure };
            command.Parameters.Add("@OrderItemId", SqlDbType.BigInt).Value = id;
            command.Parameters.Add("@ToStatus", SqlDbType.VarChar, 20).Value = toStatus;
            command.Parameters.Add("@ActorUserId", SqlDbType.Int).Value = User.ActorUserId();
            await connection.OpenAsync();
            await command.ExecuteNonQueryAsync();
            TempData["Success"] = toStatus == "Preparing" ? "Đã chuyển món sang Đang nấu." : "Món đã xong, chờ phục vụ mang ra.";
        }
        catch (SqlException ex) when (ex.Number is >= 51000 and < 51500)
        {
            TempData["Error"] = ex.Number == 51001 ? "Tài khoản không có quyền chuyển trạng thái chế biến." : ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }
}
