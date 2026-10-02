namespace RestaurantManagement.Web.Models;

/// <summary>Tài khoản trong danh sách chọn của bộ lọc nhật ký.</summary>
public sealed record SecurityAuditAccount(int Id, string UserName, string RoleName, bool IsActive)
{
    public string Label => IsActive ? $"{UserName} — {RoleName}" : $"{UserName} — {RoleName} (ngừng hoạt động)";
}

/// <summary>Kết quả một lần truy vấn nhật ký: tối đa <see cref="SecurityAuditFilter.RowLimit"/> dòng và tổng số dòng khớp.</summary>
public sealed record SecurityAuditPage(IReadOnlyList<SecurityAuditEntry> Items, long TotalCount)
{
    public static SecurityAuditPage Empty { get; } = new([], 0);
}

public sealed record SecurityAuditViewModel(
    SecurityAuditFilter Filter,
    IReadOnlyList<SecurityAuditAccount> Accounts,
    SecurityAuditPage Page)
{
    public string AccountLabel => Filter.UserId switch
    {
        null => "Tất cả tài khoản",
        SecurityAuditFilter.UnknownAccountsValue => SecurityAuditFilter.UnknownAccountsLabel,
        var id => Accounts.FirstOrDefault(a => a.Id == id)?.UserName ?? $"#{id}"
    };
}

/// <summary>
/// S1-05 Task 2: điều kiện lọc nhật ký theo khoảng ngày (theo giờ Việt Nam, UTC+7) và tài khoản.
/// Quy tắc đề xuất chốt với PO (docs/S1-05-Task2.md):
/// - Mở màn hình không có điều kiện: 7 ngày gần nhất = hôm nay và 6 ngày trước, tính theo ngày Việt Nam.
/// - Khoảng ngày bao gồm cả hai đầu, tối đa 90 ngày; "Từ ngày" không được sau "Đến ngày".
/// - Tài khoản chọn từ danh sách có sẵn (không nhập tự do); giá trị 0 = định danh không tồn tại.
/// </summary>
public sealed class SecurityAuditFilter
{
    public const int DefaultDays = 7;
    public const int MaxDays = 90;
    public const int RowLimit = 1000;
    public const int UnknownAccountsValue = 0;
    public const string UnknownAccountsLabel = "Định danh không tồn tại";
    public static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public const string ErrorInvalidDate = "Ngày lọc không hợp lệ. Vui lòng chọn lại ngày.";
    public const string ErrorOrder = "\"Từ ngày\" phải trước hoặc bằng \"Đến ngày\".";
    public static readonly string ErrorTooLong = $"Khoảng ngày tối đa {MaxDays} ngày. Vui lòng thu hẹp khoảng lọc.";
    public const string ErrorAccount = "Tài khoản đã chọn không có trong danh sách.";

    private static readonly DateOnly MinDate = new(2000, 1, 1);
    private static readonly DateOnly MaxDate = new(9998, 12, 31);

    public DateOnly FromDate { get; private init; }
    public DateOnly ToDate { get; private init; }
    public int? UserId { get; private init; }
    public bool IsDefault { get; private init; }
    public IReadOnlyList<string> Errors { get; private init; } = [];

    public bool IsValid => Errors.Count == 0;
    public bool UnknownAccounts => UserId == UnknownAccountsValue;
    public int Days => ToDate.DayNumber - FromDate.DayNumber + 1;

    /// <summary>00:00 ngày bắt đầu theo giờ Việt Nam, đổi sang UTC (bao gồm).</summary>
    public DateTime FromUtc => DateTime.SpecifyKind(FromDate.ToDateTime(TimeOnly.MinValue) - VietnamOffset, DateTimeKind.Utc);

    /// <summary>00:00 ngày sau ngày kết thúc theo giờ Việt Nam, đổi sang UTC (không bao gồm).</summary>
    public DateTime ToUtcExclusive => DateTime.SpecifyKind(ToDate.AddDays(1).ToDateTime(TimeOnly.MinValue) - VietnamOffset, DateTimeKind.Utc);

    public static DateOnly TodayVietnam(DateTime utcNow) =>
        DateOnly.FromDateTime(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc) + VietnamOffset);

    /// <param name="invalidInput">true khi trình duyệt gửi ngày sai định dạng (lỗi model binding).</param>
    /// <param name="knownUserIds">Id các tài khoản trong danh sách chọn; null = không kiểm tra.</param>
    public static SecurityAuditFilter Resolve(DateOnly? from, DateOnly? to, int? userId, DateOnly todayVietnam,
        IReadOnlyCollection<int>? knownUserIds = null, bool invalidInput = false)
    {
        var errors = new List<string>();
        if (invalidInput) errors.Add(ErrorInvalidDate);

        var toDate = to ?? todayVietnam;
        DateOnly fromDate;
        if (toDate < MinDate || toDate > MaxDate || from is { } f && (f < MinDate || f > MaxDate))
        {
            if (!invalidInput) errors.Add(ErrorInvalidDate);
            fromDate = todayVietnam.AddDays(-(DefaultDays - 1));
            toDate = todayVietnam;
        }
        else if ((fromDate = from ?? toDate.AddDays(-(DefaultDays - 1))) > toDate) errors.Add(ErrorOrder);
        else if (toDate.DayNumber - fromDate.DayNumber + 1 > MaxDays) errors.Add(ErrorTooLong);

        if (userId is < 0 || (userId is > 0 && knownUserIds is not null && !knownUserIds.Contains(userId.Value)))
            errors.Add(ErrorAccount);

        return new SecurityAuditFilter
        {
            FromDate = fromDate,
            ToDate = toDate,
            UserId = userId,
            IsDefault = from is null && to is null && userId is null && !invalidInput,
            Errors = errors
        };
    }
}
