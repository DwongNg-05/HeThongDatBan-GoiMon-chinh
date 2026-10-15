using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models.Cashier;
using RestaurantManagement.Web.Models.Reservations;
using RestaurantManagement.Web.Security;

namespace RestaurantManagement.Web.Controllers;

[Route("Cashier")]
[Authorize(Roles = AppRoles.Cashiers)]
public sealed class CashierController(
    IConfiguration configuration) : Controller
{
    private string ConnectionString =>
        Environment.GetEnvironmentVariable("RM_CONNECTION_STRING")
        ?? configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException(
            "Chưa cấu hình kết nối database.");

    [HttpGet("")]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken)
    {
        await using var connection =
            new SqlConnection(ConnectionString);

        await connection.OpenAsync(cancellationToken);

        var shift = await ReadShift(
            connection,
            "Status='Open'",
            cancellationToken);

        var sessions = new List<PayableSession>();

        await using var command = new SqlCommand("""
            SELECT
                s.Id,
                COALESCE(
                    (
                        SELECT STRING_AGG(
                            CONVERT(nvarchar(max), t.Code), N', ')
                        FROM dbo.SessionTables st
                        JOIN dbo.DiningTables t ON t.Id = st.TableId
                        JOIN dbo.DiningSessions x ON x.Id = st.SessionId
                        WHERE (x.Id = s.Id OR x.BillingSessionId = s.Id)
                          AND st.ReleasedAt IS NULL
                    ), N''
                ) AS Tables,
                (
                    SELECT SUM(x.GuestCount)
                    FROM dbo.DiningSessions x
                    WHERE x.Id = s.Id OR x.BillingSessionId = s.Id
                ) AS Guests,
                s.Status,
                s.OpenedAt,
                (
                    SELECT COUNT(*)
                    FROM dbo.OrderItems i
                    JOIN dbo.OrderBatches b ON b.Id = i.BatchId
                    JOIN dbo.DiningSessions x ON x.Id = b.SessionId
                    WHERE (x.Id = s.Id OR x.BillingSessionId = s.Id)
                      AND i.Status <> 'Cancelled'
                ) AS Items,
                (
                    SELECT COUNT(*)
                    FROM dbo.OrderItems i
                    JOIN dbo.OrderBatches b ON b.Id = i.BatchId
                    JOIN dbo.DiningSessions x ON x.Id = b.SessionId
                    WHERE (x.Id = s.Id OR x.BillingSessionId = s.Id)
                      AND i.Status NOT IN ('Served', 'Cancelled')
                ) AS Unserved,
                (
                    SELECT COALESCE(SUM(i.LineTotal), 0)
                    FROM dbo.OrderItems i
                    JOIN dbo.OrderBatches b ON b.Id = i.BatchId
                    JOIN dbo.DiningSessions x ON x.Id = b.SessionId
                    WHERE (x.Id = s.Id OR x.BillingSessionId = s.Id)
                      AND (
                          i.Status <> 'Cancelled'
                          OR i.ChargeWhenCancelled = 1
                      )
                ) AS Subtotal
            FROM dbo.DiningSessions s
            WHERE s.Status IN ('Open', 'AwaitingPayment')
              AND s.BillingSessionId IS NULL
            ORDER BY
                CASE s.Status
                    WHEN 'AwaitingPayment' THEN 0
                    ELSE 1
                END,
                s.OpenedAt;
            """, connection);

        await using (var reader =
            await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                sessions.Add(new PayableSession(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetString(3),
                    VietnamTime.FromUtc(reader.GetDateTime(4)),
                    reader.GetInt32(5),
                    reader.GetInt32(6),
                    reader.GetDecimal(7)));
            }
        }

        return View(new PaymentsViewModel(shift, sessions));
    }

    [HttpPost("Checkout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Checkout(
        long sessionId,
        Guid requestId,
        string? method,
        decimal? cashReceived,
        string? transferReference)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Thông tin thanh toán không hợp lệ.";
            return RedirectToAction(nameof(Index));
        }

        if (requestId == Guid.Empty
            || method is not ("Cash" or "Transfer"))
        {
            TempData["Error"] =
                "Vui lòng chọn hình thức thanh toán.";

            return RedirectToAction(nameof(Index));
        }

        try
        {
            await using var connection =
                new SqlConnection(ConnectionString);

            await using var command = new SqlCommand(
                "dbo.usp_Checkout", connection)
            {
                CommandType = CommandType.StoredProcedure
            };

            command.Parameters.Add(
                "@SessionId", SqlDbType.BigInt).Value = sessionId;

            command.Parameters.Add(
                "@RequestId", SqlDbType.UniqueIdentifier).Value = requestId;

            command.Parameters.Add(
                "@Method", SqlDbType.VarChar, 10).Value = method;

            command.Parameters.Add(
                "@ActorUserId", SqlDbType.Int).Value = User.ActorUserId();

            AddMoney(
                command,
                "@CashReceived",
                method == "Cash" ? cashReceived : null);

            command.Parameters.Add(
                "@TransferReference", SqlDbType.NVarChar, 100).Value =
                method == "Transfer"
                && !string.IsNullOrWhiteSpace(transferReference)
                    ? transferReference.Trim()
                    : (object)DBNull.Value;

            await connection.OpenAsync();

            await using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                var number = reader.GetString(
                    reader.GetOrdinal("InvoiceNumber"));

                var total = reader.GetDecimal(
                    reader.GetOrdinal("Total"));

                TempData["Success"] =
                    $"Đã thanh toán hoá đơn {number}, "
                    + $"tổng {Vnd.Format(total)}.";
            }
        }
        catch (SqlException ex)
            when (ex.Number is >= 51000 and < 51500)
        {
            TempData["Error"] = ex.Number == 51001
                ? "Tài khoản không có quyền thanh toán."
                : ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet("Invoices")]
    public async Task<IActionResult> Invoices(
        DateOnly? date,
        CancellationToken cancellationToken)
    {
        var day = date ?? DateOnly.FromDateTime(
            VietnamTime.FromUtc(DateTime.UtcNow));

        var invoices = new List<InvoiceRow>();

        await using var connection =
            new SqlConnection(ConnectionString);

        await using var command = new SqlCommand("""
            SELECT
                i.Id,
                i.InvoiceNumber,
                i.TableLabels,
                i.GuestCount,
                i.IssuedAt,
                i.Subtotal,
                i.DiscountAmount,
                i.Total,
                p.Method,
                i.Status
            FROM dbo.Invoices i
            JOIN dbo.Payments p ON p.InvoiceId = i.Id
            WHERE i.BusinessDate = @day
            ORDER BY i.IssuedAt DESC, i.Id DESC;
            """, connection);

        command.Parameters.Add("@day", SqlDbType.Date)
            .Value = day.ToDateTime(TimeOnly.MinValue);

        await connection.OpenAsync(cancellationToken);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            invoices.Add(new InvoiceRow(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                VietnamTime.FromUtc(reader.GetDateTime(4)),
                reader.GetDecimal(5),
                reader.GetDecimal(6),
                reader.GetDecimal(7),
                reader.GetString(8),
                reader.GetString(9)));
        }

        return View(new InvoicesViewModel(day, invoices));
    }

    [HttpGet("Shift")]
    public async Task<IActionResult> Shift(
        CancellationToken cancellationToken)
    {
        Response.Headers["Cache-Control"] = "no-store";

        await using var connection =
            new SqlConnection(ConnectionString);

        await connection.OpenAsync(cancellationToken);

        var open = await ReadShift(
            connection, "Status='Open'", cancellationToken);

        var closed = await ReadShift(
            connection, "Status='Closed'", cancellationToken);

        var blockers = new List<ShiftBlocker>();

        if (open is not null)
        {
            await using var command = new SqlCommand("""
                SELECT
                    s.Id,
                    COALESCE(
                        (
                            SELECT STRING_AGG(
                                CONVERT(nvarchar(max), t.Code), N', ')
                            FROM dbo.SessionTables st
                            JOIN dbo.DiningTables t ON t.Id = st.TableId
                            WHERE st.SessionId = s.Id
                              AND st.ReleasedAt IS NULL
                        ),
                        N'Chưa xác định bàn'
                    ) AS Tables,
                    s.Status
                FROM dbo.DiningSessions s
                WHERE s.ShiftId = @ShiftId
                  AND s.Status <> 'Closed'
                ORDER BY s.Id;
                """, connection);

            command.Parameters.Add(
                "@ShiftId", SqlDbType.BigInt).Value = open.Id;

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var status = reader.GetString(2);

                blockers.Add(new ShiftBlocker(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    status,
                    null,
                    status == "AwaitingPayment"
                        ? "Bàn đang chờ thanh toán."
                        : "Phiên phục vụ chưa kết thúc."));
            }
        }

        return View(new ShiftViewModel(open, closed, blockers.Count)
        {
            Blockers = blockers
        });
    }

    [HttpPost("OpenShift")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OpenShift(
        string? name,
        decimal? openingCash)
    {
        if (!ModelState.IsValid
            || openingCash is null
            || !IsValidMoney(openingCash.Value))
        {
            TempData["Error"] =
                "Tiền đầu ca phải là số nguyên không âm hợp lệ.";

            return RedirectToAction(nameof(Shift));
        }

        if (name?.Trim().Length > 100)
        {
            TempData["Error"] =
                "Tên ca không được vượt quá 100 ký tự.";

            return RedirectToAction(nameof(Shift));
        }

        return await Run(
            "dbo.usp_OpenShift",
            command =>
            {
                command.Parameters.Add(
                    "@Name", SqlDbType.NVarChar, 100).Value =
                    string.IsNullOrWhiteSpace(name)
                        ? "Ca " + VietnamTime.FromUtc(DateTime.UtcNow)
                            .ToString("dd/MM HH:mm")
                        : name.Trim();

                AddMoney(command, "@OpeningCash", openingCash.Value);
            },
            "Đã mở ca.");
    }

    [HttpPost("CloseShift")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CloseShift(
        long shiftId,
        decimal? countedCash,
        string? explanation,
        int? expectedInvoiceCount,
        decimal? expectedCashRevenue,
        decimal? expectedTransferRevenue,
        decimal? expectedDiscountTotal,
        int? expectedServedTableCount,
        decimal? expectedOpeningCash)
    {
        if (!ModelState.IsValid
            || shiftId <= 0
            || countedCash is null
            || !IsValidMoney(countedCash.Value))
        {
            TempData["Error"] =
                "Thông tin chốt ca hoặc tiền kiểm đếm không hợp lệ. "
                + "Vui lòng kiểm tra và nhập lại.";

            return RedirectToAction(nameof(Shift));
        }

        if (expectedInvoiceCount is null
            || expectedInvoiceCount.Value < 0
            || expectedServedTableCount is null
            || expectedServedTableCount.Value < 0
            || expectedCashRevenue is null
            || !IsValidMoney(expectedCashRevenue.Value)
            || expectedTransferRevenue is null
            || !IsValidMoney(expectedTransferRevenue.Value)
            || expectedDiscountTotal is null
            || !IsValidMoney(expectedDiscountTotal.Value)
            || expectedOpeningCash is null
            || !IsValidMoney(expectedOpeningCash.Value))
        {
            TempData["Error"] =
                "Thiếu số liệu đối chiếu của ca. "
                + "Vui lòng tải lại màn hình rồi thực hiện chốt ca.";

            return RedirectToAction(nameof(Shift));
        }

        explanation = explanation?.Trim();

        if (explanation?.Length > 500)
        {
            TempData["Error"] =
                "Giải trình không được vượt quá 500 ký tự.";

            return RedirectToAction(nameof(Shift));
        }

        return await Run(
            "dbo.usp_CloseShiftReviewed",
            command =>
            {
                command.Parameters.Add(
                    "@ShiftId", SqlDbType.BigInt).Value = shiftId;

                AddMoney(command, "@CountedCash", countedCash.Value);

                command.Parameters.Add(
                    "@ExpectedInvoiceCount", SqlDbType.Int)
                    .Value = expectedInvoiceCount.Value;

                AddMoney(
                    command,
                    "@ExpectedCashRevenue",
                    expectedCashRevenue.Value);

                AddMoney(
                    command,
                    "@ExpectedTransferRevenue",
                    expectedTransferRevenue.Value);

                AddMoney(
                    command,
                    "@ExpectedDiscountTotal",
                    expectedDiscountTotal.Value);

                command.Parameters.Add(
                    "@ExpectedServedTableCount", SqlDbType.Int)
                    .Value = expectedServedTableCount.Value;

                AddMoney(
                    command,
                    "@ExpectedOpeningCash",
                    expectedOpeningCash.Value);

                command.Parameters.Add(
                    "@Explanation", SqlDbType.NVarChar, 500).Value =
                    string.IsNullOrWhiteSpace(explanation)
                        ? (object)DBNull.Value
                        : explanation;
            },
            "Đã chốt ca thành công.");
    }

    private async Task<IActionResult> Run(
        string procedure,
        Action<SqlCommand> parameters,
        string success)
    {
        try
        {
            await using var connection =
                new SqlConnection(ConnectionString);

            await using var command =
                new SqlCommand(procedure, connection)
                {
                    CommandType = CommandType.StoredProcedure
                };

            parameters(command);

            command.Parameters.Add(
                "@ActorUserId", SqlDbType.Int).Value = User.ActorUserId();

            await connection.OpenAsync();
            await command.ExecuteNonQueryAsync();

            TempData["Success"] = success;
        }
        catch (SqlException ex)
            when (ex.Number is >= 51000 and < 51500)
        {
            TempData["Error"] = ex.Number == 51001
                ? "Tài khoản không có quyền mở/chốt ca."
                : ex.Message;
        }
        catch (SqlException ex)
            when (ex.Number is 547 or 8114 or 8115)
        {
            TempData["Error"] =
                "Số tiền hoặc dữ liệu ca không hợp lệ.";
        }

        return RedirectToAction(nameof(Shift));
    }

    private static bool IsValidMoney(decimal value)
    {
        return value >= 0
            && value <= 999999999999999999m
            && decimal.Truncate(value) == value;
    }

    private static void AddMoney(
        SqlCommand command,
        string name,
        decimal? value)
    {
        var parameter = command.Parameters.Add(name, SqlDbType.Decimal);

        parameter.Precision = 18;
        parameter.Scale = 0;
        parameter.Value = value.HasValue
            ? (object)value.Value
            : DBNull.Value;
    }

    private static async Task<ShiftSummary?> ReadShift(
        SqlConnection connection,
        string where,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand($"""
            SELECT TOP(1)
                s.Id,
                s.Name,
                s.BusinessDate,
                s.OpenedAt,
                s.OpeningCash,
                s.Status,
                CASE WHEN s.Status = 'Closed'
                    THEN COALESCE(s.InvoiceCount, 0)
                    ELSE (
                        SELECT COUNT(*)
                        FROM dbo.Invoices i
                        WHERE i.ShiftId = s.Id AND i.Status = 'Paid'
                    )
                END AS InvoiceCount,
                CASE WHEN s.Status = 'Closed'
                    THEN COALESCE(s.CashRevenue, 0)
                    ELSE (
                        SELECT COALESCE(SUM(p.Amount), 0)
                        FROM dbo.Invoices i
                        JOIN dbo.Payments p ON p.InvoiceId = i.Id
                        WHERE i.ShiftId = s.Id
                          AND i.Status = 'Paid'
                          AND p.Method = 'Cash'
                    )
                END AS CashRevenue,
                CASE WHEN s.Status = 'Closed'
                    THEN COALESCE(s.TransferRevenue, 0)
                    ELSE (
                        SELECT COALESCE(SUM(p.Amount), 0)
                        FROM dbo.Invoices i
                        JOIN dbo.Payments p ON p.InvoiceId = i.Id
                        WHERE i.ShiftId = s.Id
                          AND i.Status = 'Paid'
                          AND p.Method = 'Transfer'
                    )
                END AS TransferRevenue,
                s.ClosedAt,
                s.CountedCash,
                s.ExpectedCash,
                s.CashDifference,
                s.Explanation,
                CASE WHEN s.Status = 'Closed'
                    THEN COALESCE(s.DiscountTotal, 0)
                    ELSE (
                        SELECT COALESCE(SUM(i.DiscountAmount), 0)
                        FROM dbo.Invoices i
                        WHERE i.ShiftId = s.Id AND i.Status = 'Paid'
                    )
                END AS DiscountTotal,
                CASE WHEN s.Status = 'Closed'
                    THEN COALESCE(s.ServedTableCount, 0)
                    ELSE (
                        SELECT COUNT(*)
                        FROM dbo.SessionTables st
                        JOIN dbo.DiningSessions ds ON ds.Id = st.SessionId
                        WHERE ds.ShiftId = s.Id
                    )
                END AS ServedTableCount
            FROM dbo.Shifts s
            WHERE s.{where}
            ORDER BY COALESCE(s.ClosedAt, s.OpenedAt) DESC, s.Id DESC;
            """, connection);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        decimal? Money(int ordinal) =>
            reader.IsDBNull(ordinal)
                ? null
                : reader.GetDecimal(ordinal);

        return new ShiftSummary(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetDateTime(2),
            VietnamTime.FromUtc(reader.GetDateTime(3)),
            reader.GetDecimal(4),
            reader.GetString(5),
            reader.GetInt32(6),
            reader.GetDecimal(7),
            reader.GetDecimal(8),
            reader.IsDBNull(9)
                ? null
                : VietnamTime.FromUtc(reader.GetDateTime(9)),
            Money(10),
            Money(11),
            Money(12),
            reader.IsDBNull(13) ? null : reader.GetString(13))
        {
            DiscountTotal = reader.GetDecimal(14),
            ServedTableCount = reader.GetInt32(15)
        };
    }
}