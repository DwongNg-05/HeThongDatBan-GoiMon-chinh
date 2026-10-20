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
    string? CancelReason = null,
    string? RejectionReason = null)
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
/// Bản HTML trình bày như một tấm thiệp mời: nền đỏ rượu vang, giấy kem, con dấu sáp đỏ (ảnh nhúng, xem EmailBranding).
/// Email chỉ gửi khi Quản lý / Phục vụ xác nhận lượt đặt (BookingConfirmed, migration 050); email huỷ khi lượt bị huỷ.
/// Tiêu đề dạng "Bếp Nhà - Xác nhận đặt bàn ED095C". Có bản HTML và bản chữ thường; mọi dữ liệu khách nhập đều được mã hoá HTML.
/// </summary>
public static class BookingConfirmationEmail
{
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
            // Dữ liệu mẫu cũ "Nhà hàng Demo" hoặc tên trống → "Bếp Nhà" (EmailBranding.NormalizeName).
            EmailBranding.NormalizeName(Text("RestaurantName")),
            Text("RestaurantAddress")?.Trim() ?? string.Empty,
            string.IsNullOrWhiteSpace(Text("RestaurantPhone")) ? null : Text("RestaurantPhone")!.Trim(),
            string.IsNullOrWhiteSpace(Text("TableCode")) ? null : Text("TableCode")!.Trim(),
            string.IsNullOrWhiteSpace(Text("CancelReason")) ? null : Text("CancelReason")!.Trim(),
            string.IsNullOrWhiteSpace(Text("RejectionReason")) ? null : Text("RejectionReason")!.Trim());
    }

    /// <summary>Ví dụ: "19:00, Thứ Hai ngày 05/10/2026".</summary>
    public static string FormatDateTime(DateTime vietnamTime) =>
        $"{vietnamTime.ToString("HH:mm", CultureInfo.InvariantCulture)}, {Weekdays[(int)vietnamTime.DayOfWeek]} ngày {vietnamTime.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}";

    public const string Received = "BookingReceived";
    public const string Confirmed = "BookingConfirmed";
    public const string Cancelled = "BookingCancelled";
    /// <summary>Email báo từ chối đặt bàn kèm lý do (usp_RejectReservation, migration 065).</summary>
    public const string Rejected = "BookingRejected";

    /// <summary>Màu thương hiệu (đỏ logo Bếp Nhà) dùng trong email.</summary>
    private const string Brand = "#3c0102", BrandDark = "#260001";

    /// <summary>Ví dụ: "Bếp Nhà - Xác nhận đặt bàn ED095C" (tên nhà hàng đứng trước).</summary>
    public static string Subject(BookingEmailDetails d, string messageType = Confirmed) => messageType == Rejected
        ? $"{d.RestaurantName} - Không thể nhận đặt bàn {d.Code}"
        : messageType == Cancelled
        ? IsCustomerCancellation(d) ? $"{d.RestaurantName} - Huỷ đặt bàn thành công {d.Code}" : $"{d.RestaurantName} - Đã huỷ đặt bàn {d.Code}"
        : $"{d.RestaurantName} - Xác nhận đặt bàn {d.Code}";

    /// <summary>Email xác nhận (BookingConfirmed; BookingReceived cũ dùng cùng mẫu) hoặc email báo huỷ (BookingCancelled).</summary>
    /// <param name="confirmUrl">Chỉ cho email "đã ghi nhận" cũ: liên kết để khách tự xác nhận (lượt đã có bàn).</param>
    /// <param name="lookupUrl">Liên kết "Tra cứu đặt bàn" đã điền sẵn mã đặt bàn (khách không phải gõ lại mã).</param>
    /// <summary>Lý do huỷ ghi khi khách tự huỷ ở trang "Tra cứu đặt bàn" (ReservationStore.CancelAsync).</summary>
    public const string OnlineCancelReason = "Khách huỷ trực tuyến";
    /// <summary>Lý do huỷ ghi khi khách tự huỷ bằng đường dẫn trong email nhắc lịch (migration 062).</summary>
    public const string ReminderLinkCancelReason = "Khách huỷ qua email nhắc lịch";

    /// <summary>
    /// Khách tự huỷ: lý do là một trong hai lý do cố định ở trên (hoặc trống, dữ liệu cũ).
    /// Nhân viên huỷ luôn phải ghi lý do riêng (dbo.usp_StaffCancelReservation).
    /// </summary>
    public static bool IsCustomerCancellation(BookingEmailDetails d) =>
        string.IsNullOrWhiteSpace(d.CancelReason)
        || d.CancelReason.Trim() == OnlineCancelReason
        || d.CancelReason.Trim() == ReminderLinkCancelReason;

    public static EmailMessage Create(string to, BookingEmailDetails d, string messageType = Confirmed, string? confirmUrl = null,
        string? lookupUrl = null)
    {
        var cancelled = messageType == Cancelled;
        // Khách tự huỷ: email báo "Huỷ đặt bàn thành công" thay cho lời xin lỗi của nhà hàng.
        var customerCancelled = cancelled && IsCustomerCancellation(d);
        var confirmed = messageType == Confirmed;
        // Nhà hàng từ chối lượt chờ xác nhận: email xin lỗi kèm lý do (Hết bàn / Ngoài giờ phục vụ / Không liên lạc được).
        var rejected = messageType == Rejected;
        var rejectionLabel = d.RejectionReason is not null && RejectionReasons.Labels.TryGetValue(d.RejectionReason, out var reasonLabel) ? reasonLabel : null;
        var rejectionText = RejectionReasons.CustomerText(d.RejectionReason);
        // Email huỷ / từ chối: không có lời dặn "đọc mã khi đến quán".
        var closed = cancelled || rejected;
        var showConfirm = messageType == Received && !string.IsNullOrWhiteSpace(confirmUrl) && d.TableCode is not null;
        var showLookup = !cancelled && !string.IsNullOrWhiteSpace(lookupUrl);
        var title = rejected ? "Không thể nhận đặt bàn" : customerCancelled ? "Huỷ đặt bàn thành công" : cancelled ? "Huỷ đặt bàn" : "Xác nhận đặt bàn";
        var intro = rejected
            ? "Nhà hàng rất tiếc chưa thể nhận lượt đặt bàn dưới đây của bạn." + (rejectionText is null ? "" : " " + rejectionText)
            : customerCancelled
            ? "Bạn đã huỷ đặt bàn thành công. Lượt đặt bàn dưới đây đã được huỷ, bạn không cần làm gì thêm."
            : cancelled
            ? "Nhà hàng rất tiếc phải thông báo lượt đặt bàn dưới đây đã bị huỷ."
            : confirmed
                ? d.TableCode is null
                    ? "Nhà hàng đã xác nhận lượt đặt bàn của bạn."
                    : $"Nhà hàng đã xác nhận lượt đặt bàn của bạn và giữ bàn {d.TableCode} trong khung giờ dưới đây."
                : showConfirm
                    ? "Nhà hàng đã ghi nhận yêu cầu đặt bàn của bạn. Vui lòng bấm nút “Xác nhận đặt bàn” bên dưới để giữ bàn."
                    : "Nhà hàng đã ghi nhận yêu cầu đặt bàn của bạn. Vui lòng giữ lại mã đặt bàn dưới đây.";
        var note = rejected
            ? "Bạn có thể đặt bàn vào khung giờ khác hoặc gọi điện cho nhà hàng để được hỗ trợ. Mong sớm được đón tiếp bạn."
            : customerCancelled
            ? "Cảm ơn bạn đã báo trước cho nhà hàng. Khi muốn dùng bữa vào dịp khác, bạn có thể đặt bàn mới bất cứ lúc nào."
            : cancelled
            ? "Nếu bạn vẫn muốn dùng bữa, vui lòng đặt bàn mới hoặc gọi điện cho nhà hàng."
            : string.Empty;
        // Mã hoá ký tự HTML nguy hiểm nhưng giữ nguyên chữ tiếng Việt để email dễ đọc (kể cả khi xem mã nguồn).
        // Chuẩn hoá Unicode dạng dựng sẵn (NFC): dữ liệu nhập kiểu "tổ hợp" (ví dụ "Tầng" = "Tâ" + dấu huyền rời) hiện lỗi dấu trên nhiều phông.
        static string E(string? value) => Html.Encode((value ?? string.Empty).Normalize(System.Text.NormalizationForm.FormC));
        var when = FormatDateTime(d.StartsAtVietnam);
        var guestName = string.IsNullOrWhiteSpace(d.CustomerName) ? "quý khách" : d.CustomerName;
        var address = string.IsNullOrWhiteSpace(d.RestaurantAddress) ? "(nhà hàng sẽ liên hệ để báo địa chỉ)" : d.RestaurantAddress;
        // ---------- Thiệp mời: nền đỏ rượu vang, tờ giấy kem có con dấu sáp đỏ ở mép trên ----------
        // Bảng lồng + CSS nội dòng để hiện đúng trên Gmail/Outlook; chữ có chân có sẵn trên máy người đọc (không tải phông ngoài).
        const string Wine = "#3c0102", Paper = "#f5ede4", Ink = "#2b1a17", Muted = "#8a7770", Line = "#dccbbd";
        // Palatino / Times: chữ có chân đủ dấu tiếng Việt và chữ số thẳng hàng (Georgia dùng số kiểu cổ, "09:00" lên xuống không đều).
        const string Serif = "'Palatino Linotype','Book Antiqua',Palatino,'Times New Roman',Times,serif";
        const string Sans = "Arial,Helvetica,sans-serif";
        var eyelet = rejected ? "Thư báo từ nhà hàng" : closed ? "Thư báo huỷ đặt bàn" : "Thiệp xác nhận đặt bàn";
        string Row(string label, string valueHtml) =>
            $"<tr><td style=\"padding:11px 0;border-bottom:1px dotted {Line};font-family:{Sans};font-size:11px;letter-spacing:2px;text-transform:uppercase;color:{Muted};vertical-align:top;width:38%\">{label}</td>" +
            $"<td align=\"right\" style=\"padding:11px 0;border-bottom:1px dotted {Line};font-family:{Serif};font-size:17px;line-height:1.45;color:{Ink};vertical-align:top\">{valueHtml}</td></tr>";
        // Nút vuông màu đỏ thương hiệu, kèm liên kết dự phòng dạng chữ.
        string Button(string url, string label, string? hint) => $"""
            <tr><td align="center" style="padding:30px 0 4px">
              <table role="presentation" cellpadding="0" cellspacing="0"><tr><td bgcolor="{Brand}" style="background:{Brand}">
                <a href="{E(url)}" target="_blank" style="display:inline-block;padding:15px 36px;font-family:{Sans};font-size:13px;font-weight:700;letter-spacing:3px;text-transform:uppercase;color:#ffffff;text-decoration:none">{label}</a>
              </td></tr></table>
              {(hint is null ? "" : $"<div style=\"font-family:{Sans};font-size:11px;color:{Muted};margin-top:12px;line-height:1.6\">{hint}<br><a href=\"{E(url)}\" style=\"color:{Brand};word-break:break-all\">{E(url)}</a></div>")}
            </td></tr>
            """;

        var actionHtml = showConfirm
            ? Button(confirmUrl!, "Xác nhận đặt bàn", "Nút không bấm được? Mở liên kết sau trong trình duyệt:")
            : showLookup
                ? Button(lookupUrl!, "Tra cứu đặt bàn", null)
                : string.Empty;
        // Lượt đã huỷ / bị từ chối: mã đặt bàn hiện màu nhạt, gạch ngang.
        var codeStyle = closed
            ? $"color:{Muted};border:3px double {Line};text-decoration:line-through"
            : $"color:{BrandDark};border:3px double {Brand}";

        var html = $"""
            <!doctype html>
            <html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>{E(d.RestaurantName)} - {title} {E(d.Code)}</title></head>
            <body style="margin:0;padding:0;background:{Wine};color:{Ink}">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" bgcolor="{Wine}" style="background:{Wine}">
                <tr><td align="center" style="padding:32px 12px 36px">
                  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:520px">
                    <tr><td align="center" style="padding:0 0 22px">{EmailBranding.LogoImg}</td></tr>
                    <tr><td style="line-height:0;font-size:0">{EmailBranding.InviteTopImg}</td></tr>
                    <tr><td bgcolor="{Paper}"{EmailBranding.PaperBackgroundAttr} style="background-color:{Paper};{EmailBranding.PaperBackgroundCss}padding:4px 26px 34px">
                      <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border:1px solid {Line}"><tr><td style="padding:5px">
                      <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border:1px solid {Line}"><tr><td style="padding:30px 26px 32px">

                        <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
                          <tr><td align="center" style="font-family:{Sans};font-size:11px;font-weight:700;letter-spacing:4px;text-transform:uppercase;color:{Brand};padding:0 0 12px">{E(d.RestaurantName)} · {eyelet}</td></tr>
                          <tr><td align="center" style="font-family:{Serif};font-size:34px;font-style:italic;line-height:1.2;color:{Wine};padding:0 0 10px">{E(title)}</td></tr>
                          <tr><td align="center" style="font-family:{Serif};font-size:14px;letter-spacing:6px;color:{Brand};padding:0 0 22px">—&nbsp;✦&nbsp;—</td></tr>
                          <tr><td align="center" style="font-family:{Serif};font-size:17px;line-height:1.75;color:{Ink};padding:0 4px">
                            <p style="margin:0 0 10px;font-style:italic">Kính gửi <strong style="font-style:normal">{E(guestName)}</strong>,</p>
                            <p style="margin:0">{E(intro)}</p>
                          </td></tr>
                          <tr><td align="center" style="padding:26px 0 8px">
                            <div style="font-family:{Sans};font-size:11px;letter-spacing:2px;text-transform:uppercase;color:{Muted};margin-bottom:10px">Mã đặt bàn (dùng để tra cứu)</div>
                            <div style="display:inline-block;padding:12px 26px;background:#fffdf9;{codeStyle};font-family:{Serif};font-size:30px;letter-spacing:8px;-webkit-user-select:all;user-select:all">{E(d.Code)}</div>
                          </td></tr>
                          <tr><td style="padding:18px 0 0">
                            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border-top:1px solid {Line}">
                              {(d.TableCode is null ? "" : Row("Bàn", E(d.TableCode)))}
                              {Row("Ngày giờ", E(when))}
                              {Row("Số khách", E($"{d.GuestCount} người"))}
                              {(d.AreaName is null ? "" : Row("Khu vực", E(d.AreaName)))}
                              {Row("Địa chỉ quán", E(address))}
                              {(d.RestaurantPhone is null ? "" : Row("Điện thoại", E(d.RestaurantPhone)))}
                              {(cancelled && !customerCancelled && d.CancelReason is not null ? Row("Lý do huỷ", E(d.CancelReason)) : "")}
                              {(rejected && rejectionLabel is not null ? Row("Lý do", E(rejectionLabel)) : "")}
                            </table>
                          </td></tr>
                          {actionHtml}
                          {(string.IsNullOrEmpty(note) ? "" : $"<tr><td align=\"center\" style=\"padding:28px 6px 0;font-family:{Serif};font-size:15px;font-style:italic;line-height:1.7;color:{Muted}\">{E(note)}</td></tr>")}
                          <tr><td align="center" style="padding:26px 0 0">
                            <div style="font-family:{Serif};font-size:15px;font-style:italic;color:{Muted}">Trân trọng,</div>
                            <div style="font-family:{Serif};font-size:26px;font-style:italic;color:{Brand};margin-top:4px">{E(d.RestaurantName)}</div>
                          </td></tr>
                        </table>

                      </td></tr></table>
                      </td></tr></table>
                    </td></tr>
                    <tr><td align="center" style="padding:20px 12px 0;font-family:{Sans};font-size:11px;letter-spacing:1px;color:#c9a7a3">Email được gửi tự động, vui lòng không trả lời.</td></tr>
                  </table>
                </td></tr>
              </table>
            </body></html>
            """;

        var text = string.Join("\n", new[]
        {
            $"Xin chào {guestName},",
            "",
            cancelled && !customerCancelled ? $"{d.RestaurantName} rất tiếc phải thông báo lượt đặt bàn dưới đây đã bị huỷ." : intro,
            "",
            $"Mã đặt bàn:   {d.Code}",
            d.TableCode is null ? null : $"Bàn:          {d.TableCode}",
            $"Ngày giờ:     {when} (giờ Việt Nam)",
            $"Số khách:     {d.GuestCount} người",
            d.AreaName is null ? null : $"Khu vực:      {d.AreaName}",
            $"Địa chỉ quán: {address}",
            d.RestaurantPhone is null ? null : $"Điện thoại:   {d.RestaurantPhone}",
            cancelled && !customerCancelled && d.CancelReason is not null ? $"Lý do huỷ:    {d.CancelReason}" : null,
            rejected && rejectionLabel is not null ? $"Lý do:        {rejectionLabel}" : null,
            "",
            showConfirm ? $"Xác nhận đặt bàn: {confirmUrl}" : showLookup ? $"Tra cứu đặt bàn: {lookupUrl}" : null,
            showConfirm || showLookup ? "" : null,
            closed ? note : null,
            "Email được gửi tự động, vui lòng không trả lời."
        }.Where(line => line is not null));

        return new EmailMessage(to, Subject(d, messageType), html, text);
    }
}
