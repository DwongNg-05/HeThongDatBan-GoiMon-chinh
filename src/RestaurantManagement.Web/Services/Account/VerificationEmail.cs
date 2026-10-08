using System.Net;
using System.Globalization;

namespace RestaurantManagement.Web.Services.EmailVerification;

public sealed record EmailMessage(string To, string Subject, string HtmlBody, string TextBody);

/// <summary>Nội dung email chứa mã xác minh: mã to, rõ, tách từng ô; có bản chữ thường cho ứng dụng không hiển thị HTML.</summary>
public static class VerificationEmail
{
    public static EmailMessage Create(string to, string fullName, string code, int validMinutes, DateTime sentAtUtc)
    {
        var name = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(fullName) ? "bạn" : fullName);
        // Quote the separators so the Vietnamese display is stable on every server culture.
        var sentAt = sentAtUtc.AddHours(7).ToString("HH:mm 'ngày' dd'/'MM'/'yyyy", CultureInfo.InvariantCulture);
        var boxes = string.Concat(code.Select(c =>
            $"<td style=\"padding:0 4px\"><div style=\"width:46px;height:58px;line-height:58px;border:2px solid #c92d3e;border-radius:10px;" +
            $"background:#fff3f4;color:#1f1a1b;font-family:'Courier New',Consolas,monospace;font-size:34px;font-weight:700;text-align:center\">{c}</div></td>"));
        var html = $"""
            <!doctype html>
            <html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Mã xác minh đăng nhập</title></head>
            <body style="margin:0;padding:0;background:#f4f1f1;font-family:Arial,Helvetica,sans-serif;color:#30282a">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f4f1f1;padding:24px 12px">
                <tr><td align="center">
                  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:520px;background:#ffffff;border-radius:16px;overflow:hidden">
                    <tr><td style="background:#c92d3e;padding:18px 24px;color:#ffffff;font-size:20px;font-weight:700">Bếp Nhà · Hệ thống nhà hàng</td></tr>
                    <tr><td style="padding:28px 24px 8px;font-size:16px;line-height:1.6">
                      <p style="margin:0 0 12px">Xin chào <strong>{name}</strong>,</p>
                      <p style="margin:0 0 20px">Đây là <strong>mã xác minh đăng nhập</strong> của bạn. Hãy nhập mã này vào màn hình xác minh để tiếp tục:</p>
                    </td></tr>
                    <tr><td align="center" style="padding:0 24px 8px">
                      <table role="presentation" cellpadding="0" cellspacing="0"><tr>{boxes}</tr></table>
                    </td></tr>
                    <tr><td align="center" style="padding:4px 24px 20px;font-size:13px;color:#766b6e">Mã gồm {code.Length} ký tự: chữ in hoa và số · <span style="font-family:'Courier New',monospace;font-size:16px;color:#1f1a1b;letter-spacing:3px">{code}</span></td></tr>
                    <tr><td style="padding:0 24px 24px">
                      <div style="background:#fff8e5;border:1px solid #f2d68a;border-radius:10px;padding:12px 14px;font-size:15px;line-height:1.5">
                        ⏱ Mã có hiệu lực trong <strong>{validMinutes} phút</strong> (gửi lúc {sentAt}, giờ Việt Nam).<br>
                        🔒 Không chia sẻ mã này cho bất kỳ ai, kể cả người tự nhận là nhân viên nhà hàng.
                      </div>
                      <p style="margin:16px 0 0;font-size:14px;color:#766b6e;line-height:1.5">Nếu bạn không đăng nhập, hãy bỏ qua email này và báo cho quản lý để đổi mật khẩu.</p>
                    </td></tr>
                    <tr><td style="background:#faf7f7;padding:14px 24px;font-size:12px;color:#9a8f91">Email được gửi tự động, vui lòng không trả lời.</td></tr>
                  </table>
                </td></tr>
              </table>
            </body></html>
            """;
        var spaced = string.Join(" ", code.ToCharArray());
        var text = $"""
            Xin chào {(string.IsNullOrWhiteSpace(fullName) ? "bạn" : fullName)},

            Mã xác minh đăng nhập Bếp Nhà của bạn là:

                {code}      ({spaced})

            Mã gồm {code.Length} ký tự chữ in hoa và số, có hiệu lực trong {validMinutes} phút (gửi lúc {sentAt}, giờ Việt Nam).
            Không chia sẻ mã này cho bất kỳ ai.
            Nếu bạn không đăng nhập, hãy bỏ qua email này và báo cho quản lý.
            """;
        return new EmailMessage(to, $"Mã xác minh đăng nhập Bếp Nhà: {code}", html, text);
    }
}
