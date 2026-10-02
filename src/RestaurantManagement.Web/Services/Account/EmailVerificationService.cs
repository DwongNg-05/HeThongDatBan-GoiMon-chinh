using System.Net.Mail;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using RestaurantManagement.Web.Models;

namespace RestaurantManagement.Web.Services.EmailVerification;

public sealed record SendResult(bool Sent, string? Error, int RetryAfterSeconds);

/// <summary>Quy tắc xác minh đăng nhập bằng email: ai phải xác minh, gửi/gửi lại mã, kiểm tra mã.</summary>
public sealed class EmailVerificationService(EmailVerificationStore store, IEmailSender sender,
    IOptions<EmailVerificationOptions> options, ILogger<EmailVerificationService> logger)
{
    /// <summary>Claim ghi mã phiên đã xác minh; gắn với phiên nên đăng nhập lại phải xác minh lại.</summary>
    public const string VerifiedClaim = "EmailVerifiedSession";
    public const string ManagerRole = "Manager";

    public EmailVerificationOptions Options => options.Value;

    /// <summary>Mọi người dùng trừ Quản lý, mỗi phiên đăng nhập một lần.</summary>
    public bool IsRequired(ClaimsPrincipal user)
    {
        if (!options.Value.Enabled || user.Identity?.IsAuthenticated != true || user.IsInRole(ManagerRole)) return false;
        var session = user.FindFirstValue(LoginSessionStore.SessionClaim);
        return session is null || user.FindFirstValue(VerifiedClaim) != session;
    }

    public static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254) return false;
        try { return new MailAddress(email.Trim()).Address.Equals(email.Trim(), StringComparison.OrdinalIgnoreCase) && email.Contains('.'); }
        catch (FormatException) { return false; }
    }

    public Task<EmailVerificationState?> State(int userId, Guid sessionId) => store.State(userId, sessionId);

    public async Task<SendResult> SendCode(int userId, Guid sessionId, string email, string fullName, CancellationToken ct = default)
    {
        var code = VerificationCode.Generate();
        var (status, retry) = await store.Issue(userId, sessionId, email.Trim(), VerificationCode.Hash(code, userId, sessionId), options.Value);
        switch (status)
        {
            case IssueStatus.Cooldown:
                return new(false, $"Vui lòng chờ {retry} giây rồi bấm “Gửi lại mã”.", retry);
            case IssueStatus.TooMany:
                return new(false, $"Bạn đã yêu cầu gửi mã quá nhiều lần. Vui lòng thử lại sau {Math.Max(1, (retry + 59) / 60)} phút.", retry);
            case IssueStatus.InvalidSession:
                return new(false, "Phiên đăng nhập không còn hợp lệ. Vui lòng đăng nhập lại.", 0);
        }
        try
        {
            await sender.SendAsync(VerificationEmail.Create(email.Trim(), fullName, code, options.Value.ValidMinutes, DateTime.UtcNow), ct);
            return new(true, null, options.Value.ResendCooldownSeconds);
        }
        catch (Exception ex) when (ex is SmtpException or InvalidOperationException or IOException or FormatException)
        {
            logger.LogError(ex, "Không gửi được email xác minh cho tài khoản {UserId}.", userId);
            return new(false, "Không gửi được email. Vui lòng kiểm tra lại địa chỉ email hoặc thử “Gửi lại mã” sau ít phút.", options.Value.ResendCooldownSeconds);
        }
    }

    public Task<(VerifyStatus Status, int RemainingAttempts)> Verify(int userId, Guid sessionId, string code) =>
        store.Verify(userId, sessionId, VerificationCode.Hash(code, userId, sessionId), options.Value.MaxAttempts);
}
