using System.Data;
using System.Net;
using Microsoft.Data.SqlClient;

namespace RestaurantManagement.Web.Models;

/// <summary>Một dòng nhật ký bảo mật (S1-05). Thời điểm lưu theo UTC.</summary>
public sealed record SecurityAuditEntry(
    long Id,
    DateTime OccurredAtUtc,
    string UserName,
    string? RoleCode,
    string? RoleName,
    string Action,
    string? Detail,
    string IpAddress)
{
    public const string LoginSucceeded = "LoginSucceeded";
    public const string LoginFailed = "LoginFailed";
    public const string PriceChanged = "PriceChanged";

    /// <summary>Giờ Việt Nam (UTC+7) để hiển thị.</summary>
    public DateTime OccurredAtVietnam => DateTime.SpecifyKind(OccurredAtUtc, DateTimeKind.Utc).AddHours(7);

    public string ActionLabel => Action switch
    {
        LoginSucceeded => "Đăng nhập thành công",
        LoginFailed => "Đăng nhập thất bại",
        PriceChanged => "Sửa giá món",
        _ => Action
    };

    public string RoleLabel => RoleName ?? RoleCode ?? "Không xác định";
}

/// <summary>Kho nhật ký bảo mật riêng: bảng dbo.SecurityAuditLogs (migration 020), chỉ thêm, không sửa/xoá.</summary>
public sealed class SecurityAuditStore(string connectionString)
{
    /// <summary>
    /// Ghi đăng nhập thành công/thất bại. Với tài khoản có thật, database tự lấy tên và vai trò theo <paramref name="userId"/>.
    /// Với định danh không khớp tài khoản nào, chỉ lưu dạng đã che bớt (xem <see cref="MaskIdentifier"/>).
    /// </summary>
    public async Task WriteLogin(int? userId, string? identifier, bool succeeded, string? ipAddress)
    {
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("dbo.usp_WriteLoginAudit", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = (object?)userId ?? DBNull.Value;
        cmd.Parameters.Add("@UserName", SqlDbType.NVarChar, 50).Value = userId is null ? MaskIdentifier(identifier) : "";
        cmd.Parameters.Add("@Succeeded", SqlDbType.Bit).Value = succeeded;
        cmd.Parameters.Add("@IpAddress", SqlDbType.VarChar, 45).Value = ClientIp.Normalize(ipAddress);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Nhật ký theo điều kiện lọc (S1-05 Task 2), mới nhất lên đầu, tối đa <see cref="SecurityAuditFilter.RowLimit"/> dòng
    /// kèm tổng số dòng khớp. Database kiểm tra lại quyền Audit.Read của người xem.
    /// </summary>
    public async Task<SecurityAuditPage> Search(int actorUserId, SecurityAuditFilter filter)
    {
        if (!filter.IsValid) return SecurityAuditPage.Empty;
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("dbo.usp_SecurityAuditList", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.Add("@ActorUserId", SqlDbType.Int).Value = actorUserId;
        cmd.Parameters.Add("@Top", SqlDbType.Int).Value = SecurityAuditFilter.RowLimit;
        cmd.Parameters.Add("@FromUtc", SqlDbType.DateTime2).Value = filter.FromUtc;
        cmd.Parameters.Add("@ToUtcExclusive", SqlDbType.DateTime2).Value = filter.ToUtcExclusive;
        cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = filter.UserId is > 0 ? (object)filter.UserId.Value : DBNull.Value;
        cmd.Parameters.Add("@UnknownAccounts", SqlDbType.Bit).Value = filter.UnknownAccounts;
        await using var reader = await cmd.ExecuteReaderAsync();
        var items = new List<SecurityAuditEntry>();
        while (await reader.ReadAsync())
        {
            items.Add(new SecurityAuditEntry(
                reader.GetInt64(0),
                reader.GetDateTime(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetString(7)));
        }
        long total = items.Count;
        if (await reader.NextResultAsync() && await reader.ReadAsync()) total = reader.GetInt64(0);
        return new SecurityAuditPage(items, total);
    }

    /// <summary>Danh sách tài khoản để chọn trong bộ lọc (kể cả tài khoản ngừng hoạt động).</summary>
    public async Task<IReadOnlyList<SecurityAuditAccount>> Accounts(int actorUserId)
    {
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("dbo.usp_SecurityAuditAccounts", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.Add("@ActorUserId", SqlDbType.Int).Value = actorUserId;
        await using var reader = await cmd.ExecuteReaderAsync();
        var accounts = new List<SecurityAuditAccount>();
        while (await reader.ReadAsync())
            accounts.Add(new SecurityAuditAccount(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3)));
        return accounts;
    }

    /// <summary>
    /// Định danh không thuộc tài khoản nào có thể là lỗi gõ hoặc mật khẩu gõ nhầm ô, nên chỉ giữ 2 ký tự đầu.
    /// Ví dụ "khach-la" → "kh*** (không tồn tại)".
    /// </summary>
    public static string MaskIdentifier(string? identifier)
    {
        var value = (identifier ?? "").Trim();
        if (value.Length == 0) return "(trống)";
        var prefix = value.Length <= 2 ? value[..1] : value[..2];
        return prefix + "*** (không tồn tại)";
    }
}

public static class ClientIp
{
    /// <summary>Địa chỉ IP của yêu cầu hiện tại; IPv4 ánh xạ trong IPv6 được đổi về IPv4.</summary>
    public static string From(HttpContext? context) => Normalize(context?.Connection.RemoteIpAddress);

    public static string Normalize(IPAddress? address)
    {
        if (address is null) return "unknown";
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        return Normalize(address.ToString());
    }

    public static string Normalize(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0) return "unknown";
        return text.Length <= 45 ? text : text[..45];
    }
}
