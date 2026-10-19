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

            // Logo Bếp Nhà đính kèm dạng ảnh nhúng (cid:bepnha-logo); không có tệp logo thì bỏ thẻ ảnh.
            var builder = new BodyBuilder();
            var logo = body.Contains(EmailBranding.LogoImg) ? EmailBranding.FindLogo(_environment) : null;
            if (logo is null)
            {
                builder.HtmlBody = EmailBranding.RemoveLogo(body);
            }
            else
            {
                builder.HtmlBody = body;
                var image = builder.LinkedResources.Add(logo);
                image.ContentId = EmailBranding.LogoContentId;
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