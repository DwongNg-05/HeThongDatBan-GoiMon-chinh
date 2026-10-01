using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Options;

namespace RestaurantManagement.Web.Services.EmailVerification;

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// Gửi email qua SMTP (ví dụ Gmail: smtp.gmail.com, cổng 587, mật khẩu ứng dụng).
/// Khi chưa cấu hình Email:Host, email được lưu thành tệp .html/.txt trong thư mục PickupDirectory
/// để thử nghiệm trên máy (mở tệp .html bằng trình duyệt để xem đúng giao diện email).
/// </summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> options, IWebHostEnvironment environment, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.Host))
        {
            var dir = string.IsNullOrWhiteSpace(o.PickupDirectory)
                ? Path.Combine(environment.ContentRootPath, "App_Data", "emails")
                : Path.GetFullPath(o.PickupDirectory, environment.ContentRootPath);
            Directory.CreateDirectory(dir);
            var safeTo = string.Concat(message.To.Select(c => char.IsLetterOrDigit(c) || c is '.' or '@' or '-' or '_' ? c : '_'));
            var name = $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}"[..32] + "-" + safeTo;
            var header = $"To: {message.To}\nSubject: {message.Subject}\n\n";
            await File.WriteAllTextAsync(Path.Combine(dir, name + ".html"), message.HtmlBody, Encoding.UTF8, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(dir, name + ".txt"), header + message.TextBody, Encoding.UTF8, cancellationToken);
            logger.LogWarning("Chưa cấu hình SMTP (Email:Host). Email xác minh được lưu tại {Directory}.", dir);
            return;
        }

        var from = string.IsNullOrWhiteSpace(o.FromAddress) ? o.UserName : o.FromAddress;
        if (string.IsNullOrWhiteSpace(from)) throw new InvalidOperationException("Thiếu Email:FromAddress hoặc Email:UserName.");
        using var mail = new MailMessage
        {
            From = new MailAddress(from, o.FromName, Encoding.UTF8),
            Subject = message.Subject,
            SubjectEncoding = Encoding.UTF8,
            Body = message.TextBody,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = false
        };
        mail.To.Add(new MailAddress(message.To));
        mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.HtmlBody, Encoding.UTF8, "text/html"));
        using var client = new SmtpClient(o.Host, o.Port)
        {
            EnableSsl = o.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Credentials = string.IsNullOrEmpty(o.UserName) ? null : new NetworkCredential(o.UserName, o.Password)
        };
        await client.SendMailAsync(mail, cancellationToken);
    }
}
