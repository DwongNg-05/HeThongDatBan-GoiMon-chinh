using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Authentication;
using RestaurantManagement.Web.Models.Serving;
using RestaurantManagement.Web.Security;

namespace RestaurantManagement.Web.Controllers;

/// <summary>
/// Món chờ mang ra (/Serving): Phục vụ (và Quản lý) thấy các món Bếp đã nấu xong nhưng chưa mang ra,
/// sắp theo thời điểm hoàn thành; món chờ quá 5 phút được đánh dấu nổi bật để ưu tiên mang ra trước khi nguội.
/// Bấm "Đã mang ra" gọi usp_TransitionOrderItem (Ready → Served); thủ tục SQL kiểm tra lại quyền Orders.Serve.
/// Quy tắc: Models/Serving/ServingQueue.cs; tài liệu: docs/S3-08-Task1.md (danh sách chờ, xác nhận, chống trùng),
/// docs/Mon-cho-mang-ra.md (cảnh báo chờ quá 5 phút).
/// </summary>
[Route("Serving")]
public sealed class ServingController(IConfiguration configuration) : Controller
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Chưa cấu hình kết nối database.");

    [HttpGet("")]
    [Authorize(Roles = AppRoles.FrontOfHouse)]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => View(await Load(cancellationToken));

    /// <summary>
    /// Danh sách chờ dạng JSON — trang gọi lại mỗi 5 giây để thêm món vừa xong và gỡ món đã được mang ra.
    /// Gọi tự động nên không gia hạn phiên đăng nhập đang nhàn rỗi.
    /// </summary>
    [HttpGet("Queue")]
    [Authorize(Roles = AppRoles.FrontOfHouse)]
    [PassiveSessionRead]
    [Produces("application/json")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Queue(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(ServingQueue.ToJson(await Load(cancellationToken)));
        }
        catch (SqlException)
        {
            return Problem("Không thể tải danh sách món chờ lúc này.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    /// <summary>
    /// Phục vụ xác nhận đã mang món ra. Gọi bằng fetch (X-Requested-With) nhận JSON để gỡ dòng ngay;
    /// form thường (không JavaScript) được chuyển về danh sách kèm thông báo.
    /// S3-08 Task 1 — chống xác nhận trùng: usp_TransitionOrderItem khoá độc quyền rồi mới đọc trạng thái, nên hai người bấm cùng lúc
    /// chỉ một người được ghi nhận (200, served = true); người còn lại nhận 409 (alreadyServed = true) kèm tên người đã mang ra.
    /// </summary>
    [HttpPost("Serve/{id:long}")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.FrontOfHouse)]
    public async Task<IActionResult> Serve(long id, CancellationToken cancellationToken)
    {
        int status;
        string message;
        var alreadyServed = false;
        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync(cancellationToken);
            bool changed;
            await using (var command = new SqlCommand("dbo.usp_TransitionOrderItem", connection) { CommandType = CommandType.StoredProcedure })
            {
                command.Parameters.Add("@OrderItemId", SqlDbType.BigInt).Value = id;
                command.Parameters.Add("@ToStatus", SqlDbType.VarChar, 20).Value = "Served";
                command.Parameters.Add("@ActorUserId", SqlDbType.Int).Value = User.ActorUserId();
                changed = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0) == 1;
            }
            await using var info = new SqlCommand("""
                SELECT t.Code,i.ItemName,i.ServedBy,u.FullName,i.ServedAt
                FROM dbo.OrderItems i JOIN dbo.DiningTables t ON t.Id=i.OriginalTableId LEFT JOIN dbo.Users u ON u.Id=i.ServedBy
                WHERE i.Id=@id;
                """, connection);
            info.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
            await using var reader = await info.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            var result = ServingQueue.ServeOutcomeFor(changed, User.ActorUserId(), reader.GetString(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2), reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Utc));
            status = result.Succeeded ? StatusCodes.Status200OK : StatusCodes.Status409Conflict;
            alreadyServed = !result.Succeeded;
            message = result.Message;
        }
        catch (SqlException ex) when (ex.Number is >= 51000 and < 51500)
        {
            (status, message) = ex.Number switch
            {
                51001 => (StatusCodes.Status403Forbidden, "Tài khoản không có quyền xác nhận mang món ra."),
                51029 => (StatusCodes.Status404NotFound, "Món không tồn tại hoặc bàn đã thanh toán."),
                51030 => (StatusCodes.Status409Conflict, "Món chưa nấu xong hoặc đã bị huỷ, không thể mang ra."),
                _ => (StatusCodes.Status409Conflict, ex.Message)
            };
        }

        if (IdleSessionEvents.IsApiRequest(Request))
            return StatusCode(status, new { id, served = status == StatusCodes.Status200OK, alreadyServed, message });
        TempData[status == StatusCodes.Status200OK ? "Success" : "Error"] = message;
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Món Ready, chưa phục vụ, của phiên chưa đóng. Giờ "hiện tại" lấy từ database (SYSUTCDATETIME) — cùng đồng hồ đã ghi ReadyAt —
    /// để thời gian chờ không lệch theo đồng hồ máy chủ web.
    /// </summary>
    private async Task<ServingQueueSnapshot> Load(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand("""
            SELECT SYSUTCDATETIME();
            SELECT i.Id,t.Code AS TableCode,i.ItemName,i.Unit,i.Quantity,i.Notes,i.Status,
                   COALESCE(i.ReadyAt,i.PreparingAt,i.SubmittedAt) AS ReadyAt,i.ServedAt
            FROM dbo.OrderItems i
            JOIN dbo.OrderBatches b ON b.Id=i.BatchId
            JOIN dbo.DiningSessions s ON s.Id=b.SessionId
            JOIN dbo.DiningTables t ON t.Id=i.OriginalTableId
            WHERE s.Status<>'Closed' AND i.Status='Ready' AND i.ServedAt IS NULL
            ORDER BY COALESCE(i.ReadyAt,i.PreparingAt,i.SubmittedAt), i.Id;
            """, connection);
        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var nowUtc = DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc);
        await reader.NextResultAsync(cancellationToken);
        var rows = new List<ReadyDishRow>();
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new ReadyDishRow(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetString(6),
                DateTime.SpecifyKind(reader.GetDateTime(7), DateTimeKind.Utc),
                reader.IsDBNull(8) ? null : DateTime.SpecifyKind(reader.GetDateTime(8), DateTimeKind.Utc)));
        return ServingQueue.Snapshot(rows, nowUtc);
    }
}
