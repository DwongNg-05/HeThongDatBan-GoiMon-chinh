using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models.Cashier;
using RestaurantManagement.Web.Models.Reservations;
using RestaurantManagement.Web.Security;

namespace RestaurantManagement.Web.Controllers;

/// <summary>
/// S1-04 Task 2: phần việc của Thu ngân — thanh toán, hoá đơn, chốt ca. Chỉ Quản lý và Thu ngân (Payments.Manage);
/// Phục vụ và Bếp bị chặn ở máy chủ. Thủ tục SQL (usp_Checkout, usp_OpenShift, usp_CloseShift) kiểm tra quyền lần nữa.
/// </summary>
[Route("Cashier")]
[Authorize(Roles = AppRoles.Cashiers)]
public sealed class CashierController(IConfiguration configuration) : Controller
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Chưa cấu hình kết nối database.");

    // ---------------- Thanh toán ----------------

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var shift = await ReadShift(connection, "Status='Open'", cancellationToken);
        var sessions = new List<PayableSession>();
        await using var command = new SqlCommand("""
            SELECT s.Id,
                   COALESCE((SELECT STRING_AGG(CONVERT(nvarchar(max),t.Code),N', ') FROM dbo.SessionTables st JOIN dbo.DiningTables t ON t.Id=st.TableId
                              JOIN dbo.DiningSessions x ON x.Id=st.SessionId WHERE (x.Id=s.Id OR x.BillingSessionId=s.Id) AND st.ReleasedAt IS NULL),N'') AS Tables,
                   (SELECT SUM(x.GuestCount) FROM dbo.DiningSessions x WHERE x.Id=s.Id OR x.BillingSessionId=s.Id) AS Guests,
                   s.Status,s.OpenedAt,
                   (SELECT COUNT(*) FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId JOIN dbo.DiningSessions x ON x.Id=b.SessionId
                     WHERE (x.Id=s.Id OR x.BillingSessionId=s.Id) AND i.Status<>'Cancelled') AS Items,
                   (SELECT COUNT(*) FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId JOIN dbo.DiningSessions x ON x.Id=b.SessionId
                     WHERE (x.Id=s.Id OR x.BillingSessionId=s.Id) AND i.Status NOT IN ('Served','Cancelled')) AS Unserved,
                   (SELECT COALESCE(SUM(i.LineTotal),0) FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId JOIN dbo.DiningSessions x ON x.Id=b.SessionId
                     WHERE (x.Id=s.Id OR x.BillingSessionId=s.Id) AND (i.Status<>'Cancelled' OR i.ChargeWhenCancelled=1)) AS Subtotal
            FROM dbo.DiningSessions s
            WHERE s.Status IN ('Open','AwaitingPayment') AND s.BillingSessionId IS NULL
            ORDER BY CASE s.Status WHEN 'AwaitingPayment' THEN 0 ELSE 1 END, s.OpenedAt;
            """, connection);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                sessions.Add(new PayableSession(reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3),
                    VietnamTime.FromUtc(reader.GetDateTime(4)), reader.GetInt32(5), reader.GetInt32(6), reader.GetDecimal(7)));
        return View(new PaymentsViewModel(shift, sessions));
    }

    /// <summary>
    /// S4-02 Task 1: màn hình chốt tiền mặt. Mọi giá trị món chỉ đọc và tổng tiền
    /// được tính lại ở SQL Server khi chốt, không tin giá trị do trình duyệt gửi lên.
    /// </summary>
    [HttpGet("Payment/{sessionId:long}")]
    public async Task<IActionResult> Payment(long sessionId, string? method, decimal? cashReceived, string? transferReference, string? error, Guid? requestId,
        CancellationToken cancellationToken)
    {
        var details = await ReadPaymentAsync(sessionId, cancellationToken);
        if (details is null) return NotFound();

        var payment = details.Value;
        var discount = await ReadDiscountAsync(sessionId, cancellationToken);
        var history = await ReadDiscountHistoryAsync(sessionId, cancellationToken);
        var paymentMethod = method is "Transfer" ? "Transfer" : "Cash";
        var received = cashReceived ?? (payment.Session.Subtotal - (discount?.Amount ?? 0));
        return View(new CashierPaymentViewModel(
            payment.Session,
            payment.Lines,
            paymentMethod,
            Math.Max(0, received),
            transferReference,
            requestId is { } id && id != Guid.Empty ? id : Guid.NewGuid(),
            discount,
            history,
            error));
    }

    /// <summary>S4-02 Task 3: giảm giá được lưu tại phiên và audit trước khi thanh toán.</summary>
    [HttpPost("Discount")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Discount(long sessionId, Guid requestId, string? discountType, decimal? discountValue, string? discountReason)
    {
        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await using var command = new SqlCommand("dbo.usp_SetSessionDiscount", connection) { CommandType = CommandType.StoredProcedure };
            command.Parameters.Add("@SessionId", SqlDbType.BigInt).Value = sessionId;
            command.Parameters.Add("@DiscountType", SqlDbType.VarChar, 10).Value = discountType ?? string.Empty;
            command.Parameters.Add("@DiscountValue", SqlDbType.Decimal).Value = discountValue ?? -1;
            command.Parameters.Add("@Reason", SqlDbType.NVarChar, 500).Value = discountReason?.Trim() ?? string.Empty;
            command.Parameters.Add("@ActorUserId", SqlDbType.Int).Value = User.ActorUserId();
            await connection.OpenAsync();
            await command.ExecuteNonQueryAsync();
            TempData["Success"] = "Đã áp dụng giảm giá. Tổng cần thu đã được tính lại.";
        }
        catch (SqlException ex) when (ex.Number is >= 51000 and < 51500)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Payment), new { sessionId, requestId });
    }

    [HttpPost("Discount/Remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveDiscount(long sessionId, Guid requestId)
    {
        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await using var command = new SqlCommand("dbo.usp_RemoveSessionDiscount", connection) { CommandType = CommandType.StoredProcedure };
            command.Parameters.Add("@SessionId", SqlDbType.BigInt).Value = sessionId;
            command.Parameters.Add("@ActorUserId", SqlDbType.Int).Value = User.ActorUserId();
            await connection.OpenAsync();
            await command.ExecuteNonQueryAsync();
            TempData["Success"] = "Đã gỡ giảm giá.";
        }
        catch (SqlException ex) when (ex.Number is >= 51000 and < 51500)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Payment), new { sessionId, requestId });
    }

    /// <summary>
    /// Chốt tiền mặt. RequestId sinh khi hiển thị form, vì vậy gửi lặp hoặc hai lần
    /// bấm chỉ trả về hoá đơn cũ, không thể tạo hai hoá đơn cho cùng phiên.
    /// </summary>
    [HttpPost("Checkout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Checkout(long sessionId, Guid requestId, string? method, decimal? cashReceived, string? transferReference)
    {
        if (requestId == Guid.Empty || method is not ("Cash" or "Transfer") ||
            (method == "Cash" && (cashReceived is null || cashReceived < 0)) ||
            (method == "Transfer" && !IsValidTransferReference(transferReference)))
        {
            return RedirectToAction(nameof(Payment), new { sessionId, method, cashReceived, transferReference, requestId,
                error = method == "Transfer" ? "Mã chuyển khoản gồm 6–50 ký tự chữ, số hoặc dấu gạch ngang." : "Vui lòng nhập số tiền khách đưa hợp lệ." });
        }
        try
        {
            var discount = await ReadDiscountAsync(sessionId, HttpContext.RequestAborted);
            await using var connection = new SqlConnection(ConnectionString);
            await using var command = new SqlCommand("dbo.usp_Checkout", connection) { CommandType = CommandType.StoredProcedure };
            command.Parameters.Add("@SessionId", SqlDbType.BigInt).Value = sessionId;
            command.Parameters.Add("@RequestId", SqlDbType.UniqueIdentifier).Value = requestId;
            command.Parameters.Add("@Method", SqlDbType.VarChar, 10).Value = method;
            command.Parameters.Add("@ActorUserId", SqlDbType.Int).Value = User.ActorUserId();
            command.Parameters.Add("@DiscountType", SqlDbType.VarChar, 10).Value = discount?.Type ?? "None";
            command.Parameters.Add("@DiscountValue", SqlDbType.Decimal).Value = discount?.Value ?? 0;
            command.Parameters.Add("@DiscountReason", SqlDbType.NVarChar, 500).Value = discount?.Reason ?? (object)DBNull.Value;
            command.Parameters.Add("@CashReceived", SqlDbType.Decimal).Value = method == "Cash" ? cashReceived!.Value : DBNull.Value;
            command.Parameters.Add("@TransferReference", SqlDbType.NVarChar, 100).Value = method == "Transfer" ? transferReference!.Trim().ToUpperInvariant() : DBNull.Value;
            await connection.OpenAsync();
            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                return RedirectToAction(nameof(Receipt), new { invoiceId = reader.GetInt64(reader.GetOrdinal("InvoiceId")) });
        }
        catch (SqlException ex) when (ex.Number is >= 51000 and < 51500)
        {
            return RedirectToAction(nameof(Payment), new { sessionId, method, cashReceived, transferReference, requestId,
                error = ex.Number == 51001 ? "Tài khoản không có quyền thanh toán." : ex.Message });
        }
        return RedirectToAction(nameof(Payment), new { sessionId, method, cashReceived, transferReference, requestId,
            error = "Không thể chốt thanh toán. Vui lòng thử lại." });
    }

    [HttpGet("Receipt/{invoiceId:long}")]
    public async Task<IActionResult> Receipt(long invoiceId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT i.Id,i.InvoiceNumber,i.TableLabels,i.GuestCount,i.IssuedAt,
                   u.FullName,i.Subtotal,i.Total,p.Method,p.CashReceived,p.ChangeAmount,p.TransferReference
            FROM dbo.Invoices i
            JOIN dbo.Payments p ON p.InvoiceId=i.Id
            JOIN dbo.Users u ON u.Id=i.IssuedBy
            WHERE i.Id=@id AND i.Status='Paid';

            SELECT l.ItemName,l.Unit,l.Quantity,l.UnitPrice,l.LineTotal,l.Notes,l.OriginalTableCode,l.IsChargedCancellation
            FROM dbo.InvoiceLines l WHERE l.InvoiceId=@id ORDER BY l.Id;
            """, connection);
        command.Parameters.Add("@id", SqlDbType.BigInt).Value = invoiceId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return NotFound();

        var id = reader.GetInt64(0);
        var number = reader.GetString(1);
        var tables = reader.GetString(2);
        var guests = reader.GetInt32(3);
        var issued = VietnamTime.FromUtc(reader.GetDateTime(4));
        var cashier = reader.GetString(5);
        var subtotal = reader.GetDecimal(6);
        var total = reader.GetDecimal(7);
        var method = reader.GetString(8);
        decimal? received = reader.IsDBNull(9) ? null : reader.GetDecimal(9);
        decimal? change = reader.IsDBNull(10) ? null : reader.GetDecimal(10);
        var transferReference = reader.IsDBNull(11) ? null : reader.GetString(11);
        var lines = new List<CashierPaymentLine>();
        if (await reader.NextResultAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                lines.Add(new CashierPaymentLine(reader.GetString(0), reader.GetString(1), reader.GetInt32(2),
                    reader.GetDecimal(3), reader.GetDecimal(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.GetString(6), reader.GetBoolean(7)));
        return View(new CashierReceiptViewModel(id, number, tables, guests, issued, cashier, subtotal, total, method, received, change, transferReference, lines));
    }

    // ---------------- Hoá đơn ----------------

    [HttpGet("Invoices")]
    public async Task<IActionResult> Invoices(DateOnly? date, CancellationToken cancellationToken)
    {
        var day = date ?? DateOnly.FromDateTime(VietnamTime.FromUtc(DateTime.UtcNow));
        var invoices = new List<InvoiceRow>();
        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand("""
            SELECT i.Id,i.InvoiceNumber,i.TableLabels,i.GuestCount,i.IssuedAt,i.Subtotal,i.DiscountAmount,i.Total,p.Method,i.Status
            FROM dbo.Invoices i JOIN dbo.Payments p ON p.InvoiceId=i.Id
            WHERE i.BusinessDate=@day ORDER BY i.IssuedAt DESC,i.Id DESC;
            """, connection);
        command.Parameters.Add("@day", SqlDbType.Date).Value = day.ToDateTime(TimeOnly.MinValue);
        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            invoices.Add(new InvoiceRow(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3),
                VietnamTime.FromUtc(reader.GetDateTime(4)), reader.GetDecimal(5), reader.GetDecimal(6), reader.GetDecimal(7),
                reader.GetString(8), reader.GetString(9)));
        return View(new InvoicesViewModel(day, invoices));
    }

    // ---------------- Chốt ca ----------------

    [HttpGet("Shift")]
    public async Task<IActionResult> Shift(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var open = await ReadShift(connection, "Status='Open'", cancellationToken);
        var closed = await ReadShift(connection, "Status='Closed'", cancellationToken);
        var openSessions = 0;
        if (open is not null)
        {
            await using var count = new SqlCommand("SELECT COUNT(*) FROM dbo.DiningSessions WHERE ShiftId=@id AND Status<>'Closed';", connection);
            count.Parameters.Add("@id", SqlDbType.BigInt).Value = open.Id;
            openSessions = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken));
        }
        return View(new ShiftViewModel(open, closed, openSessions));
    }

    [HttpPost("OpenShift")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OpenShift(string? name, decimal openingCash)
    {
        return await Run("dbo.usp_OpenShift", command =>
        {
            command.Parameters.Add("@Name", SqlDbType.NVarChar, 100).Value = string.IsNullOrWhiteSpace(name) ? "Ca " + VietnamTime.FromUtc(DateTime.UtcNow).ToString("dd/MM HH:mm") : name.Trim();
            command.Parameters.Add("@OpeningCash", SqlDbType.Decimal).Value = openingCash;
        }, "Đã mở ca.");
    }

    [HttpPost("CloseShift")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CloseShift(long shiftId, decimal countedCash, string? explanation)
    {
        return await Run("dbo.usp_CloseShift", command =>
        {
            command.Parameters.Add("@ShiftId", SqlDbType.BigInt).Value = shiftId;
            command.Parameters.Add("@CountedCash", SqlDbType.Decimal).Value = countedCash;
            command.Parameters.Add("@Explanation", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(explanation) ? (object)DBNull.Value : explanation.Trim();
        }, "Đã chốt ca.");
    }

    private async Task<IActionResult> Run(string procedure, Action<SqlCommand> parameters, string success)
    {
        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await using var command = new SqlCommand(procedure, connection) { CommandType = CommandType.StoredProcedure };
            parameters(command);
            command.Parameters.Add("@ActorUserId", SqlDbType.Int).Value = User.ActorUserId();
            await connection.OpenAsync();
            await command.ExecuteNonQueryAsync();
            TempData["Success"] = success;
        }
        catch (SqlException ex) when (ex.Number is >= 51000 and < 51500)
        {
            TempData["Error"] = ex.Number == 51001 ? "Tài khoản không có quyền mở/chốt ca." : ex.Message;
        }
        catch (SqlException ex) when (ex.Number is 547 or 8114)
        {
            TempData["Error"] = "Số tiền không hợp lệ.";
        }
        return RedirectToAction(nameof(Shift));
    }

    private static async Task<ShiftSummary?> ReadShift(SqlConnection connection, string where, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand($"""
            SELECT TOP(1) s.Id,s.Name,s.BusinessDate,s.OpenedAt,s.OpeningCash,s.Status,
                   COALESCE(s.InvoiceCount,(SELECT COUNT(*) FROM dbo.Invoices i WHERE i.ShiftId=s.Id AND i.Status='Paid')) AS InvoiceCount,
                   COALESCE(s.CashRevenue,(SELECT COALESCE(SUM(p.Amount),0) FROM dbo.Invoices i JOIN dbo.Payments p ON p.InvoiceId=i.Id WHERE i.ShiftId=s.Id AND i.Status='Paid' AND p.Method='Cash')) AS CashRevenue,
                   COALESCE(s.TransferRevenue,(SELECT COALESCE(SUM(p.Amount),0) FROM dbo.Invoices i JOIN dbo.Payments p ON p.InvoiceId=i.Id WHERE i.ShiftId=s.Id AND i.Status='Paid' AND p.Method='Transfer')) AS TransferRevenue,
                   s.ClosedAt,s.CountedCash,s.ExpectedCash,s.CashDifference,s.Explanation
            FROM dbo.Shifts s WHERE s.{where} ORDER BY COALESCE(s.ClosedAt,s.OpenedAt) DESC,s.Id DESC;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        decimal? Money(int i) => reader.IsDBNull(i) ? null : reader.GetDecimal(i);
        return new ShiftSummary(reader.GetInt64(0), reader.GetString(1), reader.GetDateTime(2), VietnamTime.FromUtc(reader.GetDateTime(3)),
            reader.GetDecimal(4), reader.GetString(5), reader.GetInt32(6), reader.GetDecimal(7), reader.GetDecimal(8),
            reader.IsDBNull(9) ? null : VietnamTime.FromUtc(reader.GetDateTime(9)), Money(10), Money(11), Money(12),
            reader.IsDBNull(13) ? null : reader.GetString(13));
    }

    private async Task<(PayableSession Session, IReadOnlyList<CashierPaymentLine> Lines)?> ReadPaymentAsync(long sessionId,
        CancellationToken cancellationToken)
    {
        if (sessionId <= 0) return null;
        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand("""
            SELECT s.Id,
                   COALESCE((SELECT STRING_AGG(CONVERT(nvarchar(max),t.Code),N', ') FROM dbo.SessionTables st
                       JOIN dbo.DiningTables t ON t.Id=st.TableId
                       JOIN dbo.DiningSessions x ON x.Id=st.SessionId
                       WHERE (x.Id=s.Id OR x.BillingSessionId=s.Id) AND st.ReleasedAt IS NULL),N'') AS Tables,
                   (SELECT SUM(x.GuestCount) FROM dbo.DiningSessions x WHERE x.Id=s.Id OR x.BillingSessionId=s.Id) AS Guests,
                   s.Status,s.OpenedAt,
                   (SELECT COUNT(*) FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId JOIN dbo.DiningSessions x ON x.Id=b.SessionId
                       WHERE (x.Id=s.Id OR x.BillingSessionId=s.Id) AND (i.Status<>'Cancelled' OR i.ChargeWhenCancelled=1)) AS Items,
                   (SELECT COUNT(*) FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId JOIN dbo.DiningSessions x ON x.Id=b.SessionId
                       WHERE (x.Id=s.Id OR x.BillingSessionId=s.Id) AND i.Status NOT IN ('Served','Cancelled')) AS Unserved,
                   (SELECT COALESCE(SUM(i.LineTotal),0) FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId JOIN dbo.DiningSessions x ON x.Id=b.SessionId
                       WHERE (x.Id=s.Id OR x.BillingSessionId=s.Id) AND (i.Status<>'Cancelled' OR i.ChargeWhenCancelled=1)) AS Subtotal
            FROM dbo.DiningSessions s
            WHERE s.Id=@sessionId AND s.BillingSessionId IS NULL AND s.Status IN ('Open','AwaitingPayment');

            SELECT i.ItemName,i.Unit,i.Quantity,i.UnitPrice,i.LineTotal,i.Notes,t.Code,i.ChargeWhenCancelled
            FROM dbo.OrderItems i
            JOIN dbo.OrderBatches b ON b.Id=i.BatchId
            JOIN dbo.DiningSessions s ON s.Id=b.SessionId
            JOIN dbo.DiningTables t ON t.Id=i.OriginalTableId
            WHERE (s.Id=@sessionId OR s.BillingSessionId=@sessionId)
              AND (i.Status<>'Cancelled' OR i.ChargeWhenCancelled=1)
            ORDER BY b.BatchNumber,i.Id;
            """, connection);
        command.Parameters.Add("@sessionId", SqlDbType.BigInt).Value = sessionId;
        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var session = new PayableSession(reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3),
            VietnamTime.FromUtc(reader.GetDateTime(4)), reader.GetInt32(5), reader.GetInt32(6), reader.GetDecimal(7));
        var lines = new List<CashierPaymentLine>();
        if (await reader.NextResultAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                lines.Add(new CashierPaymentLine(reader.GetString(0), reader.GetString(1), reader.GetInt32(2),
                    reader.GetDecimal(3), reader.GetDecimal(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.GetString(6), reader.GetBoolean(7)));
        return (session, lines);
    }

    private async Task<SessionDiscount?> ReadDiscountAsync(long sessionId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand("SELECT DiscountType,DiscountValue,DiscountAmount,Reason,UpdatedAt FROM dbo.SessionDiscounts WHERE SessionId=@id;", connection);
        command.Parameters.Add("@id", SqlDbType.BigInt).Value = sessionId;
        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new SessionDiscount(reader.GetString(0), reader.GetDecimal(1), reader.GetDecimal(2), reader.GetString(3), VietnamTime.FromUtc(reader.GetDateTime(4)))
            : null;
    }

    private async Task<IReadOnlyList<DiscountHistoryEntry>> ReadDiscountHistoryAsync(long sessionId, CancellationToken cancellationToken)
    {
        var history = new List<DiscountHistoryEntry>();
        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand("""
            SELECT TOP(10) Action,Reason,OccurredAt FROM dbo.AuditLogs
            WHERE EntityType='DiningSession' AND EntityId=CONVERT(varchar(60),@id)
              AND Action IN ('SessionDiscountApplied','SessionDiscountChanged','SessionDiscountRemoved')
            ORDER BY OccurredAt DESC,Id DESC;
            """, connection);
        command.Parameters.Add("@id", SqlDbType.BigInt).Value = sessionId;
        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            history.Add(new DiscountHistoryEntry(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), VietnamTime.FromUtc(reader.GetDateTime(2))));
        return history;
    }

    private static bool IsValidTransferReference(string? value) => value is { Length: >= 6 and <= 50 }
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
}
