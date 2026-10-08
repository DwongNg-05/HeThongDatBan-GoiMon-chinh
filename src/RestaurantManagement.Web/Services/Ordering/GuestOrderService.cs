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

    public async Task<GuestOrderReceipt> SubmitAsync(GuestOrderContext context, IReadOnlyList<GuestOrderLineInput> items,
        CancellationToken cancellationToken)
    {
        var validationError = GuestOrderRules.Validate(items);
        if (validationError is not null) throw new GuestOrderException(validationError);

        // Friendly validation names the dish before SQL repeats the authoritative availability check.
        var available = menuStore.GetPublicMenu().SelectMany(category => category.Dishes)
            .Where(dish => !dish.SoldOutToday).ToDictionary(dish => dish.Id);
        var unavailable = items.FirstOrDefault(item => !available.ContainsKey(item.DishId));
        if (unavailable is not null)
            throw new GuestOrderException($"Món mã {unavailable.DishId} không còn bán hoặc không tồn tại. Vui lòng bỏ món này khỏi giỏ.");

        // PO rule: the server uses the current listed price. A stale cart is not
        // silently accepted; the customer sees every changed price and can send again.
        var priceChanges = items.Where(item => item.ObservedPriceVnd <= 0 || available[item.DishId].PriceVnd != item.ObservedPriceVnd)
            .Select(item => new GuestOrderPriceChange(item.DishId, available[item.DishId].Name,
                item.ObservedPriceVnd, available[item.DishId].PriceVnd)).ToArray();
        if (priceChanges.Length > 0) throw new GuestOrderPriceChangedException(priceChanges);

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
        return await ReadReceiptAsync(connection, Convert.ToInt64(value), context.SessionId, cancellationToken);
    }

    private static async Task<GuestOrderReceipt> ReadReceiptAsync(SqlConnection connection, long batchId, long sessionId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT b.Id,b.BatchNumber,t.Code
            FROM dbo.OrderBatches b
            JOIN dbo.SessionTables st ON st.SessionId=b.SessionId AND st.ReleasedAt IS NULL
            JOIN dbo.DiningTables t ON t.Id=st.TableId
            WHERE b.Id=@BatchId AND b.SessionId=@SessionId;
            SELECT i.ItemName,i.Unit,CAST(i.UnitPrice AS int),i.Quantity,CAST(i.LineTotal AS bigint)
            FROM dbo.OrderItems i WHERE i.BatchId=@BatchId ORDER BY i.Id;
            """, connection);
        command.Parameters.Add("@BatchId", SqlDbType.BigInt).Value = batchId;
        command.Parameters.Add("@SessionId", SqlDbType.BigInt).Value = sessionId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new GuestOrderException("Không tìm thấy order vừa gửi.");
        var receiptBatchId = reader.GetInt64(0);
        var batchNumber = reader.GetInt32(1);
        var tableCode = reader.GetString(2);
        await reader.NextResultAsync(cancellationToken);
        var lines = new List<GuestOrderReceiptLine>();
        while (await reader.ReadAsync(cancellationToken))
            lines.Add(new GuestOrderReceiptLine(reader.GetString(0), reader.GetString(1), reader.GetInt32(2),
                reader.GetInt32(3), reader.GetInt64(4)));
        return new GuestOrderReceipt(receiptBatchId, batchNumber, tableCode, lines);
    }

    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
}

public class GuestOrderException(string message) : Exception(message);

public sealed class GuestOrderPriceChangedException(IReadOnlyList<GuestOrderPriceChange> changes)
    : GuestOrderException("Giá một số món đã thay đổi. Giỏ đã được cập nhật theo giá mới.")
{
    public IReadOnlyList<GuestOrderPriceChange> Changes { get; } = changes;
}
