using System.Data;
using Microsoft.Data.SqlClient;

namespace RestaurantManagement.Web.Services;

public sealed record KitchenLine(long Id, long BatchId, string TableCode, string ItemName,
    int Quantity, string? Notes, string Status, string Version);

public sealed class KitchenStore(IConfiguration configuration)
{
    private SqlConnection Connection() => new(configuration.GetConnectionString("DefaultConnection"));

    public async Task<List<KitchenLine>> Read(int actorId, CancellationToken ct)
    {
        await using var connection = Connection();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            IF NOT EXISTS(SELECT 1 FROM dbo.Users u JOIN dbo.Roles r ON r.Id=u.RoleId
              WHERE u.Id=@ActorUserId AND u.IsActive=1 AND r.Code IN ('Kitchen','Waiter','Manager'))
              THROW 51001,N'Tài khoản không có quyền xem món.',1;
            SELECT i.Id,i.BatchId,t.Code,i.ItemName,i.Quantity,i.Notes,i.Status,i.RowVersion
            FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId
            JOIN dbo.DiningSessions s ON s.Id=b.SessionId
            JOIN dbo.DiningTables t ON t.Id=i.OriginalTableId
            WHERE i.Status IN ('Pending','Preparing','Ready') AND s.Status<>'Closed'
            ORDER BY CASE WHEN i.Status='Ready' THEN 1 ELSE 0 END,i.RowVersion,i.Id;
            """, connection);
        command.Parameters.Add("@ActorUserId", SqlDbType.Int).Value = actorId;
        var lines = new List<KitchenLine>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lines.Add(new(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3),
                reader.GetInt32(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetString(6),
                Convert.ToBase64String((byte[])reader[7])));
        return lines;
    }

    public async Task Transition(long id, string from, string to, byte[] version, int actorId, CancellationToken ct)
    {
        await using var connection = Connection();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("dbo.usp_KitchenLineTransition", connection) { CommandType = CommandType.StoredProcedure };
        command.Parameters.Add("@OrderItemId", SqlDbType.BigInt).Value = id;
        command.Parameters.Add("@FromStatus", SqlDbType.VarChar, 20).Value = from;
        command.Parameters.Add("@ToStatus", SqlDbType.VarChar, 20).Value = to;
        command.Parameters.Add("@ExpectedVersion", SqlDbType.Binary, 8).Value = version;
        command.Parameters.Add("@ActorUserId", SqlDbType.Int).Value = actorId;
        await command.ExecuteNonQueryAsync(ct);
    }
}
