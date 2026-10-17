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
    /// <summary>Trang "Tra cứu đặt bàn" — liên kết trong email điền sẵn mã đặt bàn (?code=ED095C).</summary>
    public const string LookupPath = "/ReservationLookup";

    /// <summary>S4-08 Task 4: mã bảo mật riêng cho đường dẫn huỷ trong email nhắc (khác mục đích với nút xác nhận).</summary>
    public const string CancelPurpose = "RestaurantManagement.BookingReminderCancel.v1";
    public const string CancelPath = "/Reservations/CancelFromEmail";

    private readonly IDataProtector _protector;
    private readonly IDataProtector _cancelProtector;
    private readonly string? _configuredBaseUrl;
    private string? _observedBaseUrl;

    public BookingConfirmationLinks(IDataProtectionProvider dataProtection, IConfiguration configuration)
    {
        _protector = dataProtection.CreateProtector(Purpose);
        _cancelProtector = dataProtection.CreateProtector(CancelPurpose);
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

    /// <summary>Liên kết "Tra cứu đặt bàn" đã điền sẵn mã đặt bàn; null khi chưa biết địa chỉ web.</summary>
    public string? CreateLookupUrl(string reservationCode)
    {
        var baseUrl = BaseUrl;
        return baseUrl is null ? null : $"{baseUrl}{LookupPath}?code={Uri.EscapeDataString(reservationCode.Trim())}";
    }

    /// <summary>
    /// S4-08 Task 4: mã bảo mật của đường dẫn huỷ — bọc Id và mã đặt bàn của đúng một lượt, nên không dùng được cho lượt khác.
    /// </summary>
    public string CreateCancelToken(long reservationId, string reservationCode) =>
        _cancelProtector.Protect($"{reservationId}|{reservationCode.Trim()}");

    /// <summary>Id + mã đặt bàn trong mã bảo mật huỷ; null khi mã sai, bị sửa, thuộc máy chủ khác hoặc là mã của nút xác nhận.</summary>
    public (long ReservationId, string Code)? ReadCancelToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            var parts = _cancelProtector.Unprotect(token.Trim()).Split('|');
            return parts.Length == 2 && long.TryParse(parts[0], out var id) && id > 0 && parts[1].Length == 6
                ? (id, parts[1])
                : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <summary>Đường dẫn huỷ đầy đủ để đặt vào email nhắc; null khi chưa biết địa chỉ web.</summary>
    public string? CreateCancelUrl(long reservationId, string reservationCode)
    {
        var baseUrl = BaseUrl;
        return baseUrl is null ? null : $"{baseUrl}{CancelPath}?token={Uri.EscapeDataString(CreateCancelToken(reservationId, reservationCode))}";
    }

    /// <summary>Liên kết đầy đủ để đặt vào email; null khi chưa biết địa chỉ web.</summary>
    public string? CreateUrl(string reservationCode)
    {
        var baseUrl = BaseUrl;
        return baseUrl is null ? null : $"{baseUrl}{ConfirmPath}?token={Uri.EscapeDataString(CreateToken(reservationCode))}";
    }
}
