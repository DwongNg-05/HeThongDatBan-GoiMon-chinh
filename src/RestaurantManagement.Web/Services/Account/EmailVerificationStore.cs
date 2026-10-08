using System.Data;
using Microsoft.Data.SqlClient;

namespace RestaurantManagement.Web.Services.EmailVerification;

/// <param name="Email">Email đã xác minh của tài khoản (null nếu chưa có).</param>
/// <param name="PendingEmail">Email nhận mã gần nhất trong phiên này (khi tài khoản chưa có email).</param>
public sealed record EmailVerificationState(string? Email, bool Verified, DateTime? LastSentAtUtc, DateTime? ActiveExpiresAtUtc, string? PendingEmail)
{
    public string? TargetEmail => Email ?? PendingEmail;
}

public enum IssueStatus { Issued = 0, Cooldown = 1, TooMany = 2, InvalidSession = 3 }
public enum VerifyStatus { Verified = 0, Wrong = 1, ExpiredOrMissing = 2, TooManyAttempts = 3 }

/// <summary>Đọc/ghi mã xác minh qua stored procedure (migration 023). Database chỉ lưu SHA-256 của mã.</summary>
public sealed class EmailVerificationStore(string connectionString)
{
    public async Task<EmailVerificationState?> State(int userId, Guid sessionId)
    {
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync();
        await using var cmd = Proc(cn, "dbo.usp_EmailVerificationState");
        cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
        cmd.Parameters.Add("@SessionId", SqlDbType.UniqueIdentifier).Value = sessionId;
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new(r.IsDBNull(0) ? null : r.GetString(0), r.GetBoolean(1),
            r.IsDBNull(2) ? null : r.GetDateTime(2), r.IsDBNull(3) ? null : r.GetDateTime(3), r.IsDBNull(4) ? null : r.GetString(4));
    }

    public async Task<(IssueStatus Status, int RetryAfterSeconds)> Issue(int userId, Guid sessionId, string email, byte[] codeHash, EmailVerificationOptions o)
    {
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync();
        await using var cmd = Proc(cn, "dbo.usp_IssueEmailVerificationCode");
        cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
        cmd.Parameters.Add("@SessionId", SqlDbType.UniqueIdentifier).Value = sessionId;
        cmd.Parameters.Add("@Email", SqlDbType.NVarChar, 254).Value = email;
        cmd.Parameters.Add("@CodeHash", SqlDbType.Binary, 32).Value = codeHash;
        cmd.Parameters.Add("@ValidMinutes", SqlDbType.Int).Value = o.ValidMinutes;
        cmd.Parameters.Add("@CooldownSeconds", SqlDbType.Int).Value = o.ResendCooldownSeconds;
        cmd.Parameters.Add("@MaxPerWindow", SqlDbType.Int).Value = o.MaxSendsPer15Minutes;
        await using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();
        return ((IssueStatus)r.GetInt32(0), Math.Max(0, r.GetInt32(1)));
    }

    public async Task<(VerifyStatus Status, int RemainingAttempts)> Verify(int userId, Guid sessionId, byte[] codeHash, int maxAttempts)
    {
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync();
        await using var cmd = Proc(cn, "dbo.usp_VerifyEmailCode");
        cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
        cmd.Parameters.Add("@SessionId", SqlDbType.UniqueIdentifier).Value = sessionId;
        cmd.Parameters.Add("@CodeHash", SqlDbType.Binary, 32).Value = codeHash;
        cmd.Parameters.Add("@MaxAttempts", SqlDbType.Int).Value = maxAttempts;
        await using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();
        return ((VerifyStatus)r.GetInt32(0), Math.Max(0, r.GetInt32(1)));
    }

    private static SqlCommand Proc(SqlConnection cn, string name) => new(name, cn) { CommandType = CommandType.StoredProcedure };
}
