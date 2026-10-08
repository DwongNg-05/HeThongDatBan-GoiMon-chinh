namespace RestaurantManagement.Web.Services.EmailVerification;

/// <summary>Cấu hình xác minh đăng nhập bằng email (mục "EmailVerification" trong appsettings).</summary>
public sealed class EmailVerificationOptions
{
    public const string Section = "EmailVerification";

    /// <summary>Bật/tắt bước xác minh. Mặc định bật; chỉ tắt cho các bộ kiểm thử cũ.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Mã có hiệu lực bao nhiêu phút.</summary>
    public int ValidMinutes { get; set; } = 10;
    /// <summary>Phải chờ bao nhiêu giây giữa hai lần gửi mã.</summary>
    public int ResendCooldownSeconds { get; set; } = 60;
    /// <summary>Số lần gửi tối đa trong 15 phút cho một tài khoản.</summary>
    public int MaxSendsPer15Minutes { get; set; } = 5;
    /// <summary>Số lần nhập sai tối đa cho một mã.</summary>
    public int MaxAttempts { get; set; } = 5;
}

/// <summary>Cấu hình gửi email (mục "Email" trong appsettings / user-secrets).</summary>
public sealed class EmailOptions
{
    public const string Section = "Email";

    /// <summary>Máy chủ SMTP, ví dụ smtp.gmail.com. Để trống thì email được lưu thành tệp trong PickupDirectory.</summary>
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string? FromAddress { get; set; }
    public string FromName { get; set; } = "Bếp Nhà";
    /// <summary>Thư mục lưu email khi chưa cấu hình SMTP (mặc định App_Data/emails).</summary>
    public string? PickupDirectory { get; set; }
}
