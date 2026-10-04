using System.Globalization;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Models.Reservations;

/// <summary>
/// S2-09 Task 1: trang xác nhận đặt bàn. Mã đặt bàn luôn hiển thị đầy đủ,
/// kể cả khi email xác nhận chưa gửi được hoặc khách không nhập email.
/// </summary>
public sealed record BookingSuccessViewModel(
    string Code,
    string? TableCode,
    DateTime? StartsAt,
    int? GuestCount,
    BookingEmailOutcome EmailOutcome,
    string? MaskedEmail,
    long? ReservationId = null,
    BookingEmailCustomerStatus? EmailStatus = null)
{
    /// <summary>S2-09 Task 2: Id lượt đặt bàn vừa tạo (chỉ nằm trong TempData của trình duyệt đã đặt bàn).</summary>
    public const string ReservationIdKey = "Booking.ReservationId";

    public const string CodeKey = "Booking.Code";
    public const string TableCodeKey = "Booking.TableCode";
    public const string StartsAtKey = "Booking.StartsAt";
    public const string GuestCountKey = "Booking.GuestCount";
    public const string EmailOutcomeKey = "Booking.EmailOutcome";
    public const string EmailToKey = "Booking.EmailTo";

    public bool EmailSent => EmailOutcome == BookingEmailOutcome.Sent;

    /// <summary>Mã đặt bàn hiển thị cho khách = mã bàn đã chọn (ví dụ A05). Lượt cũ chưa có bàn dùng mã nội bộ.</summary>
    public string DisplayCode => TableCode ?? Code;

    private const string CodeName = "mã đặt bàn";
    public bool EmailFailed => EmailOutcome == BookingEmailOutcome.Failed;

    /// <summary>Thông báo về email xác nhận, tách riêng với thông báo đặt bàn thành công.</summary>
    public string EmailMessage => EmailOutcome switch
    {
        BookingEmailOutcome.Sent => $"Email xác nhận đã được gửi tới {MaskedEmail ?? "email của bạn"}.",
        BookingEmailOutcome.Failed => $"Email xác nhận chưa gửi được. Lượt đặt bàn của bạn vẫn được ghi nhận — vui lòng lưu lại {CodeName} ở trên.",
        _ => $"Bạn không nhập email nên hệ thống không gửi email xác nhận. Vui lòng lưu lại {CodeName} ở trên."
    };

    /// <summary>Thông báo email đang hiển thị: trạng thái mới nhất (kể cả sau các lần gửi lại) nếu có.</summary>
    public string CurrentEmailMessage => EmailStatus?.Message ?? EmailMessage;

    public string EmailCssClass => EmailStatus?.CssClass
        ?? (EmailSent ? "alert-info" : EmailFailed ? "alert-warning" : "alert-secondary");

    /// <summary>Email đã có kết quả cuối cùng (hoặc không có email): trang không cần tự cập nhật.</summary>
    public bool EmailFinal => EmailStatus?.IsFinal ?? true;

    /// <summary>Đọc từ TempData; trả về null khi không có lượt đặt bàn vừa tạo (mở thẳng trang).</summary>
    public static BookingSuccessViewModel? FromTempData(Func<string, object?> read)
    {
        if (read(CodeKey) is not string code || string.IsNullOrWhiteSpace(code)) return null;
        DateTime? startsAt = DateTime.TryParseExact(read(StartsAtKey) as string, "yyyy-MM-ddTHH:mm",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;
        int? guests = read(GuestCountKey) switch
        {
            int i => i,
            long l => (int)l,
            string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) => n,
            _ => null
        };
        var outcome = Enum.TryParse<BookingEmailOutcome>(read(EmailOutcomeKey) as string, out var o) ? o : BookingEmailOutcome.NotRequested;
        var table = (read(TableCodeKey) as string)?.Trim();
        long? reservationId = long.TryParse(read(ReservationIdKey) as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;
        return new BookingSuccessViewModel(code.Trim(), string.IsNullOrEmpty(table) ? null : table, startsAt, guests, outcome,
            read(EmailToKey) as string, reservationId);
    }

    /// <summary>Che bớt email trên màn hình, ví dụ "khach@example.com" → "k***h@example.com".</summary>
    public static string? MaskEmail(string? email)
    {
        var value = email?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        var at = value.IndexOf('@');
        if (at <= 0) return null;
        var local = value[..at];
        var masked = local.Length <= 2 ? local[0] + "***" : local[0] + "***" + local[^1];
        return masked + value[at..];
    }
}

/// <summary>
/// S2-09 Task 2: trạng thái email xác nhận hiển thị cho khách trên trang xác nhận đặt bàn.
/// Khi email chưa gửi được, khách thấy hệ thống đang tự gửi lại và cuối cùng là kết quả cuối cùng (đã gửi / không gửi được).
/// </summary>
public sealed record BookingEmailCustomerStatus(EmailDeliveryState State, bool IsFinal, string Message, string CssClass)
{
    public static BookingEmailCustomerStatus? From(ReservationEmailRecord? email, string displayCode, string? maskedEmail)
    {
        if (email is null) return null;
        var item = ReservationEmailStatus.ToItem(email);
        var to = maskedEmail ?? "email của bạn";
        var keep = $"Lượt đặt bàn của bạn vẫn được ghi nhận — vui lòng lưu lại mã đặt bàn {displayCode}.";
        var attempts = item.AttemptCount;
        return item.State switch
        {
            EmailDeliveryState.Sent => new(item.State, true,
                $"Email xác nhận đã được gửi tới {to}."
                + (attempts > 1 ? $" Lần gửi đầu chưa thành công; hệ thống đã tự gửi lại và gửi được ở lần thử thứ {attempts}." : ""),
                "alert-info"),
            EmailDeliveryState.Failed => new(item.State, true,
                $"Không gửi được email xác nhận tới {to} sau {attempts} lần thử"
                + (attempts > 1 ? $" (lần gửi đầu và {attempts - 1} lần gửi lại)" : "") + $". {keep}",
                "alert-danger"),
            EmailDeliveryState.Cancelled => new(item.State, true,
                $"Email xác nhận không được gửi vì lượt đặt bàn đã kết thúc. {keep}", "alert-secondary"),
            EmailDeliveryState.Retrying => new(item.State, false,
                $"Email xác nhận chưa gửi được. {keep} Hệ thống sẽ tự gửi lại"
                + (item.NextRetryAt is null ? "" : $" lúc {item.NextRetryAt.Value:HH:mm}")
                + $" (đã thử {attempts}/{item.MaxAttempts} lần; tối đa {item.MaxRetries} lần gửi lại, mỗi lần cách nhau 5 phút).",
                "alert-warning"),
            EmailDeliveryState.Sending => new(item.State, false,
                attempts > 1 ? $"Đang gửi lại email xác nhận ({EmailRetryPolicy.AttemptLabel(attempts).ToLowerInvariant()}/{item.MaxRetries})…"
                             : $"Đang gửi email xác nhận tới {to}…",
                "alert-warning"),
            _ => new(item.State, false, $"Đang chờ gửi email xác nhận tới {to}.", "alert-secondary")
        };
    }
}
