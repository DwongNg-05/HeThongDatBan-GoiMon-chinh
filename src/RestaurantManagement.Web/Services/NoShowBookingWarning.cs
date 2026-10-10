using System.Data;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models.Reservations;

namespace RestaurantManagement.Web.Services;

public sealed class NoShowBookingWarning(IConfiguration configuration, IDataProtectionProvider protection)
{
    private readonly IDataProtector protector = protection.CreateProtector("Reservation.NoShowWarning.v1");
    private sealed record Receipt(string Phone, int Count, DateTime ExpiresAt);

    public async Task Refresh(NoShowWarningInput model, string phone, CancellationToken ct = default)
    {
        var normalized = ReservationPhoneNormalizer.Normalize(phone);
        model.NoShowCount = 0;
        model.NoShowAcknowledged = false;
        model.NoShowWarningToken = null;
        if (normalized is null) return;
        await using var cn = new SqlConnection(configuration.GetConnectionString("DefaultConnection"));
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT dbo.fn_RecentNoShowCount(@phone,SYSUTCDATETIME());", cn);
        cmd.Parameters.Add("@phone", SqlDbType.NVarChar, 100).Value = normalized;
        model.NoShowCount = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        if (model.NoShowCount >= 3)
            model.NoShowWarningToken = protector.Protect(JsonSerializer.Serialize(new Receipt(normalized, model.NoShowCount, DateTime.UtcNow.AddHours(1))));
    }

    public int AcknowledgedCount(NoShowWarningInput model, string phone)
    {
        if (!model.NoShowAcknowledged || string.IsNullOrEmpty(model.NoShowWarningToken)) return -1;
        try
        {
            var receipt = JsonSerializer.Deserialize<Receipt>(protector.Unprotect(model.NoShowWarningToken));
            return receipt is not null && receipt.Phone == ReservationPhoneNormalizer.Normalize(phone)
                && receipt.ExpiresAt > DateTime.UtcNow ? receipt.Count : -1;
        }
        catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or JsonException or FormatException)
        { return -1; }
    }
}
