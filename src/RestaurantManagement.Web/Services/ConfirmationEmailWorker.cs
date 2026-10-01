using System.Data;
using System.Net;
using System.Net.Mail;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models.Reservations;

namespace RestaurantManagement.Web.Services;

public sealed record ConfirmationEmailPayload(string Code, string CustomerName, int GuestCount,
    DateTime StartsAt, DateTime EndsAt, string TableCode);
public interface IConfirmationEmailSender
{
    Task Send(string recipient, string subject, ConfirmationEmailPayload payload, CancellationToken ct);
}
public sealed class SmtpConfirmationEmailSender(IConfiguration configuration) : IConfirmationEmailSender
{
    public static string Body(ConfirmationEmailPayload p) =>
        $"Xin chào {p.CustomerName},\nLượt đặt {p.Code} đã được xác nhận.\nBàn: {p.TableCode}\nSố khách: {p.GuestCount}\n" +
        $"Giờ hẹn: {VietnamTime.FromUtc(p.StartsAt):dd/MM/yyyy HH:mm}\n" +
        $"Giữ bàn đến: {VietnamTime.FromUtc(p.EndsAt):dd/MM/yyyy HH:mm} (giờ Việt Nam).\nCảm ơn quý khách.";
    public async Task Send(string recipient, string subject, ConfirmationEmailPayload payload, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(payload.TableCode) || string.IsNullOrWhiteSpace(payload.Code))
            throw new InvalidOperationException("Confirmation payload is incomplete.");
        var settings = configuration.GetSection("ConfirmationEmail");
        var host = settings["Host"];
        var from = settings["From"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from))
            throw new InvalidOperationException("SMTP configuration is missing.");
        using var client = new SmtpClient(host, settings.GetValue("Port", 587))
        {
            EnableSsl = settings.GetValue("EnableSsl", true),
            UseDefaultCredentials = false
        };
        if (!string.IsNullOrWhiteSpace(settings["Username"]))
            client.Credentials = new NetworkCredential(settings["Username"], settings["Password"]);
        using var message = new MailMessage(from, recipient, subject, Body(payload));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await client.SendMailAsync(message, timeout.Token);
    }
}

// A durable outbox is committed with the reservation; delivery happens after that transaction.
public sealed class ConfirmationEmailDispatcher(IConfiguration configuration, IConfirmationEmailSender sender)
{
    public async Task<bool> DispatchOne(CancellationToken ct)
    {
        await using var cn = new SqlConnection(configuration.GetConnectionString("EmailWorker")
            ?? configuration.GetConnectionString("DefaultConnection"));
        await cn.OpenAsync(ct);
        long id; int attempt; string recipient, subject, json;
        await using (var claim = new SqlCommand("dbo.usp_ClaimConfirmationEmail", cn) { CommandType = CommandType.StoredProcedure })
        await using (var reader = await claim.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) return false;
            id=reader.GetInt64(0); attempt=reader.GetInt32(1); recipient=reader.GetString(2);
            subject=reader.GetString(3); json=reader.GetString(4);
        }
        string? error = null;
        try
        {
            var payload = JsonSerializer.Deserialize<ConfirmationEmailPayload>(json)
                ?? throw new InvalidOperationException("Missing email payload.");
            await sender.Send(recipient, subject, payload, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is SmtpException or FormatException or InvalidOperationException or JsonException or OperationCanceledException)
        {
            // Do not persist SMTP credentials, server diagnostics, or customer data in error messages.
            error = "Không gửi được email xác nhận. Vui lòng kiểm tra cấu hình email và địa chỉ người nhận.";
        }
        await using var complete = new SqlCommand("dbo.usp_CompleteEmail", cn) { CommandType=CommandType.StoredProcedure };
        complete.Parameters.Add("@EmailId",SqlDbType.BigInt).Value=id;
        complete.Parameters.Add("@AttemptNumber",SqlDbType.Int).Value=attempt;
        complete.Parameters.Add("@Succeeded",SqlDbType.Bit).Value=error is null;
        complete.Parameters.Add("@Error",SqlDbType.NVarChar,2000).Value=(object?)error??DBNull.Value;
        await complete.ExecuteNonQueryAsync(ct);
        return true;
    }
}
public sealed class ConfirmationEmailWorker(IServiceScopeFactory scopes, IConfiguration configuration,
    ILogger<ConfirmationEmailWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue<bool>("ConfirmationEmail:Enabled")) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                if (await scope.ServiceProvider.GetRequiredService<ConfirmationEmailDispatcher>().DispatchOne(stoppingToken)) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                logger.LogWarning("Confirmation email worker failed ({ErrorType}); will retry.", ex.GetType().Name);
            }
            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}
