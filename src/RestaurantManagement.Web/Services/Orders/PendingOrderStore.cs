using System.Data;
using Microsoft.Data.SqlClient;

namespace RestaurantManagement.Web.Services;

public sealed record SentOrderLine(long Id, long SessionId, string TableCode, string ItemName,
    string Unit, int Quantity, decimal UnitPrice, string Status, string? CancelReason, decimal Subtotal)
{
    public string StatusLabel => Status switch
    {
        "Pending" => "Chờ bếp", "Preparing" => "Đang chế biến", "Ready" => "Chờ mang ra",
        "Served" => "Đã phục vụ", "Cancelled" => "Đã huỷ", _ => Status
    };
}

public sealed class PendingOrderStore(IConfiguration configuration)
{
    private string Connection => configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Chưa cấu hình kết nối database.");

    public async Task<IReadOnlyList<SentOrderLine>> List(CancellationToken ct)
    {
        await using var connection = new SqlConnection(Connection);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT i.Id,s.Id,t.Code,i.ItemName,i.Unit,i.Quantity,i.UnitPrice,i.Status,i.CancelReason,
              COALESCE((SELECT SUM(v.Subtotal) FROM dbo.vw_SessionTotals v
                WHERE v.BillingSessionId=COALESCE(s.BillingSessionId,s.Id)),0)
            FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId
            JOIN dbo.DiningSessions s ON s.Id=b.SessionId JOIN dbo.DiningTables t ON t.Id=i.OriginalTableId
            WHERE s.Status<>'Closed' ORDER BY t.Code,s.Id,
                CASE i.Status WHEN 'Pending' THEN 0 WHEN 'Preparing' THEN 1 WHEN 'Ready' THEN 2 WHEN 'Served' THEN 3 ELSE 4 END,i.Id DESC;
            """, connection);
        var lines = new List<SentOrderLine>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lines.Add(new(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetInt32(5), reader.GetDecimal(6), reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8), reader.GetDecimal(9)));
        return lines;
    }

    public async Task<bool> Cancel(long id, int quantity, string reason, int actor, Guid requestId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(Connection);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("dbo.usp_CancelPendingOrderItem", connection) { CommandType = CommandType.StoredProcedure };
        command.Parameters.Add("@OrderItemId", SqlDbType.BigInt).Value = id;
        command.Parameters.Add("@Quantity", SqlDbType.Int).Value = quantity;
        command.Parameters.Add("@Reason", SqlDbType.VarChar, 30).Value = reason;
        command.Parameters.Add("@ActorUserId", SqlDbType.Int).Value = actor;
        command.Parameters.Add("@RequestId", SqlDbType.UniqueIdentifier).Value = requestId;
        return (bool)(await command.ExecuteScalarAsync(ct))!;
    }
}
