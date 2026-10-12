using System.Data;
using Microsoft.Data.SqlClient;

namespace RestaurantManagement.Web.Models.Reservations;

/// <summary>
/// S3-09 Task 1: một lượt đặt bàn đang có cảnh báo trễ (giờ hẹn + 15 phút mà khách chưa tới).
/// Quy tắc nằm trong database (dbo.fn_LateReservationAlerts, migration 049) để sơ đồ bàn, danh sách đặt bàn và thao tác
/// "Đánh dấu khách không tới" dùng chung một định nghĩa.
/// </summary>
public sealed record LateReservationAlert(long Id, string Code, string CustomerName, string Phone, int GuestCount,
    string? TableCode, DateTime StartsAtUtc, DateTime WarnAtUtc, int LateMinutes, string Status)
{
    public string StatusLabel => DailyReservationRules.StatusLabel(Status);
    public string LateLabel => LateReservationRules.LateLabel(LateMinutes);
    public string AppointmentTime => VietnamTime.FromUtc(StartsAtUtc).ToString("HH:mm");
}

/// <summary>Các cảnh báo hiện tại và mốc cảnh báo kế tiếp (để màn hình tự cập nhật đúng lúc đạt ngưỡng, không chờ chu kỳ tải lại).</summary>
public sealed record LateReservationSnapshot(DateTime EvaluatedAtUtc, IReadOnlyList<LateReservationAlert> Alerts, DateTime? NextWarnAtUtc);

/// <summary>Kết quả "Đánh dấu khách không tới". AlreadyMarked: lần gửi lặp hoặc nhân viên khác đã đánh dấu trước — không ghi thêm.</summary>
public sealed record NoShowMarkResult(bool AlreadyMarked, string ReservationCode, string? TableCode, string? TableStatus,
    bool TableReleased, int PhoneNoShowCount)
{
    public string Message => LateReservationRules.MarkedMessage(this);
}

/// <summary>Thao tác bị từ chối (khách đã tới, lượt đã huỷ, chưa tới mốc cảnh báo, không tồn tại, hệ thống bận).</summary>
public sealed class NoShowRejectedException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public static class LateReservationRules
{
    /// <summary>Mặc định của RestaurantSettings.NoShowGraceMinutes: cảnh báo từ đúng mốc giờ hẹn + 15 phút.</summary>
    public const int DefaultGraceMinutes = 15;

    public static string LateLabel(int lateMinutes) =>
        lateMinutes >= 60 ? $"Trễ {lateMinutes / 60} giờ {lateMinutes % 60:00} phút" : $"Trễ {lateMinutes} phút";

    public static string MarkedMessage(NoShowMarkResult result)
    {
        var history = $"Số điện thoại này đã có {result.PhoneNoShowCount} lần không tới.";
        if (result.AlreadyMarked)
            return $"Lượt {result.ReservationCode} đã được đánh dấu khách không tới trước đó, không ghi thêm. {history}";
        var table = result.TableCode is null
            ? "Lượt này chưa xếp bàn."
            : result.TableReleased || result.TableStatus == "Available"
                ? $"Bàn {result.TableCode} đã trống."
                : $"Bàn {result.TableCode} không đổi trạng thái vì đang có khách hoặc lượt đặt khác.";
        return $"Đã đánh dấu khách không tới cho lượt {result.ReservationCode}. {table} {history}";
    }

    /// <summary>Lỗi do dbo.usp_MarkLateReservationNoShow trả về → mã HTTP + thông báo tiếng Việt cho nhân viên.</summary>
    public static NoShowRejectedException? Rejection(SqlException ex) => ex.Number switch
    {
        51120 => new(StatusCodesNotFound, ex.Message),
        51121 or 51122 or 51123 => new(StatusCodesConflict, ex.Message),
        51000 => new(StatusCodesUnavailable, "Hệ thống đang bận. Vui lòng bấm lại sau ít giây."),
        51001 => new(StatusCodesForbidden, "Tài khoản không có quyền xử lý đặt bàn."),
        _ => null
    };

    private const int StatusCodesNotFound = 404, StatusCodesConflict = 409, StatusCodesUnavailable = 503, StatusCodesForbidden = 403;
}

public sealed class LateReservationStore(string connectionString)
{
    /// <summary>
    /// Cảnh báo tại thời điểm <paramref name="utcNow"/> (null = giờ máy chủ database, cùng đồng hồ với thao tác đánh dấu).
    /// Chỉ trả số điện thoại đã che (090****123) như danh sách đặt bàn.
    /// </summary>
    public async Task<LateReservationSnapshot> ReadAsync(CancellationToken cancellationToken, DateTime? utcNow = null)
    {
        await using var connection = new SqlConnection(connectionString);
        await using var command = new SqlCommand("""
            DECLARE @n datetime2(3) = COALESCE(@now, SYSUTCDATETIME());
            SELECT @n;
            SELECT Id, Code, CustomerName, Phone, GuestCount, TableCode, StartsAt, WarnAt, LateMinutes, Status
            FROM dbo.fn_LateReservationAlerts(@n)
            ORDER BY WarnAt, Id;
            SELECT MIN(WarnAt) FROM dbo.fn_ReservationWarnAt() WHERE WarnAt > @n AND StartsAt > DATEADD(hour, -12, @n);
            """, connection);
        command.Parameters.Add("@now", SqlDbType.DateTime2).Value = utcNow is null ? DBNull.Value : utcNow.Value;
        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var evaluatedAt = Utc(reader.GetDateTime(0));
        await reader.NextResultAsync(cancellationToken);
        var alerts = new List<LateReservationAlert>();
        while (await reader.ReadAsync(cancellationToken))
            alerts.Add(new(reader.GetInt64(0), reader.GetString(1).TrimEnd(), reader.GetString(2),
                DailyReservationRules.MaskPhone(reader.GetString(3)), reader.GetInt32(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                Utc(reader.GetDateTime(6)), Utc(reader.GetDateTime(7)), reader.GetInt32(8), reader.GetString(9)));
        await reader.NextResultAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        DateTime? next = reader.IsDBNull(0) ? null : Utc(reader.GetDateTime(0));
        return new(evaluatedAt, alerts, next);
    }

    /// <summary>Đánh dấu khách không tới (một giao dịch: trạng thái, lịch sử số điện thoại, giải phóng bàn).</summary>
    /// <exception cref="NoShowRejectedException">Thao tác bị từ chối, kèm mã HTTP và lý do.</exception>
    public async Task<NoShowMarkResult> MarkNoShowAsync(long reservationId, int actorUserId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await using var command = new SqlCommand("dbo.usp_MarkLateReservationNoShow", connection)
            { CommandType = CommandType.StoredProcedure, CommandTimeout = 30 };
        command.Parameters.Add("@ReservationId", SqlDbType.BigInt).Value = reservationId;
        command.Parameters.Add("@ActorUserId", SqlDbType.Int).Value = actorUserId;
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("usp_MarkLateReservationNoShow không trả kết quả.");
            return new(reader.GetString(0) == "AlreadyNoShow", reader.GetString(1).TrimEnd(),
                reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetBoolean(4), reader.GetInt32(5));
        }
        catch (SqlException ex) when (LateReservationRules.Rejection(ex) is { } rejection)
        {
            throw rejection;
        }
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
