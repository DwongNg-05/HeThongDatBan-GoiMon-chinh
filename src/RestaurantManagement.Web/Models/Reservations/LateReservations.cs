using System.Data;
using Microsoft.Data.SqlClient;

namespace RestaurantManagement.Web.Models.Reservations;

/// <summary>
/// S3-09 Task 1: một lượt đặt bàn đang có cảnh báo trễ (giờ hẹn + 15 phút mà khách chưa tới).
/// Quy tắc nằm trong database (dbo.fn_LateReservationAlerts, migration 049) để sơ đồ bàn, danh sách đặt bàn và thao tác
/// "Đánh dấu khách không tới" dùng chung một định nghĩa.
/// </summary>
/// <param name="ExtensionCount">S3-09 Task 2: số lần đã gia hạn giữ bàn (0 hoặc 1).</param>
/// <param name="HoldExtendedUntilUtc">S3-09 Task 2: thời hạn đã gia hạn (đã hết, vì lượt đang bị cảnh báo lại).</param>
public sealed record LateReservationAlert(long Id, string Code, string CustomerName, string Phone, int GuestCount,
    string? TableCode, DateTime StartsAtUtc, DateTime WarnAtUtc, int LateMinutes, string Status,
    int ExtensionCount = 0, DateTime? HoldExtendedUntilUtc = null)
{
    public string StatusLabel => DailyReservationRules.StatusLabel(Status);
    public string LateLabel => LateReservationRules.LateLabel(LateMinutes);
    public string AppointmentTime => VietnamTime.FromUtc(StartsAtUtc).ToString("HH:mm");
    /// <summary>S3-09 Task 2: chỉ gia hạn được một lần; sau đó chỉ còn "Đánh dấu khách không tới".</summary>
    public bool CanExtend => ExtensionCount < LateReservationRules.MaxHoldExtensions;
    /// <summary>S3-09 Task 2: lượt đã gia hạn và hết hạn mà khách chưa tới → "Đã gia hạn 1 lần, hết hạn lúc 18:45".</summary>
    public string? ExtensionLabel => LateReservationRules.ExpiredExtensionLabel(ExtensionCount, HoldExtendedUntilUtc);
}

/// <summary>
/// S3-09 Task 2: lượt đang trong thời gian gia hạn giữ bàn (đã gia hạn, chưa hết hạn, khách chưa tới).
/// Không cảnh báo; sơ đồ bàn và danh sách đặt bàn hiển thị thời hạn mới. Hết hạn → lượt trở lại danh sách cảnh báo.
/// </summary>
public sealed record ExtendedReservationHold(long Id, string Code, string CustomerName, string Phone, int GuestCount,
    string? TableCode, DateTime StartsAtUtc, DateTime HoldUntilUtc, string Status)
{
    public string StatusLabel => DailyReservationRules.StatusLabel(Status);
    public string AppointmentTime => VietnamTime.FromUtc(StartsAtUtc).ToString("HH:mm");
    public string HoldUntilTime => VietnamTime.FromUtc(HoldUntilUtc).ToString("HH:mm");
    public string HoldLabel => LateReservationRules.HoldLabel(HoldUntilUtc);
}

/// <summary>
/// Các cảnh báo hiện tại, các lượt đang gia hạn giữ bàn (S3-09 Task 2) và mốc cảnh báo kế tiếp — gồm cả lúc một lượt
/// gia hạn hết hạn — để màn hình tự cập nhật đúng lúc, không chờ chu kỳ tải lại.
/// </summary>
public sealed record LateReservationSnapshot(DateTime EvaluatedAtUtc, IReadOnlyList<LateReservationAlert> Alerts, DateTime? NextWarnAtUtc)
{
    public IReadOnlyList<ExtendedReservationHold> Holds { get; init; } = Array.Empty<ExtendedReservationHold>();
}

/// <summary>Kết quả "Đánh dấu khách không tới". AlreadyMarked: lần gửi lặp hoặc nhân viên khác đã đánh dấu trước — không ghi thêm.</summary>
public sealed record NoShowMarkResult(bool AlreadyMarked, string ReservationCode, string? TableCode, string? TableStatus,
    bool TableReleased, int PhoneNoShowCount)
{
    public string Message => LateReservationRules.MarkedMessage(this);
}

/// <summary>S3-09 Task 2: kết quả "Gia hạn 15 phút".</summary>
public sealed record HoldExtensionResult(string ReservationCode, string? TableCode, DateTime HoldExtendedUntilUtc, int ExtensionCount)
{
    public string HoldUntilTime => VietnamTime.FromUtc(HoldExtendedUntilUtc).ToString("HH:mm");
    public string Message => LateReservationRules.ExtendedMessage(this);
}

/// <summary>
/// Thao tác bị từ chối (khách đã tới, lượt đã huỷ, chưa tới mốc cảnh báo, đã gia hạn một lần, không tồn tại, hệ thống bận).
/// Dùng chung cho "Đánh dấu khách không tới" và "Gia hạn 15 phút".
/// </summary>
public sealed class NoShowRejectedException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public static class LateReservationRules
{
    /// <summary>Mặc định của RestaurantSettings.NoShowGraceMinutes: cảnh báo từ đúng mốc giờ hẹn + 15 phút.</summary>
    public const int DefaultGraceMinutes = 15;

    /// <summary>S3-09 Task 2: mỗi lần gia hạn giữ bàn thêm 15 phút (tính từ lúc nhân viên bấm — cần PO chốt), tối đa một lần.</summary>
    public const int HoldExtensionMinutes = 15;
    public const int MaxHoldExtensions = 1;

    public static string HoldLabel(DateTime holdUntilUtc) => $"Gia hạn giữ bàn tới {VietnamTime.FromUtc(holdUntilUtc):HH:mm}";

    public static string? ExpiredExtensionLabel(int extensionCount, DateTime? holdUntilUtc) => extensionCount < MaxHoldExtensions
        ? null
        : holdUntilUtc is { } until ? $"Đã gia hạn 1 lần, hết hạn lúc {VietnamTime.FromUtc(until):HH:mm}" : "Đã gia hạn 1 lần";

    public static string ExtendedMessage(HoldExtensionResult result)
    {
        var table = result.TableCode is null ? "chỗ" : $"bàn {result.TableCode}";
        return $"Đã gia hạn giữ {table} cho lượt {result.ReservationCode} tới {result.HoldUntilTime}. " +
               "Hết hạn mà khách chưa tới, cảnh báo sẽ hiện lại và chỉ còn thao tác đánh dấu khách không tới.";
    }

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
        51121 or 51122 or 51123 or 51124 or 51125 => new(StatusCodesConflict, ex.Message),
        51000 => new(StatusCodesUnavailable, "Hệ thống đang bận. Vui lòng bấm lại sau ít giây."),
        // Thủ tục / hàm chưa có trong database: chưa chạy migration 049, 054.
        2812 or 208 => new(StatusCodesUnavailable, DatabaseNotUpdated),
        51001 => new(StatusCodesForbidden, "Tài khoản không có quyền xử lý đặt bàn."),
        _ => null
    };

    public const string DatabaseNotUpdated =
        "Database chưa được cập nhật cho chức năng này. Hãy chạy: dotnet run --project tools/RestaurantManagement.DbTool -- migrate, rồi tải lại trang.";

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
        // Đọc thẳng từ dbo.Reservations theo đúng quy tắc của dbo.fn_ReservationWarnAt / fn_LateReservationAlerts /
        // fn_ExtendedReservationHolds (migration 049, 054), để cảnh báo vẫn hiện khi database chưa chạy kịp migration mới nhất.
        // Mốc cảnh báo = HoldExtendedUntil (đã gia hạn) hoặc giờ hẹn + RestaurantSettings.NoShowGraceMinutes; chỉ lượt
        // Chờ xác nhận / Đã xác nhận chưa tới, giờ hẹn trong 12 giờ gần nhất.
        await using var command = new SqlCommand("""
            DECLARE @n datetime2(3) = COALESCE(@now, SYSUTCDATETIME());
            DECLARE @grace int = COALESCE((SELECT NoShowGraceMinutes FROM dbo.RestaurantSettings WHERE Id = 1), 15);
            DECLARE @w TABLE(Id bigint PRIMARY KEY, Code varchar(10), CustomerName nvarchar(100), Phone varchar(20), GuestCount int,
                             TableCode varchar(20) NULL, StartsAt datetime2(3), Status varchar(20), ExtensionCount int,
                             HoldExtendedUntil datetime2(3) NULL, WarnAt datetime2(3));
            INSERT @w
            SELECT r.Id, r.Code, r.CustomerName, r.Phone, r.GuestCount, t.Code, r.StartsAt, r.Status, r.ExtensionCount,
                   r.HoldExtendedUntil, COALESCE(r.HoldExtendedUntil, DATEADD(minute, @grace, r.StartsAt))
            FROM dbo.Reservations r
            LEFT JOIN dbo.DiningTables t ON t.Id = r.TableId
            WHERE r.Status IN ('Pending', 'Confirmed') AND r.ArrivedAt IS NULL AND r.StartsAt > DATEADD(hour, -12, @n);
            SELECT @n;
            SELECT Id, Code, CustomerName, Phone, GuestCount, TableCode, StartsAt, WarnAt, DATEDIFF(second, StartsAt, @n) / 60,
                   Status, ExtensionCount, HoldExtendedUntil
            FROM @w WHERE WarnAt <= @n
            ORDER BY WarnAt, Id;
            SELECT MIN(WarnAt) FROM @w WHERE WarnAt > @n;
            -- S3-09 Task 2: lượt đang gia hạn giữ bàn.
            SELECT Id, Code, CustomerName, Phone, GuestCount, TableCode, StartsAt, HoldExtendedUntil, Status
            FROM @w WHERE ExtensionCount >= 1 AND HoldExtendedUntil > @n
            ORDER BY HoldExtendedUntil, Id;
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
                Utc(reader.GetDateTime(6)), Utc(reader.GetDateTime(7)), reader.GetInt32(8), reader.GetString(9),
                Convert.ToInt32(reader.GetValue(10)), reader.IsDBNull(11) ? null : Utc(reader.GetDateTime(11))));
        await reader.NextResultAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        DateTime? next = reader.IsDBNull(0) ? null : Utc(reader.GetDateTime(0));
        await reader.NextResultAsync(cancellationToken);
        var holds = new List<ExtendedReservationHold>();
        while (await reader.ReadAsync(cancellationToken))
            holds.Add(new(reader.GetInt64(0), reader.GetString(1).TrimEnd(), reader.GetString(2),
                DailyReservationRules.MaskPhone(reader.GetString(3)), reader.GetInt32(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                Utc(reader.GetDateTime(6)), Utc(reader.GetDateTime(7)), reader.GetString(8)));
        return new(evaluatedAt, alerts, next) { Holds = holds };
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

    /// <summary>
    /// S3-09 Task 2: gia hạn giữ bàn 15 phút (một giao dịch: kiểm tra trạng thái + số lần gia hạn, lưu thời hạn mới, ghi sự kiện).
    /// Lần gia hạn thứ hai (kể cả gửi lặp hoặc hai nhân viên cùng bấm) bị từ chối.
    /// </summary>
    /// <exception cref="NoShowRejectedException">Thao tác bị từ chối, kèm mã HTTP và lý do.</exception>
    public async Task<HoldExtensionResult> ExtendHoldAsync(long reservationId, int actorUserId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await using var command = new SqlCommand("dbo.usp_ExtendLateReservationHold", connection)
            { CommandType = CommandType.StoredProcedure, CommandTimeout = 30 };
        command.Parameters.Add("@ReservationId", SqlDbType.BigInt).Value = reservationId;
        command.Parameters.Add("@ActorUserId", SqlDbType.Int).Value = actorUserId;
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("usp_ExtendLateReservationHold không trả kết quả.");
            return new(reader.GetString(1).TrimEnd(), reader.IsDBNull(2) ? null : reader.GetString(2),
                Utc(reader.GetDateTime(3)), reader.GetInt32(4));
        }
        catch (SqlException ex) when (LateReservationRules.Rejection(ex) is { } rejection)
        {
            throw rejection;
        }
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
