namespace RestaurantManagement.Web.Services;

/// <summary>Kết quả của email sau một lần thử gửi (giống dbo.usp_CompleteEmail).</summary>
public sealed record EmailAttemptOutcome(string Status, DateTime? NextAttemptAtUtc, bool IsFinal);

/// <summary>
/// S2-09 Task 2: quy tắc tự động gửi lại email đặt bàn.
/// 1 lần gửi đầu + tối đa 3 lần gửi lại (= tối đa 4 lần thử), mỗi lần gửi lại cách lần thử trước 5 phút.
/// Database là nơi quyết định (030_EmailRetry.sql: usp_CompleteEmail, usp_ClaimDueBookingEmail);
/// lớp này mô tả cùng quy tắc cho màn hình và kiểm thử.
/// </summary>
public static class EmailRetryPolicy
{
    /// <summary>Số lần gửi lại tối đa sau khi lần gửi đầu thất bại.</summary>
    public const int MaxRetries = 3;

    /// <summary>Tổng số lần thử tối đa = lần gửi đầu + 3 lần gửi lại (khớp CHECK AttemptCount BETWEEN 0 AND 4).</summary>
    public const int MaxAttempts = 1 + MaxRetries;

    /// <summary>Khoảng cách giữa các lần thử.</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(5);

    /// <summary>Loại email worker của web tự gửi lại (web biết dựng nội dung).</summary>
    public static readonly IReadOnlyList<string> RetryableMessageTypes = ["BookingReceived", "BookingCancelled"];

    /// <summary>Trạng thái sau một lần thử: thành công → Sent; thất bại còn lần thử → Pending, gửi lại sau 5 phút; hết lần thử → Failed.</summary>
    public static EmailAttemptOutcome AfterAttempt(int attemptNumber, bool succeeded, DateTime completedAtUtc)
    {
        if (attemptNumber is < 1 or > MaxAttempts) throw new ArgumentOutOfRangeException(nameof(attemptNumber));
        if (succeeded) return new("Sent", null, true);
        return attemptNumber >= MaxAttempts ? new("Failed", null, true) : new("Pending", completedAtUtc + RetryDelay, false);
    }

    /// <summary>Email ở lần thử này có được worker gửi lại không (đã thử ít nhất 1 lần, còn lần thử, đã tới giờ).</summary>
    public static bool IsDueForRetry(string status, int attemptCount, DateTime nextAttemptAtUtc, DateTime nowUtc) =>
        status == "Pending" && attemptCount is >= 1 and < MaxAttempts && nextAttemptAtUtc <= nowUtc;

    /// <summary>"Lần gửi đầu", "Lần gửi lại 1", … "Lần gửi lại 3".</summary>
    public static string AttemptLabel(int attemptNumber) => attemptNumber <= 1 ? "Lần gửi đầu" : $"Lần gửi lại {attemptNumber - 1}";
}
