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

[Route("Kitchen")]
public sealed class KitchenController(IConfiguration configuration) : Controller
{
    private string ConnectionString =>
        Environment.GetEnvironmentVariable("RM_CONNECTION_STRING")
        ?? configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException(
            "Chưa cấu hình kết nối database.");

    // S3-04 Task 2: lấy món kèm phiên, đợt gọi và thời điểm gửi.
    [HttpGet("")]
    [Authorize(Roles = AppRoles.KitchenReaders)]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        var items = new List<KitchenOrderItem>();

        await using var connection = new SqlConnection(ConnectionString);

        await using var command = new SqlCommand("""
            SELECT
                i.Id,
                t.Code AS TableCode,
                i.ItemName,
                i.Unit,
                i.Quantity,
                i.Notes,
                i.Status,
                i.SubmittedAt,
                i.EstimatedPrepMinutes,
                b.SessionId,
                b.Id AS BatchId,
                b.BatchNumber,
                b.CreatedAt AS BatchSubmittedAt
            FROM dbo.OrderItems i
            JOIN dbo.OrderBatches b ON b.Id = i.BatchId
            JOIN dbo.DiningSessions s ON s.Id = b.SessionId
            JOIN dbo.DiningTables t ON t.Id = i.OriginalTableId
            WHERE s.Status <> 'Closed'
            ORDER BY b.CreatedAt, b.SessionId, b.BatchNumber, b.Id, i.Id;
            """, connection);

        await connection.OpenAsync(cancellationToken);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new KitchenOrderItem(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6),
                VietnamTime.FromUtc(reader.GetDateTime(7)),
                reader.GetInt32(8))
            {
                SessionId = reader.GetInt64(9),
                BatchId = reader.GetInt64(10),
                BatchNumber = reader.GetInt32(11),
                BatchSubmittedAt =
                    VietnamTime.FromUtc(reader.GetDateTime(12))
            });
        }

        return View(new KitchenScreenViewModel(
            items,
            User.IsInRole(AppRoles.Kitchen)));
    }

    // S2-08 Task 1: danh sách món trong ngày.
    [HttpGet("Dishes")]
    [Authorize(Roles = TemporaryOutRules.AllowedRoles)]
    public async Task<IActionResult> Dishes(
        [FromServices] DailyDishStore daily,
        CancellationToken cancellationToken)
        => View(new DailyDishesViewModel(
            await daily.List(cancellationToken)));

    // S2-08 Task 2: lịch sử bật/tắt món tạm hết.
    [HttpGet("Dishes/History")]
    [Authorize(Roles = AppRoles.Manager)]
    public async Task<IActionResult> History(
        int? dishId,
        [FromServices] DailyDishStore daily,
        CancellationToken cancellationToken)
    {
        var dishes = await daily.List(cancellationToken);

        var entries = await daily.History(
            dishId,
            cancellationToken: cancellationToken);

        var dishName = dishId is null
            ? null
            : dishes.FirstOrDefault(d => d.Id == dishId)?.Name
                ?? entries.FirstOrDefault()?.DishName;

        return View(new TemporaryOutHistoryViewModel(
            dishId,
            dishName,
            dishes,
            entries));
    }

    // Bếp và Quản lý bật/tắt món tạm hết.
    [HttpPost("Dishes/{id:int}/TemporarilyOut")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = TemporaryOutRules.AllowedRoles)]
    public async Task<IActionResult> TemporarilyOut(
        int id,
        bool isTemporarilyOut,
        [FromServices] DailyDishStore daily,
        CancellationToken cancellationToken)
    {
        var result = await daily.SetTemporarilyOut(
            id,
            isTemporarilyOut,
            User.ActorUserId(),
            cancellationToken);

        var message = result.Status switch
        {
            TemporaryOutStatus.Updated => result.IsTemporarilyOut
                ? $"Đã báo tạm hết: {result.DishName}. Món không nhận order mới."
                : $"Đã bán lại: {result.DishName}.",

            TemporaryOutStatus.Forbidden =>
                "Tài khoản không có quyền bật/tắt món tạm hết.",

            _ => "Món không tồn tại hoặc đã ngừng bán."
        };

        if (IdleSessionEvents.IsApiRequest(Request))
        {
            return result.Status switch
            {
                TemporaryOutStatus.Updated => Ok(new
                {
                    id,
                    isTemporarilyOut = result.IsTemporarilyOut,
                    message
                }),

                TemporaryOutStatus.Forbidden => StatusCode(
                    StatusCodes.Status403Forbidden,
                    new { id, message }),

                _ => NotFound(new { id, message })
            };
        }

        TempData[result.Status == TemporaryOutStatus.Updated
            ? "Success"
            : "Error"] = message;

        return RedirectToAction(nameof(Dishes));
    }

    // Bếp chuyển món: Chờ nấu → Đang nấu → Xong.
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
            await using var connection =
                new SqlConnection(ConnectionString);

            await using var command = new SqlCommand(
                "dbo.usp_TransitionOrderItem",
                connection)
            {
                CommandType = CommandType.StoredProcedure
            };

            command.Parameters.Add(
                "@OrderItemId",
                SqlDbType.BigInt).Value = id;

            command.Parameters.Add(
                "@ToStatus",
                SqlDbType.VarChar,
                20).Value = toStatus;

            command.Parameters.Add(
                "@ActorUserId",
                SqlDbType.Int).Value = User.ActorUserId();

            await connection.OpenAsync();
            await command.ExecuteNonQueryAsync();

            TempData["Success"] = toStatus == "Preparing"
                ? "Đã chuyển món sang Đang nấu."
                : "Món đã xong, chờ phục vụ mang ra.";
        }
        catch (SqlException ex)
            when (ex.Number is >= 51000 and < 51500)
        {
            TempData["Error"] = ex.Number == 51001
                ? "Tài khoản không có quyền chuyển trạng thái chế biến."
                : ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }
}