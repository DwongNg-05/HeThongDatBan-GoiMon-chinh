using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using RestaurantManagement.Web.Services.EmailVerification;

namespace RestaurantManagement.Web.Services;

/// <summary>
/// S4-08 Task 1: nội dung email nhắc lịch gửi trước giờ hẹn 2 tiếng.
/// Bắt buộc có: mã đặt bàn, giờ hẹn và bàn đã xếp. Thông tin đọc từ EmailOutbox.PayloadJson
/// (usp_QueueDueBookingReminders chụp lại lúc xếp hàng), cùng định dạng với email xác nhận (<see cref="BookingConfirmationEmail"/>).
/// Chưa có liên kết huỷ đặt bàn (task sau). Mọi dữ liệu khách nhập đều được mã hoá HTML.
/// </summary>
public static class BookingReminderEmail
{
    public const string MessageType = "BookingReminder";

    private static readonly HtmlEncoder Html = HtmlEncoder.Create(UnicodeRanges.All);
    private const string Brand = "#b40829", BrandDark = "#8f0620", BrandSoft = "#fff4f5";

    /// <summary>Dòng "Bàn" khi lượt đặt chưa có bàn (không xảy ra với lượt đã xác nhận, phòng dữ liệu cũ).</summary>
    public const string NoTableLabel = "Nhà hàng sẽ xếp bàn khi bạn đến";

    /// <summary>Ví dụ: "Bếp Nhà - Nhắc lịch đặt bàn ED095C".</summary>
    public static string Subject(BookingEmailDetails d) => $"{d.RestaurantName} - Nhắc lịch đặt bàn {d.Code}";

    /// <param name="lookupUrl">Liên kết "Tra cứu đặt bàn" đã điền sẵn mã (không bắt buộc).</param>
    public static EmailMessage Create(string to, BookingEmailDetails d, string? lookupUrl = null)
    {
        static string E(string? value) => Html.Encode(value ?? string.Empty);
        var when = BookingConfirmationEmail.FormatDateTime(d.StartsAtVietnam);
        var table = d.TableCode ?? NoTableLabel;
        var guestName = string.IsNullOrWhiteSpace(d.CustomerName) ? "quý khách" : d.CustomerName;
        var address = string.IsNullOrWhiteSpace(d.RestaurantAddress) ? null : d.RestaurantAddress;
        var intro = $"Nhà hàng xin nhắc bạn có lịch đặt bàn lúc {d.StartsAtVietnam.ToString("HH:mm", CultureInfo.InvariantCulture)}."
            + (d.TableCode is null ? "" : $" Nhà hàng đang giữ bàn {d.TableCode} cho bạn.");
        var showLookup = !string.IsNullOrWhiteSpace(lookupUrl);

        string Row(string label, string valueHtml, string? dataField = null) =>
            $"<tr><td style=\"padding:8px 0;color:#6e6e6e;width:120px;vertical-align:top\">{label}</td>" +
            $"<td style=\"padding:8px 0;font-weight:600\"{(dataField is null ? "" : $" data-field=\"{dataField}\"")}>{valueHtml}</td></tr>";

        var lookupHtml = showLookup ? $"""
            <tr><td align="center" style="padding:4px 24px 20px">
              <table role="presentation" cellpadding="0" cellspacing="0"><tr><td style="border-radius:10px;background:{Brand}">
                <a href="{E(lookupUrl)}" target="_blank" style="display:inline-block;padding:14px 30px;font-size:16px;font-weight:700;color:#ffffff;text-decoration:none;border-radius:10px">Xem lượt đặt bàn</a>
              </td></tr></table>
            </td></tr>
            """ : string.Empty;

        var html = $"""
            <!doctype html>
            <html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>{E(Subject(d))}</title></head>
            <body style="margin:0;padding:0;background:#f6f6f6;font-family:Arial,Helvetica,sans-serif;color:#222222">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f6f6f6;padding:24px 12px">
                <tr><td align="center">
                  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:520px;background:#ffffff;border-radius:16px;overflow:hidden">
                    <tr><td style="background:{Brand};padding:18px 24px;color:#ffffff;font-size:20px;font-weight:700">{E(d.RestaurantName)} - Nhắc lịch đặt bàn</td></tr>
                    <tr><td style="padding:24px 24px 8px;font-size:16px;line-height:1.6">
                      <p style="margin:0 0 12px">Xin chào <strong>{E(guestName)}</strong>,</p>
                      <p style="margin:0">{E(intro)}</p>
                    </td></tr>
                    <tr><td align="center" style="padding:12px 24px">
                      <div style="font-size:13px;color:#6e6e6e;margin-bottom:6px">Mã đặt bàn</div>
                      <div data-field="code" style="display:inline-block;padding:10px 22px;border:2px solid {Brand};border-radius:12px;background:{BrandSoft};font-family:'Courier New',Consolas,monospace;font-size:32px;font-weight:700;letter-spacing:6px;color:{BrandDark}">{E(d.Code)}</div>
                    </td></tr>
                    <tr><td style="padding:8px 24px 16px">
                      <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="font-size:15px;border-top:1px solid #eeeeee">
                        {Row("Giờ hẹn", E(when), "starts-at")}
                        {Row("Bàn", E(table), "table")}
                        {(d.AreaName is null ? "" : Row("Khu vực", E(d.AreaName)))}
                        {(d.GuestCount > 0 ? Row("Số khách", E($"{d.GuestCount} người")) : "")}
                        {(address is null ? "" : Row("Địa chỉ quán", E(address)))}
                        {(d.RestaurantPhone is null ? "" : Row("Điện thoại", E(d.RestaurantPhone)))}
                      </table>
                    </td></tr>
                    {lookupHtml}
                    <tr><td style="padding:0 24px 24px">
                      <div style="background:{BrandSoft};border:1px solid #f1b8c2;border-radius:10px;padding:12px 14px;font-size:14px;line-height:1.5">
                        Giờ hiển thị theo giờ Việt Nam (UTC+7). Khi đến quán, vui lòng đọc mã đặt bàn <strong>{E(d.Code)}</strong> cho nhân viên.
                        Nếu không đến được, vui lòng gọi điện báo nhà hàng.
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
            intro,
            "",
            $"Mã đặt bàn:   {d.Code}",
            $"Giờ hẹn:      {when} (giờ Việt Nam)",
            $"Bàn:          {table}",
            d.AreaName is null ? null : $"Khu vực:      {d.AreaName}",
            d.GuestCount > 0 ? $"Số khách:     {d.GuestCount} người" : null,
            address is null ? null : $"Địa chỉ quán: {address}",
            d.RestaurantPhone is null ? null : $"Điện thoại:   {d.RestaurantPhone}",
            "",
            showLookup ? $"Xem lượt đặt bàn: {lookupUrl}" : null,
            showLookup ? "" : null,
            $"Khi đến quán, vui lòng đọc mã đặt bàn {d.Code} cho nhân viên. Nếu không đến được, vui lòng gọi điện báo nhà hàng.",
            "Email được gửi tự động, vui lòng không trả lời."
        }.Where(line => line is not null));

        return new EmailMessage(to, Subject(d), html, text);
    }
}
