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
    /// S3-05 Task 1: huỷ một dòng món từ hộp xác nhận trên hoá đơn tạm tính (docs/S3-05-Task1.md).
    /// - Lý do bắt buộc: khách đổi ý, gọi nhầm hoặc hết nguyên liệu; "Quản lý yêu cầu" chỉ dành cho Quản lý.
    /// - expectedStatus: trạng thái dòng món lúc mở hộp xác nhận. Bếp đổi trạng thái trong lúc nhân viên chọn lý do → từ chối.
    /// - Database (dbo.usp_CancelOrderItem, migration 056) kiểm tra lại quyền, dòng món thuộc đúng hoá đơn, lý do và trạng thái
    ///   mới nhất trong một giao dịch có khoá; Phục vụ chỉ huỷ được món còn chờ bếp. Huỷ trước khi bếp làm thì không tính tiền.
    /// - Gửi lặp / nhiều người cùng huỷ: chỉ lần đầu được ghi, các lần sau báo "đã được hủy trước đó" (không trừ tiền thêm).
    /// - S3-05 Task 3: Quản lý huỷ món đang nấu / đã xong (chưa mang ra) với một trong ba lý do và ô xác nhận confirmCharged;
    ///   món vẫn tính tiền, dòng giữ trên hoá đơn với nhãn "Đã huỷ có tính tiền", bếp nhận thẻ "Dừng món".
    /// Mỗi lần huỷ thành công ghi nhật ký (dòng món, người thực hiện, thời điểm, lý do). Kết quả hiện ở đầu hoá đơn sau khi tải lại.
    /// </summary>
    [HttpPost("{sessionId:long}/CancelItem/{orderItemId:long}")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.FrontOfHouse)]
    public async Task<IActionResult> CancelItem(long sessionId, long orderItemId, string? reason, string? note, string? expectedStatus,
        CancellationToken cancellationToken, bool confirmCharged = false)
    {
        var actorUserId = User.ActorUserId();
        if (actorUserId <= 0)
            return Forbid();

        // S3-05 Task 2: mọi yêu cầu bị từ chối (web chặn trước hoặc database từ chối) được ghi vào nhật ký huỷ của ca.
        var log = new ShiftCancellationStore(ConnectionString);
        async Task<IActionResult> Refuse(int number, string message)
        {
            await log.LogRejectionAsync(orderItemId > 0 ? orderItemId : null, sessionId, actorUserId, reason, number, message);
            return Back(sessionId, "Error", message);
        }

        var isManager = User.IsInRole(AppRoles.Manager);
        if (string.IsNullOrWhiteSpace(reason))
            return await Refuse(51032, OrderCancelRules.MissingReason);
        if (!OrderCancelRules.IsAllowed(reason, isManager))
            return reason == OrderCancelRules.ManagerOverride
                ? await Refuse(51036, OrderCancelRules.ManagerOnlyReason)
                : await Refuse(51032, OrderCancelRules.MissingReason);
        if (orderItemId <= 0 || (expectedStatus is not null && !OrderCancelRules.IsKnownStatus(expectedStatus)))
            return await Refuse(51037, OrderCancelRules.InvalidRequest);
        // S3-05 Task 3: huỷ có tính tiền (Quản lý, món đang nấu / đã xong) cần một trong ba lý do và xác nhận rõ "có tính tiền".
        // Database kiểm tra lại với trạng thái mới nhất (kể cả khi không gửi expectedStatus).
        if (isManager && OrderCancelRules.IsChargedStatus(expectedStatus))
        {
            if (!OrderCancelRules.IsAllowedForCharged(reason))
                return await Refuse(51142, OrderCancelRules.ChargedReasonRequired);
            if (!confirmCharged)
                return await Refuse(51144, OrderCancelRules.ChargedConfirmRequired);
        }

        try
        {
            var result = await new OrderCancelStore(ConnectionString)
                .CancelAsync(orderItemId, sessionId, reason, note, expectedStatus, actorUserId, cancellationToken, confirmCharged);
            // Huỷ thành công đã có nhật ký trong dbo.OrderItemEvents; lần xác nhận lặp (AlreadyCancelled) không ghi thêm.
            return Back(sessionId, result.AlreadyCancelled ? "Info" : "Success", result.Message);
        }
        catch (SqlException exception) when (exception.Number is >= 51000 and < 51500)
        {
            return await Refuse(exception.Number, OrderCancelRules.RejectionMessage(exception.Number, exception.Message));
        }
    }

    /// <summary>Về lại hoá đơn (tải lại trạng thái mới nhất của từng dòng và tạm tính) kèm thông báo kết quả.</summary>
    private RedirectToActionResult Back(long sessionId, string kind, string message)
    {
        TempData[kind] = message;
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
