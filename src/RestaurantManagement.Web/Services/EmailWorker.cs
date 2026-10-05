using System.Data;
using System.Net;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace RestaurantManagement.Web.Services
{
    public class EmailWorker : BackgroundService
    {
        private readonly IConfiguration _configuration;
        private readonly CancellationSmtpEmailSender _emailSender;
        private readonly ILogger<EmailWorker> _logger;

        public EmailWorker(
            IConfiguration configuration,
            CancellationSmtpEmailSender emailSender,
            ILogger<EmailWorker> logger)
        {
            _configuration = configuration;
            _emailSender = emailSender;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(
            CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var email = await ClaimEmailAsync(stoppingToken);

                    if (email is null)
                    {
                        await Task.Delay(
                            TimeSpan.FromSeconds(5),
                            stoppingToken);

                        continue;
                    }

                    try
                    {
                        var body = BuildEmailBody(email);

                        await _emailSender.SendAsync(
                            email.Recipient,
                            BuildSubject(email),
                            body,
                            stoppingToken);

                        await CompleteEmailAsync(
                            email.Id,
                            email.AttemptCount,
                            true,
                            null,
                            stoppingToken);

                        _logger.LogInformation(
                            "Email {EmailId} sent successfully.",
                            email.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "Failed to send email {EmailId}.",
                            email.Id);

                        await CompleteEmailAsync(
                            email.Id,
                            email.AttemptCount,
                            false,
                            ex.Message,
                            stoppingToken);
                    }
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Email worker error.");

                    await Task.Delay(
                        TimeSpan.FromSeconds(10),
                        stoppingToken);
                }
            }
        }

        private async Task<QueuedEmail?> ClaimEmailAsync(
            CancellationToken cancellationToken)
        {
            var connectionString =
                _configuration.GetConnectionString(
                    "DefaultConnection");

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "DefaultConnection is not configured.");
            }

            await using var connection =
                new SqlConnection(connectionString);

            await connection.OpenAsync(cancellationToken);

            await using var command =
                new SqlCommand(
                    "dbo.usp_ClaimEmail",
                    connection);

            command.CommandType =
                CommandType.StoredProcedure;

            await using var reader =
                await command.ExecuteReaderAsync(
                    cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
                return null;

            return new QueuedEmail
            {
                Id = reader.GetInt64(
                    reader.GetOrdinal("Id")),

                AttemptCount = reader.GetInt32(
                    reader.GetOrdinal("AttemptCount")),

                Recipient = reader.GetString(
                    reader.GetOrdinal("Recipient")),

                Subject = reader.GetString(
                    reader.GetOrdinal("Subject")),

                PayloadJson = reader.GetString(
                    reader.GetOrdinal("PayloadJson")),

                MessageType = reader.GetString(
                    reader.GetOrdinal("MessageType"))
            };
        }

        private async Task CompleteEmailAsync(
            long emailId,
            int attemptNumber,
            bool succeeded,
            string? error,
            CancellationToken cancellationToken)
        {
            var connectionString =
                _configuration.GetConnectionString(
                    "DefaultConnection");

            await using var connection =
                new SqlConnection(connectionString);

            await connection.OpenAsync(cancellationToken);

            await using var command =
                new SqlCommand(
                    "dbo.usp_CompleteEmail",
                    connection);

            command.CommandType =
                CommandType.StoredProcedure;

            command.Parameters.AddWithValue(
                "@EmailId",
                emailId);

            command.Parameters.AddWithValue(
                "@AttemptNumber",
                attemptNumber);

            command.Parameters.AddWithValue(
                "@Succeeded",
                succeeded);

            command.Parameters.AddWithValue(
                "@Error",
                (object?)error ?? DBNull.Value);

            await command.ExecuteNonQueryAsync(
                cancellationToken);
        }

        private static string BuildSubject(
            QueuedEmail email)
        {
            if (email.MessageType ==
                "BookingCancelled")
            {
                return "Xác nhận huỷ đặt bàn";
            }

            return email.Subject;
        }

        private static string BuildEmailBody(
            QueuedEmail email)
        {
            using var document =
                JsonDocument.Parse(email.PayloadJson);

            var root = document.RootElement;

            var code =
                GetString(root, "Code");

            var customerName =
                GetString(root, "CustomerName");

            var guestCount =
                GetInt(root, "GuestCount");

            var areaName =
                GetString(root, "AreaName");

            var startLabel =
                GetStartTimeLabel(root);

            if (email.MessageType ==
                "BookingCancelled")
            {
                return $"""
                    <h2>Xác nhận huỷ đặt bàn</h2>

                    <p>
                        Xin chào {Html(customerName)},
                    </p>

                    <p>
                        Đặt bàn của quý khách đã được
                        huỷ thành công.
                    </p>

                    <p>
                        <strong>Mã đặt bàn:</strong>
                        {Html(code)}
                        <br />

                        <strong>Thời gian:</strong>
                        {Html(startLabel)}
                        <br />

                        <strong>Số khách:</strong>
                        {guestCount}
                        <br />

                        <strong>Khu vực:</strong>
                        {Html(
                            string.IsNullOrWhiteSpace(areaName)
                                ? "Chưa xác định"
                                : areaName)}
                    </p>

                    <p>
                        Cảm ơn quý khách.
                    </p>
                    """;
            }

            return $"""
                <h2>{Html(email.Subject)}</h2>

                <p>
                    Mã đặt bàn:
                    <strong>{Html(code)}</strong>
                </p>
                """;
        }

        private static string GetString(
            JsonElement root,
            string propertyName)
        {
            if (!root.TryGetProperty(
                    propertyName,
                    out var value))
            {
                return "";
            }

            if (value.ValueKind ==
                JsonValueKind.Null)
            {
                return "";
            }

            return value.ToString();
        }

        private static int GetInt(
            JsonElement root,
            string propertyName)
        {
            if (!root.TryGetProperty(
                    propertyName,
                    out var value))
            {
                return 0;
            }

            if (value.TryGetInt32(out var result))
                return result;

            return 0;
        }

        private static string GetStartTimeLabel(
            JsonElement root)
        {
            var value =
                GetString(root, "StartsAt");

            if (!DateTime.TryParse(
                    value,
                    out var startsAt))
            {
                return value;
            }

            // StartsAt trong database đang lưu UTC.
            var vietnamTime =
                startsAt.AddHours(7);

            return vietnamTime.ToString(
                "HH:mm 'ngày' dd/MM/yyyy");
        }

        private static string Html(string? value)
        {
            return WebUtility.HtmlEncode(
                value ?? "");
        }

        private sealed class QueuedEmail
        {
            public long Id { get; init; }

            public int AttemptCount { get; init; }

            public string Recipient { get; init; }
                = "";

            public string Subject { get; init; }
                = "";

            public string PayloadJson { get; init; }
                = "";

            public string MessageType { get; init; }
                = "";
        }
    }
}