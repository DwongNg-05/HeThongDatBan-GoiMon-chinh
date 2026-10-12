using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using RestaurantManagement.Web.Models.Reservations;
using RestaurantManagement.Web.Services.EmailVerification;

namespace RestaurantManagement.Web.Services;

/// <summary>
/// Thông tin của một lượt đặt bàn dùng trong email xác nhận (S2-09 Task 1),
/// đọc từ EmailOutbox.PayloadJson do usp_QueueBookingEmail lưu lúc đặt bàn.
/// </summary>
public sealed record BookingEmailDetails(
    string Code,
    string CustomerName,
    DateTime StartsAtUtc,
    int GuestCount,
    string? AreaName,
    string RestaurantName,
    string RestaurantAddress,
    string? RestaurantPhone,
    string? TableCode = null,
    string? CancelReason = null)
{
    /// <summary>
    /// Mã đặt bàn gửi cho khách: luôn là mã đặt bàn 6 ký tự (ví dụ ED095C) — đúng mã khách nhập ở "Tra cứu đặt bàn".
    /// Bàn đã giữ (ví dụ LAU02) hiển thị ở dòng riêng.
    /// </summary>
    public string DisplayCode => Code;

    /// <summary>Giờ bắt đầu theo giờ Việt Nam.</summary>
    public DateTime StartsAtVietnam => VietnamTime.FromUtc(StartsAtUtc);
}

/// <summary>
/// Nội dung email đặt bàn: mã đặt bàn (để tra cứu), bàn đã giữ, ngày giờ, số khách, địa chỉ quán.
/// Email chỉ gửi khi Quản lý / Phục vụ xác nhận lượt đặt (BookingConfirmed, migration 050); email huỷ khi lượt bị huỷ.
/// Tiêu đề dạng "Bếp Nhà - Xác nhận đặt bàn ED095C". Có bản HTML và bản chữ thường; mọi dữ liệu khách nhập đều được mã hoá HTML.
/// </summary>
public static class BookingConfirmationEmail
{
    private const string DefaultRestaurantName = "Bếp Nhà";
    private static readonly HtmlEncoder Html = HtmlEncoder.Create(UnicodeRanges.All);
    private static readonly string[] Weekdays = ["Chủ nhật", "Thứ Hai", "Thứ Ba", "Thứ Tư", "Thứ Năm", "Thứ Sáu", "Thứ Bảy"];

    public static BookingEmailDetails ParsePayload(string payloadJson)
    {
        using var json = JsonDocument.Parse(payloadJson);
        var root = json.RootElement;
        string? Text(string name) =>
            root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        var code = Text("Code")?.Trim();
        if (string.IsNullOrEmpty(code)) throw new FormatException("Thiếu mã đặt bàn trong nội dung email.");
        var startsAt = DateTime.Parse(Text("StartsAt") ?? throw new FormatException("Thiếu giờ đặt bàn."),
            CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        var guests = root.TryGetProperty("GuestCount", out var g) && g.ValueKind == JsonValueKind.Number ? g.GetInt32() : 0;
        return new BookingEmailDetails(
            code,
            Text("CustomerName")?.Trim() ?? string.Empty,
            DateTime.SpecifyKind(startsAt, DateTimeKind.Utc),
            guests,
            string.IsNullOrWhiteSpace(Text("AreaName")) ? null : Text("AreaName")!.Trim(),
            string.IsNullOrWhiteSpace(Text("RestaurantName")) ? DefaultRestaurantName : Text("RestaurantName")!.Trim(),
            Text("RestaurantAddress")?.Trim() ?? string.Empty,
            string.IsNullOrWhiteSpace(Text("RestaurantPhone")) ? null : Text("RestaurantPhone")!.Trim(),
            string.IsNullOrWhiteSpace(Text("TableCode")) ? null : Text("TableCode")!.Trim(),
            string.IsNullOrWhiteSpace(Text("CancelReason")) ? null : Text("CancelReason")!.Trim());
    }

    /// <summary>Ví dụ: "19:00, Thứ Hai ngày 05/10/2026".</summary>
    public static string FormatDateTime(DateTime vietnamTime) =>
        $"{vietnamTime.ToString("HH:mm", CultureInfo.InvariantCulture)}, {Weekdays[(int)vietnamTime.DayOfWeek]} ngày {vietnamTime.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}";

    public const string Received = "BookingReceived";
    public const string Confirmed = "BookingConfirmed";
    public const string Cancelled = "BookingCancelled";

    /// <summary>Màu thương hiệu (đỏ logo Bếp Nhà) dùng trong email.</summary>
    private const string Brand = "#b40829", BrandDark = "#8f0620", BrandSoft = "#fff4f5";

    /// <summary>Ví dụ: "Bếp Nhà - Xác nhận đặt bàn ED095C" (tên nhà hàng đứng trước).</summary>
    public static string Subject(BookingEmailDetails d, string messageType = Confirmed) => messageType == Cancelled
        ? $"{d.RestaurantName} - Đã huỷ đặt bàn {d.Code}"
        : $"{d.RestaurantName} - Xác nhận đặt bàn {d.Code}";

    /// <summary>Email xác nhận (BookingConfirmed; BookingReceived cũ dùng cùng mẫu) hoặc email báo huỷ (BookingCancelled).</summary>
    /// <param name="confirmUrl">Chỉ cho email "đã ghi nhận" cũ: liên kết để khách tự xác nhận (lượt đã có bàn).</param>
    /// <param name="lookupUrl">Liên kết "Tra cứu đặt bàn" đã điền sẵn mã đặt bàn (khách không phải gõ lại mã).</param>
    public static EmailMessage Create(string to, BookingEmailDetails d, string messageType = Confirmed, string? confirmUrl = null,
        string? lookupUrl = null)
    {
        var cancelled = messageType == Cancelled;
        var confirmed = messageType == Confirmed;
        var showConfirm = messageType == Received && !string.IsNullOrWhiteSpace(confirmUrl) && d.TableCode is not null;
        var showLookup = !cancelled && !string.IsNullOrWhiteSpace(lookupUrl);
        var title = cancelled ? "Huỷ đặt bàn" : "Xác nhận đặt bàn";
        var intro = cancelled
            ? "Nhà hàng rất tiếc phải thông báo lượt đặt bàn dưới đây đã bị huỷ."
            : confirmed
                ? d.TableCode is null
                    ? "Nhà hàng đã xác nhận lượt đặt bàn của bạn."
                    : $"Nhà hàng đã xác nhận lượt đặt bàn của bạn và giữ bàn {d.TableCode} trong khung giờ dưới đây."
                : showConfirm
                    ? "Nhà hàng đã ghi nhận yêu cầu đặt bàn của bạn. Vui lòng bấm nút “Xác nhận đặt bàn” bên dưới để giữ bàn."
                    : "Nhà hàng đã ghi nhận yêu cầu đặt bàn của bạn. Vui lòng giữ lại mã đặt bàn dưới đây.";
        var note = cancelled
            ? "Nếu bạn vẫn muốn dùng bữa, vui lòng đặt bàn mới hoặc gọi điện cho nhà hàng."
            : "Giờ hiển thị theo giờ Việt Nam (UTC+7). Dùng mã đặt bàn và số điện thoại để tra cứu hoặc huỷ đặt bàn.";
        // Mã hoá ký tự HTML nguy hiểm nhưng giữ nguyên chữ tiếng Việt để email dễ đọc (kể cả khi xem mã nguồn).
        static string E(string? value) => Html.Encode(value ?? string.Empty);
        var when = FormatDateTime(d.StartsAtVietnam);
        var guestName = string.IsNullOrWhiteSpace(d.CustomerName) ? "quý khách" : d.CustomerName;
        var address = string.IsNullOrWhiteSpace(d.RestaurantAddress) ? "(nhà hàng sẽ liên hệ để báo địa chỉ)" : d.RestaurantAddress;
        string Row(string label, string valueHtml) =>
            $"<tr><td style=\"padding:8px 0;color:#6e6e6e;width:120px;vertical-align:top\">{label}</td><td style=\"padding:8px 0;font-weight:600\">{valueHtml}</td></tr>";
        // Nút dạng bảng lồng để hiện đúng trên Gmail/Outlook, kèm liên kết dự phòng dạng chữ.
        string Button(string url, string label, string hint) => $"""
            <tr><td align="center" style="padding:4px 24px 20px">
              <table role="presentation" cellpadding="0" cellspacing="0"><tr><td style="border-radius:10px;background:{Brand}">
                <a href="{E(url)}" target="_blank" style="display:inline-block;padding:14px 30px;font-size:16px;font-weight:700;color:#ffffff;text-decoration:none;border-radius:10px">{label}</a>
              </td></tr></table>
              <div style="font-size:12px;color:#8a8a8a;margin-top:10px;line-height:1.5">{hint}<br><a href="{E(url)}" style="color:{Brand};word-break:break-all">{E(url)}</a></div>
            </td></tr>
            """;

        var actionHtml = showConfirm
            ? Button(confirmUrl!, "Xác nhận đặt bàn", "Nút không bấm được? Mở liên kết sau trong trình duyệt:")
            : showLookup
                ? Button(lookupUrl!, "Tra cứu đặt bàn", "Mã đặt bàn đã được điền sẵn, bạn chỉ cần nhập số điện thoại. Hoặc mở liên kết:")
                : string.Empty;

        var html = $"""
            <!doctype html>
            <html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>{E(d.RestaurantName)} - {title} {E(d.Code)}</title></head>
            <body style="margin:0;padding:0;background:#f6f6f6;font-family:Arial,Helvetica,sans-serif;color:#222222">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f6f6f6;padding:24px 12px">
                <tr><td align="center">
                  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:520px;background:#ffffff;border-radius:16px;overflow:hidden">
                    <tr><td style="background:{Brand};padding:18px 24px;color:#ffffff;font-size:20px;font-weight:700">{E(d.RestaurantName)} - {title}</td></tr>
                    <tr><td style="padding:24px 24px 8px;font-size:16px;line-height:1.6">
                      <p style="margin:0 0 12px">Xin chào <strong>{E(guestName)}</strong>,</p>
                      <p style="margin:0">{E(intro)}</p>
                    </td></tr>
                    <tr><td align="center" style="padding:12px 24px">
                      <div style="font-size:13px;color:#6e6e6e;margin-bottom:6px">Mã đặt bàn (dùng để tra cứu)</div>
                      <div style="display:inline-block;padding:10px 22px;border:2px solid {Brand};border-radius:12px;background:{BrandSoft};font-family:'Courier New',Consolas,monospace;font-size:32px;font-weight:700;letter-spacing:6px;color:{BrandDark};-webkit-user-select:all;user-select:all">{E(d.Code)}</div>
                    </td></tr>
                    <tr><td style="padding:8px 24px 16px">
                      <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="font-size:15px;border-top:1px solid #eeeeee">
                        {(d.TableCode is null ? "" : Row("Bàn", E(d.TableCode)))}
                        {Row("Ngày giờ", E(when))}
                        {Row("Số khách", E($"{d.GuestCount} người"))}
                        {(d.AreaName is null ? "" : Row("Khu vực", E(d.AreaName)))}
                        {Row("Địa chỉ quán", E(address))}
                        {(d.RestaurantPhone is null ? "" : Row("Điện thoại", E(d.RestaurantPhone)))}
                        {(cancelled && d.CancelReason is not null ? Row("Lý do huỷ", E(d.CancelReason)) : "")}
                      </table>
                    </td></tr>
                    {actionHtml}
                    <tr><td style="padding:0 24px 24px">
                      <div style="background:{BrandSoft};border:1px solid #f1b8c2;border-radius:10px;padding:12px 14px;font-size:14px;line-height:1.5">
                        {E(note)}
                        {(cancelled ? "" : $" Khi đến quán, vui lòng đọc mã đặt bàn <strong>{E(d.Code)}</strong> cho nhân viên.")}
                      </div>
                    </td></tr>
                    <tr><td style="background:#fafafa;padding:14px 24px;font-size:12px;color:#8a8a8a">Email được gửi tự động, vui lòng không trả lời.</td></tr>
                  </table>
                </td></tr>
              </table>
            </body></html>
            """;

        var text = string.Join("\n", new[]
        {
            $"Xin chào {guestName},",
            "",
            cancelled ? $"{d.RestaurantName} rất tiếc phải thông báo lượt đặt bàn dưới đây đã bị huỷ." : intro,
            "",
            $"Mã đặt bàn:   {d.Code}",
            d.TableCode is null ? null : $"Bàn:          {d.TableCode}",
            $"Ngày giờ:     {when} (giờ Việt Nam)",
            $"Số khách:     {d.GuestCount} người",
            d.AreaName is null ? null : $"Khu vực:      {d.AreaName}",
            $"Địa chỉ quán: {address}",
            d.RestaurantPhone is null ? null : $"Điện thoại:   {d.RestaurantPhone}",
            cancelled && d.CancelReason is not null ? $"Lý do huỷ:    {d.CancelReason}" : null,
            "",
            showConfirm ? $"Xác nhận đặt bàn: {confirmUrl}" : showLookup ? $"Tra cứu đặt bàn: {lookupUrl}" : null,
            showConfirm || showLookup ? "" : null,
            cancelled ? note : $"Khi đến quán, vui lòng đọc mã đặt bàn {d.Code} cho nhân viên.",
            "Email được gửi tự động, vui lòng không trả lời."
        }.Where(line => line is not null));

        return new EmailMessage(to, Subject(d, messageType), html, text);
    }
}
