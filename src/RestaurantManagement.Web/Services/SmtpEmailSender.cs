using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using RestaurantManagement.Web.Services.EmailVerification;

namespace RestaurantManagement.Web.Services
{
    public class CancellationSmtpEmailSender
    {
        private readonly SmtpOptions _options;
        private readonly IWebHostEnvironment _environment;

        public CancellationSmtpEmailSender(IOptions<SmtpOptions> options, IWebHostEnvironment environment)
        {
            _options = options.Value;
            _environment = environment;
        }

        public async Task SendAsync(
            string recipient,
            string subject,
            string body,
            CancellationToken cancellationToken = default)
        {
            var message = new MimeMessage();

            message.From.Add(
                new MailboxAddress(
                    _options.FromName,
                    _options.FromEmail));

            message.To.Add(
                MailboxAddress.Parse(recipient));

            message.Subject = subject;

            // Logo Bếp Nhà và ảnh thiệp mời (con dấu sáp, vân giấy) đính kèm dạng ảnh nhúng (cid:...);
            // ảnh nào thiếu tệp thì bỏ khỏi nội dung.
            var builder = new BodyBuilder();
            var images = EmailBranding.FindImages(body, _environment);
            builder.HtmlBody = EmailBranding.RemoveMissingImages(body, images.Select(i => i.Image.ContentId));
            foreach (var (inline, path) in images)
            {
                var image = builder.LinkedResources.Add(path);
                image.ContentId = inline.ContentId;
            }
            message.Body = builder.ToMessageBody();

            using var client = new SmtpClient();

            var socketOption = _options.UseSsl
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTlsWhenAvailable;

            await client.ConnectAsync(
                _options.Host,
                _options.Port,
                socketOption,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(_options.Username))
            {
                await client.AuthenticateAsync(
                    _options.Username,
                    _options.Password,
                    cancellationToken);
            }

            await client.SendAsync(
                message,
                cancellationToken);

            await client.DisconnectAsync(
                true,
                cancellationToken);
        }
    }
}