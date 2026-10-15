using System.Data;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models.Cashier;
using RestaurantManagement.Web.Models.Reservations;

namespace RestaurantManagement.Web.Models.Orders;

/// <summary>S3-05 Task 2: một ca trong danh sách chọn của báo cáo huỷ món.</summary>
public sealed record ShiftOption(long Id, string Name, DateTime BusinessDate, DateTime OpenedAtUtc, DateTime? ClosedAtUtc, string Status)
{
    public string Label =>
        $"{Name} · {BusinessDate:dd/MM/yyyy} · mở {VietnamTime.FromUtc(OpenedAtUtc):HH:mm}" +
        (ClosedAtUtc is { } closed ? $", chốt {VietnamTime.FromUtc(closed):HH:mm}" : " · đang mở");
}

/// <summary>
/// S3-05 Task 2: một dòng nhật ký huỷ món trong ca. Kind = "Cancelled" (huỷ thành công, từ dbo.OrderItemEvents) hoặc
/// "Rejected" (yêu cầu bị từ chối / thất bại, từ dbo.OrderCancelRejections).
/// </summary>
public sealed record ShiftCancellationEntry(string Kind, DateTime OccurredAtUtc, long? BillSessionId, string? TableCode, int? BatchNumber,
    long? OrderItemId, string? ItemName, int? Quantity, string? Unit, decimal? LineTotal, string? ItemStatus, bool? ChargeWhenCancelled,
    string? Reason, string? Note, string? ActorName, string? ActorRole, string? Message)
{
    public bool IsCancelled => Kind == "Cancelled";
    public string KindLabel => !IsCancelled ? "Bị từ chối" : ChargeWhenCancelled == true ? OrderCancelRules.ChargedLabel : "Đã hủy";
    public string OccurredAtLabel => VietnamTime.FromUtc(OccurredAtUtc).ToString("HH:mm:ss dd/MM/yyyy");
    public string OrderLabel => BillSessionId is null
        ? "Không xác định"
        : $"Hoá đơn #{BillSessionId}" + (TableCode is null ? "" : $" · Bàn {TableCode}") + (BatchNumber is null ? "" : $" · Đợt {BatchNumber}");
    public string LineLabel => ItemName is null ? $"Dòng món #{OrderItemId?.ToString() ?? "?"} (không tồn tại)" : $"{Quantity} {Unit} {ItemName}";
    public string ActorLabel => (ActorName ?? "Không rõ") + (ActorRole is null ? "" : $" ({AppRoleLabel(ActorRole)})");
    public string ReasonLabel => (Reason is null ? "Không chọn lý do" : OrderCancelRules.Label(Reason) ?? Reason) + (Note is null ? "" : $" · Ghi chú: {Note}");

    /// <summary>Cách tính tiền của dòng: huỷ khi còn chờ bếp → không tính; huỷ sau khi bếp bắt đầu → vẫn tính.</summary>
    public string ChargeLabel => !IsCancelled
        ? "Không đổi (yêu cầu không được thực hiện)"
        : ChargeWhenCancelled == true
            ? $"Vẫn tính tiền {Vnd.Format(LineTotal ?? 0)} — huỷ sau khi bếp đã bắt đầu ({(ItemStatus == "Ready" ? "đã làm xong" : ItemStatus == "Preparing" ? "đang nấu" : "bếp đã nhận")})"
            : $"Không tính tiền (trừ {Vnd.Format(LineTotal ?? 0)}) — huỷ khi món còn chờ bếp";

    /// <summary>"free" (huỷ không tính tiền), "charged" (vẫn tính tiền), "none" (yêu cầu bị từ chối — hoá đơn không đổi).</summary>
    public string ChargeKind => !IsCancelled ? "none" : ChargeWhenCancelled == true ? "charged" : "free";

    private static string AppRoleLabel(string role) => role switch
    {
        "Manager" => "Quản lý", "Waiter" => "Phục vụ", "Kitchen" => "Bếp", "Cashier" => "Thu ngân", _ => role
    };
}

public sealed record ShiftCancellationReport(ShiftOption Shift, IReadOnlyList<ShiftCancellationEntry> Entries)
{
    public IReadOnlyList<ShiftCancellationEntry> Cancelled => Entries.Where(e => e.IsCancelled).ToArray();
    public int CancelledCount => Cancelled.Count;
    public int RejectedCount => Entries.Count - CancelledCount;
    /// <summary>Tổng tiền đã trừ khỏi hoá đơn (huỷ không tính tiền).</summary>
    public decimal FreeAmount => Cancelled.Where(e => e.ChargeWhenCancelled != true).Sum(e => e.LineTotal ?? 0);
    /// <summary>Tổng tiền món đã huỷ nhưng vẫn tính tiền.</summary>
    public decimal ChargedAmount => Cancelled.Where(e => e.ChargeWhenCancelled == true).Sum(e => e.LineTotal ?? 0);
}

/// <summary>Trang báo cáo: danh sách ca để chọn, ca đang xem, báo cáo (null nếu lỗi) và thông báo lỗi.</summary>
public sealed record ShiftCancellationPage(IReadOnlyList<ShiftOption> Shifts, long? SelectedShiftId, ShiftCancellationReport? Report, string? Error);

public sealed class ShiftCancellationStore(string connectionString)
{
    /// <summary>30 ca gần nhất (ca đang mở lên đầu).</summary>
    public async Task<IReadOnlyList<ShiftOption>> ShiftsAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await using var command = new SqlCommand("""
            SELECT TOP(30) Id, Name, BusinessDate, OpenedAt, ClosedAt, Status FROM dbo.Shifts
            ORDER BY CASE WHEN Status = 'Open' THEN 0 ELSE 1 END, OpenedAt DESC, Id DESC;
            """, connection);
        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var shifts = new List<ShiftOption>();
        while (await reader.ReadAsync(cancellationToken))
            shifts.Add(ReadShift(reader));
        return shifts;
    }

    /// <summary>Báo cáo huỷ món của ca (dbo.usp_ShiftCancellationReport, kiểm tra quyền Audit.Read trong database).</summary>
    public async Task<ShiftCancellationReport> ReportAsync(long shiftId, int actorUserId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await using var command = new SqlCommand("dbo.usp_ShiftCancellationReport", connection) { CommandType = CommandType.StoredProcedure };
        command.Parameters.Add("@ShiftId", SqlDbType.BigInt).Value = shiftId;
        command.Parameters.Add("@ActorUserId", SqlDbType.Int).Value = actorUserId;
        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Không đọc được thông tin ca.");
        var shift = ReadShift(reader);
        await reader.NextResultAsync(cancellationToken);
        var entries = new List<ShiftCancellationEntry>();
        string? Text(int i) => reader.IsDBNull(i) ? null : reader.GetString(i);
        while (await reader.ReadAsync(cancellationToken))
            entries.Add(new ShiftCancellationEntry(reader.GetString(0), Utc(reader.GetDateTime(1)),
                reader.IsDBNull(2) ? null : reader.GetInt64(2), Text(3), reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetInt64(5), Text(6), reader.IsDBNull(7) ? null : reader.GetInt32(7), Text(8),
                reader.IsDBNull(9) ? null : reader.GetDecimal(9), Text(10), reader.IsDBNull(11) ? null : reader.GetBoolean(11),
                Text(12), Text(13), Text(14), Text(15), Text(16)));
        return new ShiftCancellationReport(shift, entries);
    }

    /// <summary>S3-05 Task 2: ghi một yêu cầu huỷ bị từ chối / thất bại vào nhật ký (không làm hỏng thao tác chính nếu ghi lỗi).</summary>
    public async Task LogRejectionAsync(long? orderItemId, long? sessionId, int actorUserId, string? reason, int errorNumber, string message)
    {
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await using var command = new SqlCommand("dbo.usp_LogOrderCancelRejection", connection) { CommandType = CommandType.StoredProcedure };
            command.Parameters.Add("@OrderItemId", SqlDbType.BigInt).Value = (object?)orderItemId ?? DBNull.Value;
            command.Parameters.Add("@SessionId", SqlDbType.BigInt).Value = (object?)sessionId ?? DBNull.Value;
            command.Parameters.Add("@ActorUserId", SqlDbType.Int).Value = actorUserId;
            var trimmed = reason?.Trim();
            command.Parameters.Add("@Reason", SqlDbType.NVarChar, 60).Value = string.IsNullOrEmpty(trimmed)
                ? DBNull.Value
                : (object)(trimmed.Length > 60 ? trimmed[..60] : trimmed);
            command.Parameters.Add("@ErrorNumber", SqlDbType.Int).Value = errorNumber;
            command.Parameters.Add("@Message", SqlDbType.NVarChar, 400).Value = message.Length > 400 ? message[..400] : message;
            await connection.OpenAsync();
            await command.ExecuteNonQueryAsync();
        }
        catch (SqlException)
        {
            // Nhật ký là phụ: database chưa có migration 057 hoặc đang bận thì vẫn trả kết quả từ chối cho nhân viên.
        }
    }

    private static ShiftOption ReadShift(SqlDataReader reader) => new(reader.GetInt64(0), reader.GetString(1), reader.GetDateTime(2),
        Utc(reader.GetDateTime(3)), reader.IsDBNull(4) ? null : Utc(reader.GetDateTime(4)), reader.GetString(5));

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
