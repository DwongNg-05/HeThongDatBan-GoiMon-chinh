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
                     WHERE (x.Id=s.Id OR x.BillingSessionId=s.Id) AND (i.Status<>'Cancelled' OR i.ChargeWhenCancelled=1)) AS Items,
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

    /// <summary>Thanh toán một bàn. RequestId sinh khi hiển thị form, nên bấm 2 lần cũng chỉ ra 1 hoá đơn.</summary>
    [HttpPost("Checkout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Checkout(long sessionId, Guid requestId, string? method, decimal? cashReceived, string? transferReference)
    {
        if (requestId == Guid.Empty || method is not ("Cash" or "Transfer"))
        {
            TempData["Error"] = "Vui lòng chọn hình thức thanh toán.";
            return RedirectToAction(nameof(Index));
        }
        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await using var command = new SqlCommand("dbo.usp_Checkout", connection) { CommandType = CommandType.StoredProcedure };
            command.Parameters.Add("@SessionId", SqlDbType.BigInt).Value = sessionId;
            command.Parameters.Add("@RequestId", SqlDbType.UniqueIdentifier).Value = requestId;
            command.Parameters.Add("@Method", SqlDbType.VarChar, 10).Value = method;
            command.Parameters.Add("@ActorUserId", SqlDbType.Int).Value = User.ActorUserId();
            command.Parameters.Add("@CashReceived", SqlDbType.Decimal).Value = method == "Cash" && cashReceived is not null ? cashReceived.Value : (object)DBNull.Value;
            command.Parameters.Add("@TransferReference", SqlDbType.NVarChar, 100).Value = method == "Transfer" && !string.IsNullOrWhiteSpace(transferReference) ? transferReference.Trim() : (object)DBNull.Value;
            await connection.OpenAsync();
            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                TempData["Success"] = $"Đã thanh toán hoá đơn {reader.GetString(reader.GetOrdinal("InvoiceNumber"))}, tổng {Vnd.Format(reader.GetDecimal(reader.GetOrdinal("Total")))}.";
        }
        catch (SqlException ex) when (ex.Number is >= 51000 and < 51500)
        {
            TempData["Error"] = ex.Number == 51001 ? "Tài khoản không có quyền thanh toán." : ex.Message;
        }
        return RedirectToAction(nameof(Index));
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

    [HttpGet("Invoices/{id:long}")]
    public async Task<IActionResult> InvoiceDetails(long id, CancellationToken ct)
    {
        await using var cn = new SqlConnection(ConnectionString); await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("""
            SELECT i.Id,i.InvoiceNumber,i.TableLabels,i.GuestCount,i.IssuedAt,i.Subtotal,i.DiscountAmount,i.Total,p.Method,i.Status
            FROM dbo.Invoices i JOIN dbo.Payments p ON p.InvoiceId=i.Id WHERE i.Id=@id;
            SELECT OrderItemId,ItemName,Unit,Quantity,UnitPrice,LineTotal,OriginalTableCode,IsChargedCancellation
            FROM dbo.InvoiceLines WHERE InvoiceId=@id ORDER BY Id;
            """, cn);
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return NotFound();
        var invoice = new InvoiceRow(reader.GetInt64(0),reader.GetString(1),reader.GetString(2),reader.GetInt32(3),
            VietnamTime.FromUtc(reader.GetDateTime(4)),reader.GetDecimal(5),reader.GetDecimal(6),reader.GetDecimal(7),reader.GetString(8),reader.GetString(9));
        await reader.NextResultAsync(ct); var lines = new List<InvoiceLineDetail>();
        while (await reader.ReadAsync(ct))
            lines.Add(new(reader.GetInt64(0),reader.GetString(1),reader.GetString(2),reader.GetInt32(3),
                reader.GetDecimal(4),reader.GetDecimal(5),reader.GetString(6),reader.GetBoolean(7)));
        return View(new InvoiceDetailsViewModel(invoice, lines));
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
}
