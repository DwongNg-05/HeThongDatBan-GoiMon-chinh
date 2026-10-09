using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.SqlClient;

namespace RestaurantManagement.Web.Services;

/// <summary>Kết quả khi khách quét QR để mở trang gọi món (dbo.usp_StartQrGuestSession, migration 043).</summary>
public enum QrStartOutcome
{
    /// <summary>Bàn trống: đã tạo phiên phục vụ mới, bàn chuyển sang "Đang phục vụ".</summary>
    Started,
    /// <summary>Quét/bấm lặp ngay sau khi chính mã này vừa mở bàn: vào đúng phiên vừa tạo, không tạo phiên mới.</summary>
    Rejoined,
    /// <summary>Điện thoại đã có phiên khách còn hạn của bàn này: dùng lại.</summary>
    Resumed,
    InvalidQr,
    QrChanged,
    TableReserved,
    TableBusy,
    TableCleaning,
    /// <summary>Không còn dùng từ migration 044 (thay bằng <see cref="OutsideOpeningHours"/>); giữ để tương thích.</summary>
    NoShift,
    /// <summary>Ngoài giờ hoạt động hôm nay (dbo.OpeningHours, ngày nghỉ đặc biệt) — migration 044.</summary>
    OutsideOpeningHours,
    /// <summary>Khoá nghiệp vụ chung đang bận quá 15 giây (lỗi 51000) hoặc bàn vừa đổi trạng thái (51310).</summary>
    SystemBusy
}

public sealed record QrStartResult(QrStartOutcome Outcome, string? GuestToken, DateTime? ExpiresAtUtc)
{
    /// <summary>Khách được đưa vào trang gọi món của bàn.</summary>
    public bool OpensOrdering => Outcome is QrStartOutcome.Started or QrStartOutcome.Rejoined or QrStartOutcome.Resumed;

    /// <summary>Cần ghi cookie phiên khách mới (Resumed giữ cookie cũ).</summary>
    public bool IssuesCookie => Outcome is QrStartOutcome.Started or QrStartOutcome.Rejoined
        && GuestToken is not null && ExpiresAtUtc is not null;
}

/// <summary>Thông tin khách được xem trên trang gọi món của bàn.</summary>
public sealed record GuestOrderingContext(
    long SessionId,
    string TableCode,
    string AreaName,
    int MinCapacity,
    int MaxCapacity,
    string TableType,
    DateTime OpenedAtUtc);

/// <summary>Một dòng khách chọn trong giỏ (trang /TableOrder).</summary>
public sealed record GuestCartLine(int DishId, int Quantity, string? Notes);

/// <summary>Kết quả gửi giỏ món xuống bếp.</summary>
public sealed record GuestOrderResult(bool Succeeded, string Message);

/// <summary>Một món đã đặt của phiên (kể cả món nhân viên gọi giúp), dùng cho danh sách "Món đã đặt".</summary>
public sealed record GuestOrderedItem(
    int BatchNumber,
    DateTime SubmittedAtUtc,
    string Name,
    string Unit,
    decimal UnitPrice,
    int Quantity,
    string? Notes,
    string Status,
    bool Charged)
{
    public decimal LineTotal => UnitPrice * Quantity;

    public string StatusLabel => Status switch
    {
        "Pending" => "Chờ bếp nhận",
        "Preparing" => "Đang nấu",
        "Ready" => "Đã xong, chờ mang ra",
        "Served" => "Đã phục vụ",
        "Cancelled" => "Đã huỷ",
        _ => Status
    };
}

/// <summary>
/// S3-01 Task 1: phiên gọi món của khách mở bằng QR bàn, không cần đăng nhập.
/// Điện thoại giữ một mã ngẫu nhiên trong cookie HttpOnly; database chỉ lưu SHA-256 của mã (GuestSessions.TokenHash).
/// </summary>
public sealed class GuestTableSessionService(IConfiguration configuration)
{
    public const string CookieName = "RM.TableSession";

    private string ConnectionString => configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Chưa cấu hình ConnectionStrings:DefaultConnection.");

    public async Task<QrStartResult> StartAsync(string? publicQrToken, string? existingGuestToken, CancellationToken cancellationToken)
    {
        if (!TableQrService.IsValidPublicToken(publicQrToken))
            return new QrStartResult(QrStartOutcome.InvalidQr, null, null);

        var guestToken = CreateGuestToken();
        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand("dbo.usp_StartQrGuestSession", connection)
            { CommandType = CommandType.StoredProcedure, CommandTimeout = 30 };
        command.Parameters.Add("@QrTokenHash", SqlDbType.Binary, 32).Value = TableQrService.Hash(publicQrToken!);
        command.Parameters.Add("@GuestTokenHash", SqlDbType.Binary, 32).Value = Hash(guestToken);
        command.Parameters.Add("@ExistingGuestTokenHash", SqlDbType.Binary, 32).Value =
            IsValidGuestToken(existingGuestToken) ? Hash(existingGuestToken!) : DBNull.Value;

        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("usp_StartQrGuestSession không trả kết quả.");

            var outcome = Enum.Parse<QrStartOutcome>(reader.GetString(0));
            DateTime? expires = reader.IsDBNull(2) ? null : DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc);
            return outcome switch
            {
                QrStartOutcome.Started or QrStartOutcome.Rejoined => new QrStartResult(outcome, guestToken, expires),
                QrStartOutcome.Resumed => new QrStartResult(outcome, existingGuestToken, expires),
                _ => new QrStartResult(outcome, null, null)
            };
        }
        catch (SqlException ex) when (ex.Number is 51000 or 51310)
        {
            return new QrStartResult(QrStartOutcome.SystemBusy, null, null);
        }
        catch (SqlException ex) when (ex.Number is 2812 or 207 or 208)
        {
            // 2812: không có thủ tục, 207/208: thiếu cột/bảng — database web đang dùng chưa chạy migration 043.
            var database = new SqlConnectionStringBuilder(ConnectionString).InitialCatalog;
            throw new InvalidOperationException(
                $"Database '{database}' chưa áp dụng migration 043_S301QrGuestSession.sql / 044_S301QrOpeningHours.sql. " +
                "Chạy: dotnet run --project tools/RestaurantManagement.DbTool -- migrate (với RM_CONNECTION_STRING trỏ đúng database này), rồi chạy lại web.",
                ex);
        }
    }

    /// <summary>Phiên khách còn hiệu lực của cookie, hoặc null (chưa quét QR, hết hạn, QR đã đổi, phiên đã đóng).</summary>
    public async Task<GuestOrderingContext?> GetContextAsync(string? guestToken, CancellationToken cancellationToken)
    {
        if (!IsValidGuestToken(guestToken)) return null;

        const string sql = """
            SELECT s.Id, t.Code, a.Name, t.MinCapacity, t.MaxCapacity, t.TableType, s.OpenedAt
            FROM dbo.GuestSessions g
            JOIN dbo.TableQrCodes q ON q.Id = g.TableQrCodeId
            JOIN dbo.DiningSessions s ON s.Id = g.SessionId
            JOIN dbo.SessionTables st ON st.SessionId = s.Id AND st.TableId = q.TableId AND st.ReleasedAt IS NULL
            JOIN dbo.DiningTables t ON t.Id = q.TableId
            JOIN dbo.Areas a ON a.Id = t.AreaId
            WHERE g.TokenHash = @TokenHash AND g.RevokedAt IS NULL AND g.ExpiresAt > SYSUTCDATETIME()
              AND q.RevokedAt IS NULL AND s.Status IN ('Open', 'AwaitingPayment');
            """;
        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@TokenHash", SqlDbType.Binary, 32).Value = Hash(guestToken!);
        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        return new GuestOrderingContext(
            reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
            reader.GetInt32(3), reader.GetInt32(4), reader.GetString(5),
            DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc));
    }

    /// <summary>Số món tối đa / số lượng tối đa mỗi dòng / độ dài ghi chú — cùng giới hạn với dbo.usp_SubmitOrder.</summary>
    public const int MaxCartLines = 100, MaxQuantity = 99, MaxNotesLength = 200;

    /// <summary>
    /// Gửi giỏ món của khách xuống bếp qua dbo.usp_SubmitOrder (đường khách: @GuestTokenHash, không có nhân viên).
    /// Thủ tục kiểm tra phiên khách còn hạn, phiên phục vụ đang mở, món đang bán / không tạm hết, và chụp lại giá tại thời điểm gọi.
    /// <paramref name="requestId"/> sinh khi hiển thị trang: bấm "Đặt món" nhiều lần chỉ tạo một lượt gọi.
    /// </summary>
    public async Task<GuestOrderResult> SubmitOrderAsync(string? guestToken, long sessionId, Guid requestId,
        IReadOnlyList<GuestCartLine> lines, CancellationToken cancellationToken)
    {
        if (!IsValidGuestToken(guestToken))
            return new GuestOrderResult(false, "Phiên gọi món đã hết hạn. Vui lòng quét lại mã QR trên bàn.");
        if (requestId == Guid.Empty || lines.Count == 0 || lines.Count > MaxCartLines
            || lines.Any(l => l.DishId <= 0 || l.Quantity is < 1 or > MaxQuantity || (l.Notes?.Length ?? 0) > MaxNotesLength))
            return new GuestOrderResult(false, "Giỏ món không hợp lệ. Mỗi món từ 1 đến 99 phần, ghi chú tối đa 200 ký tự.");

        var itemsJson = System.Text.Json.JsonSerializer.Serialize(lines.Select(l => new
        {
            MenuItemId = l.DishId,
            l.Quantity,
            Notes = string.IsNullOrWhiteSpace(l.Notes) ? null : l.Notes.Trim()
        }));

        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand("dbo.usp_SubmitOrder", connection)
            { CommandType = CommandType.StoredProcedure, CommandTimeout = 30 };
        command.Parameters.Add("@SessionId", SqlDbType.BigInt).Value = sessionId;
        command.Parameters.Add("@RequestId", SqlDbType.UniqueIdentifier).Value = requestId;
        command.Parameters.Add("@ItemsJson", SqlDbType.NVarChar, -1).Value = itemsJson;
        command.Parameters.Add("@GuestTokenHash", SqlDbType.Binary, 32).Value = Hash(guestToken!);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return new GuestOrderResult(true, "Đã gửi món xuống bếp. Theo dõi trạng thái trong mục “Món đã đặt”.");
        }
        catch (SqlException ex) when (ex.Number is >= 51023 and <= 51028 or 51000)
        {
            return new GuestOrderResult(false, ex.Number switch
            {
                51023 => "Phiên gọi món đã hết hạn. Vui lòng quét lại mã QR trên bàn.",
                51025 => "Bàn đang chờ thanh toán hoặc đã đóng phiên, không gọi thêm món được. Vui lòng gọi nhân viên.",
                51028 => "Có món vừa hết hoặc ngừng bán. Vui lòng bỏ món đó khỏi giỏ rồi đặt lại.",
                51000 => "Hệ thống đang bận. Vui lòng bấm “Đặt món” lại sau ít giây.",
                _ => "Giỏ món không hợp lệ. Vui lòng kiểm tra lại số lượng và ghi chú."
            });
        }
    }

    /// <summary>Các món đã gọi của phiên, theo thứ tự lượt gọi.</summary>
    public async Task<IReadOnlyList<GuestOrderedItem>> GetOrderedItemsAsync(long sessionId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT b.BatchNumber, i.SubmittedAt, i.ItemName, i.Unit, i.UnitPrice, i.Quantity, i.Notes, i.Status,
                   CONVERT(bit, CASE WHEN i.Status <> 'Cancelled' OR i.ChargeWhenCancelled = 1 THEN 1 ELSE 0 END)
            FROM dbo.OrderBatches b
            JOIN dbo.OrderItems i ON i.BatchId = b.Id
            WHERE b.SessionId = @SessionId
            ORDER BY b.BatchNumber, i.Id;
            """;
        var items = new List<GuestOrderedItem>();
        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@SessionId", SqlDbType.BigInt).Value = sessionId;
        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            items.Add(new GuestOrderedItem(
                reader.GetInt32(0), DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc),
                reader.GetString(2), reader.GetString(3), reader.GetDecimal(4), reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetString(7), reader.GetBoolean(8)));
        return items;
    }

    /// <summary>Cookie phiên khách: chỉ máy chủ đọc được (HttpOnly), không gửi sang trang khác (SameSite=Lax), hết hạn cùng phiên.</summary>
    public static CookieOptions CreateCookieOptions(HttpRequest request, DateTime expiresAtUtc) => new()
    {
        HttpOnly = true,
        Secure = request.IsHttps,
        SameSite = SameSiteMode.Lax,
        IsEssential = true,
        Path = "/",
        Expires = new DateTimeOffset(DateTime.SpecifyKind(expiresAtUtc, DateTimeKind.Utc))
    };

    /// <summary>32 byte ngẫu nhiên, mã hoá base64url (43 ký tự).</summary>
    public static string CreateGuestToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static bool IsValidGuestToken(string? token)
        => token is { Length: 43 }
           && token.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}
