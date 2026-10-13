using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models.Orders;
using RestaurantManagement.Web.Models.Reservations;
using RestaurantManagement.Web.Security;

namespace RestaurantManagement.Web.Controllers;

/// <summary>
/// S3-04 Task 4: hoá đơn tạm tính (chỉ đọc) — nhân viên phục vụ đối chiếu yêu cầu của khách khi mang món ra.
/// Mỗi dòng dbo.OrderItems là một dòng hoá đơn (không gộp các dòng cùng món), dưới tên món là lựa chọn nhanh (OrderItems.QuickNotes)
/// và ghi chú (OrderItems.Notes) — đúng hai cột màn hình bếp đọc, qua cùng lớp OrderItemRequests, nên hai nơi luôn khớp.
/// Không lưu bản sao: mỗi lần mở đọc lại database, và trigger tr_OrderItems_KeepNotes (048) chặn sửa ghi chú đã gửi bếp,
/// nên ghi chú giữ nguyên khi đơn được cập nhật (gọi thêm, đổi trạng thái, huỷ món) hoặc khi mở lại hoá đơn.
/// Quyền: Quản lý, Phục vụ, Thu ngân (AppRoles.BillReaders). Tài liệu: docs/S3-04-Task4.md.
/// </summary>
[Route("ProvisionalBill")]
[Authorize(Roles = AppRoles.BillReaders)]
public sealed class ProvisionalBillController(IConfiguration configuration) : Controller
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Chưa cấu hình kết nối database.");

    /// <summary>Hoá đơn tạm tính của phiên (mã phiên con của bàn đã gộp cũng được: hiển thị cả phiên thanh toán chung).</summary>
    [HttpGet("{sessionId:long}")]
    public async Task<IActionResult> Index(long sessionId, CancellationToken cancellationToken)
    {
        var bill = await Load(sessionId, cancellationToken);
        if (bill is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return View("Missing", new ProvisionalBillMissingViewModel("Không tìm thấy phiên phục vụ này."));
        }
        return View(bill);
    }

    /// <summary>Mở hoá đơn tạm tính theo mã bàn (từ chi tiết bàn trên sơ đồ bàn): chuyển tới phiên đang phục vụ của bàn.</summary>
    [HttpGet("Table/{code}")]
    public async Task<IActionResult> Table(string code, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand("""
            SELECT TOP(1) COALESCE(s.BillingSessionId,s.Id)
            FROM dbo.SessionTables st
            JOIN dbo.DiningTables t ON t.Id=st.TableId
            JOIN dbo.DiningSessions s ON s.Id=st.SessionId
            WHERE t.Code=@code AND st.ReleasedAt IS NULL AND s.Status<>'Closed'
            ORDER BY s.OpenedAt DESC;
            """, connection);
        command.Parameters.Add("@code", SqlDbType.VarChar, 20).Value = code;
        await connection.OpenAsync(cancellationToken);
        if (code.Length <= 20 && await command.ExecuteScalarAsync(cancellationToken) is long sessionId)
            return RedirectToAction(nameof(Index), new { sessionId });
        Response.StatusCode = StatusCodes.Status404NotFound;
        return View("Missing", new ProvisionalBillMissingViewModel($"Bàn {code} hiện không có khách, chưa có hoá đơn tạm tính."));
    }

    /// <summary>
    /// S3-05: Phục vụ có thể huỷ món còn chờ bếp; khi bếp đã bắt đầu, chỉ Quản lý
    /// có thể huỷ và thủ tục SQL sẽ tự đánh dấu dòng đó vẫn tính tiền. Lý do cùng
    /// người thao tác/thời điểm đều được dbo.usp_CancelOrderItem ghi vào lịch sử.
    /// </summary>
    [HttpPost("{sessionId:long}/CancelItem/{orderItemId:long}")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.FrontOfHouse)]
    public async Task<IActionResult> CancelItem(long sessionId, long orderItemId, string? reason, string? note,
        CancellationToken cancellationToken)
    {
        if (orderItemId <= 0 || reason is not ("ChangedMind" or "Mistake" or "SoldOut" or "ManagerOverride"))
        {
            TempData["Error"] = "Hãy chọn lý do huỷ món hợp lệ.";
            return RedirectToAction(nameof(Index), new { sessionId });
        }

        var actorUserId = User.ActorUserId();
        if (actorUserId <= 0)
            return Forbid();

        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await using var command = new SqlCommand("dbo.usp_CancelOrderItem", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            command.Parameters.Add("@OrderItemId", SqlDbType.BigInt).Value = orderItemId;
            command.Parameters.Add("@Reason", SqlDbType.VarChar, 30).Value = reason;
            command.Parameters.Add("@ActorUserId", SqlDbType.Int).Value = actorUserId;
            command.Parameters.Add("@Note", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(note)
                ? DBNull.Value
                : note.Trim()[..Math.Min(note.Trim().Length, 500)];
            await connection.OpenAsync(cancellationToken);
            await command.ExecuteNonQueryAsync(cancellationToken);
            TempData["Success"] = "Đã huỷ món. Lịch sử huỷ được lưu ngay bên dưới hoá đơn.";
        }
        catch (SqlException exception) when (exception.Number is >= 51000 and < 51500)
        {
            TempData["Error"] = exception.Number switch
            {
                51001 => "Tài khoản không có quyền huỷ món ở trạng thái này.",
                51031 => "Món không còn được huỷ vì đã huỷ hoặc phiên đã thanh toán.",
                51032 => "Lý do huỷ món không hợp lệ.",
                _ => exception.Message
            };
        }

        return RedirectToAction(nameof(Index), new { sessionId });
    }

    private async Task<ProvisionalBillViewModel?> Load(long sessionId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand("""
            DECLARE @root bigint=(SELECT COALESCE(BillingSessionId,Id) FROM dbo.DiningSessions WHERE Id=@id);
            SELECT s.Id,s.Status,s.OpenedAt,
                   COALESCE((SELECT STRING_AGG(CONVERT(nvarchar(max),t.Code),N', ') WITHIN GROUP (ORDER BY t.Code)
                             FROM dbo.SessionTables st JOIN dbo.DiningTables t ON t.Id=st.TableId JOIN dbo.DiningSessions x ON x.Id=st.SessionId
                             WHERE (x.Id=s.Id OR x.BillingSessionId=s.Id) AND (st.ReleasedAt IS NULL OR s.Status='Closed')),N'') AS Tables,
                   (SELECT SUM(x.GuestCount) FROM dbo.DiningSessions x WHERE x.Id=s.Id OR x.BillingSessionId=s.Id) AS Guests
            FROM dbo.DiningSessions s WHERE s.Id=@root;
            SELECT i.Id,t.Code,i.ItemName,i.Unit,i.UnitPrice,i.Quantity,i.LineTotal,i.Status,i.ChargeWhenCancelled,i.SubmittedAt,i.Notes,i.QuickNotes,
                   i.CancelledAt,cancelledBy.FullName,i.CancelReason,i.CancellationNote
            FROM dbo.OrderItems i
            JOIN dbo.OrderBatches b ON b.Id=i.BatchId
            JOIN dbo.DiningSessions x ON x.Id=b.SessionId
            JOIN dbo.DiningTables t ON t.Id=i.OriginalTableId
            LEFT JOIN dbo.Users cancelledBy ON cancelledBy.Id=i.CancelledBy
            WHERE x.Id=@root OR x.BillingSessionId=@root
            ORDER BY i.SubmittedAt, i.Id;
            """, connection);
        command.Parameters.Add("@id", SqlDbType.BigInt).Value = sessionId;
        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var (id, status, openedAt, tables, guests) = (reader.GetInt64(0), reader.GetString(1), VietnamTime.FromUtc(reader.GetDateTime(2)),
            reader.GetString(3), reader.GetInt32(4));
        await reader.NextResultAsync(cancellationToken);
        var lines = new List<ProvisionalBillLine>();
        while (await reader.ReadAsync(cancellationToken))
            lines.Add(new ProvisionalBillLine(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetDecimal(4), reader.GetInt32(5), reader.GetDecimal(6), reader.GetString(7), reader.GetBoolean(8),
                VietnamTime.FromUtc(reader.GetDateTime(9)), reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : VietnamTime.FromUtc(reader.GetDateTime(12)),
                reader.IsDBNull(13) ? null : reader.GetString(13), reader.IsDBNull(14) ? null : reader.GetString(14),
                reader.IsDBNull(15) ? null : reader.GetString(15)));
        return new ProvisionalBillViewModel(id, tables, guests, status, openedAt, lines);
    }
}
