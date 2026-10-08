using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models.Ordering;

namespace RestaurantManagement.Web.Services.Ordering;

/// <summary>Creates a guest order only for the serving session opened from a table QR code.</summary>
public sealed class GuestOrderService(IConfiguration configuration, IMenuStore menuStore)
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Chưa cấu hình kết nối cơ sở dữ liệu.");

    public async Task<GuestOrderContext> StartFromQrAsync(string qrToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(qrToken))
            throw new GuestOrderException("Hãy quét mã QR tại bàn để gọi món.");

        var guestToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand("dbo.usp_OpenGuestSession", connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        command.Parameters.Add("@QrTokenHash", SqlDbType.Binary, 32).Value = Hash(qrToken);
        command.Parameters.Add("@GuestTokenHash", SqlDbType.Binary, 32).Value = Hash(guestToken);
        await connection.OpenAsync(cancellationToken);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is null || value is DBNull)
            throw new GuestOrderException("Không mở được phiên gọi món cho bàn này. Vui lòng gọi nhân viên.");
        return new GuestOrderContext(Convert.ToInt64(value), guestToken);
    }

    public async Task<long> SubmitAsync(GuestOrderContext context, IReadOnlyList<GuestOrderLineInput> items,
        CancellationToken cancellationToken)
    {
        var validationError = GuestOrderRules.Validate(items);
        if (validationError is not null) throw new GuestOrderException(validationError);

        // Friendly validation names the dish before SQL repeats the authoritative availability check.
        var available = menuStore.GetPublicMenu().SelectMany(category => category.Dishes)
            .Where(dish => !dish.SoldOutToday).ToDictionary(dish => dish.Id, dish => dish.Name);
        var unavailable = items.FirstOrDefault(item => !available.ContainsKey(item.DishId));
        if (unavailable is not null)
            throw new GuestOrderException($"Món mã {unavailable.DishId} không còn bán hoặc không tồn tại. Vui lòng bỏ món này khỏi giỏ.");

        var payload = JsonSerializer.Serialize(items.Select(item => new
        {
            MenuItemId = item.DishId,
            item.Quantity,
            Notes = (string?)null
        }));

        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand("dbo.usp_SubmitOrder", connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        command.Parameters.Add("@SessionId", SqlDbType.BigInt).Value = context.SessionId;
        command.Parameters.Add("@RequestId", SqlDbType.UniqueIdentifier).Value = Guid.NewGuid();
        command.Parameters.Add("@ItemsJson", SqlDbType.NVarChar, -1).Value = payload;
        command.Parameters.Add("@ActorUserId", SqlDbType.Int).Value = DBNull.Value;
        command.Parameters.Add("@GuestTokenHash", SqlDbType.Binary, 32).Value = Hash(context.GuestToken);
        await connection.OpenAsync(cancellationToken);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is null || value is DBNull)
            throw new GuestOrderException("Không gửi được order. Vui lòng thử lại.");
        return Convert.ToInt64(value);
    }

    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
}

public sealed class GuestOrderException(string message) : Exception(message);
