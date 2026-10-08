using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace RestaurantManagement.Web.Services;

/// <summary>
/// Liên kết "Xác nhận đặt bàn" trong email gửi khách. Liên kết chứa mã bảo mật (ASP.NET Data Protection)
/// bọc mã đặt bàn, nên khách không cần đăng nhập và không ai đoán được liên kết của lượt đặt bàn khác.
/// Địa chỉ web lấy từ cấu hình "Booking:PublicBaseUrl" (ví dụ https://bepnha.vn); chưa cấu hình thì dùng
/// địa chỉ của yêu cầu web gần nhất (đủ cho máy phát triển, ví dụ https://localhost:7114).
/// </summary>
public sealed class BookingConfirmationLinks
{
    public const string Purpose = "RestaurantManagement.BookingConfirmation.v1";
    public const string ConfirmPath = "/Reservations/Confirm";

    private readonly IDataProtector _protector;
    private readonly string? _configuredBaseUrl;
    private string? _observedBaseUrl;

    public BookingConfirmationLinks(IDataProtectionProvider dataProtection, IConfiguration configuration)
    {
        _protector = dataProtection.CreateProtector(Purpose);
        var configured = configuration["Booking:PublicBaseUrl"]?.Trim();
        if (string.IsNullOrWhiteSpace(configured)) configured = configuration["TableQr:PublicBaseUrl"]?.Trim();
        _configuredBaseUrl = string.IsNullOrWhiteSpace(configured) ? null : configured.TrimEnd('/');
    }

    /// <summary>Địa chỉ web dùng trong email; null khi chưa cấu hình và chưa có yêu cầu web nào.</summary>
    public string? BaseUrl => _configuredBaseUrl ?? Volatile.Read(ref _observedBaseUrl);

    /// <summary>Ghi nhớ địa chỉ web từ yêu cầu đang xử lý (gọi mỗi yêu cầu, chỉ lưu lần đầu).</summary>
    public void Observe(HttpRequest request)
    {
        if (_configuredBaseUrl is not null || Volatile.Read(ref _observedBaseUrl) is not null || !request.Host.HasValue) return;
        Interlocked.CompareExchange(ref _observedBaseUrl, $"{request.Scheme}://{request.Host}{request.PathBase}".TrimEnd('/'), null);
    }

    public string CreateToken(string reservationCode) => _protector.Protect(reservationCode.Trim());

    /// <summary>Mã đặt bàn nằm trong mã bảo mật; null khi mã bảo mật sai, bị sửa hoặc thuộc máy chủ khác.</summary>
    public string? ReadToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            var code = _protector.Unprotect(token.Trim());
            return string.IsNullOrWhiteSpace(code) ? null : code;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <summary>Liên kết đầy đủ để đặt vào email; null khi chưa biết địa chỉ web.</summary>
    public string? CreateUrl(string reservationCode)
    {
        var baseUrl = BaseUrl;
        return baseUrl is null ? null : $"{baseUrl}{ConfirmPath}?token={Uri.EscapeDataString(CreateToken(reservationCode))}";
    }
}
