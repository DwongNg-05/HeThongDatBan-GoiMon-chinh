using Microsoft.Data.SqlClient;
using System.Data;

namespace RestaurantManagement.Web.Services;

public record OrderWorkflowItem(long Id,long SessionId,string TableCode,string ItemName,int Quantity,decimal UnitPrice,
    decimal LineTotal,string Status,bool ChargeWhenCancelled,string SessionStatus);
public record OrderWorkflowSession(long Id,decimal Subtotal);
public record OrderSessionOption(long Id,string TableCode);
public record OrderWorkflowSnapshot(IReadOnlyList<OrderWorkflowItem> Items,IReadOnlyList<OrderWorkflowSession> Sessions);

public class OrderWorkflowStore(string connectionString)
{
    public async Task<IReadOnlyList<OrderSessionOption>> ActiveSessions(int actor)
    {
        await using var cn=new SqlConnection(connectionString);await cn.OpenAsync();
        await using var cmd=new SqlCommand("""
            EXEC dbo.usp_RequirePermission @actor,'Orders.Read';
            SELECT s.Id,t.Code FROM dbo.DiningSessions s JOIN dbo.SessionTables st ON st.SessionId=s.Id AND st.ReleasedAt IS NULL
            JOIN dbo.DiningTables t ON t.Id=st.TableId WHERE s.Status='Open' ORDER BY t.Code;
            """,cn);cmd.Parameters.AddWithValue("@actor",actor);
        var options=new List<OrderSessionOption>();await using var reader=await cmd.ExecuteReaderAsync();
        while(await reader.ReadAsync()) options.Add(new(reader.GetInt64(0),reader.GetString(1)));return options;
    }
    public async Task Submit(long sessionId,Guid requestId,string itemsJson,int actor)
    {
        await using var cn=new SqlConnection(connectionString);await cn.OpenAsync();
        await using var cmd=new SqlCommand("dbo.usp_SubmitOrder",cn){CommandType=CommandType.StoredProcedure};
        cmd.Parameters.AddWithValue("@SessionId",sessionId);cmd.Parameters.AddWithValue("@RequestId",requestId);
        cmd.Parameters.AddWithValue("@ItemsJson",itemsJson);cmd.Parameters.AddWithValue("@ActorUserId",actor);
        await cmd.ExecuteNonQueryAsync();
    }
    public static bool ValidReason(string? reason) => reason is "ChangedMind" or "Mistake" or "SoldOut";
    public async Task<OrderWorkflowSnapshot> Read(int actor, bool kitchen, CancellationToken ct)
    {
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("""
            EXEC dbo.usp_RequirePermission @actor,@permission;
            SELECT i.Id,b.SessionId,t.Code,i.ItemName,i.Quantity,i.UnitPrice,i.LineTotal,i.Status,i.ChargeWhenCancelled,s.Status
            FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId
            JOIN dbo.DiningSessions s ON s.Id=b.SessionId JOIN dbo.DiningTables t ON t.Id=i.OriginalTableId
            WHERE s.Status<>'Closed' AND (@kitchen=0 OR i.Status IN ('Pending','Preparing','Ready'))
            ORDER BY i.SubmittedAt,i.Id;
            """, cn);
        cmd.Parameters.AddWithValue("@actor",actor);
        cmd.Parameters.AddWithValue("@permission",kitchen ? "Kitchen.Read" : "Orders.Read");
        cmd.Parameters.AddWithValue("@kitchen",kitchen);
        var items = new List<OrderWorkflowItem>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) items.Add(new(reader.GetInt64(0),reader.GetInt64(1),reader.GetString(2),reader.GetString(3),
            reader.GetInt32(4),reader.GetDecimal(5),reader.GetDecimal(6),reader.GetString(7),reader.GetBoolean(8),reader.GetString(9)));
        return new(items,items.GroupBy(i => i.SessionId).Select(g => new OrderWorkflowSession(g.Key,
            g.Where(i => i.Status!="Cancelled" || i.ChargeWhenCancelled).Sum(i => i.LineTotal))).ToList());
    }
    public async Task<bool> Cancel(long id,string reason,int actor,CancellationToken ct)
    {
        await using var cn = new SqlConnection(connectionString); await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("dbo.usp_CancelPendingOrderItem",cn) { CommandType=CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@OrderItemId",id); cmd.Parameters.AddWithValue("@Reason",reason); cmd.Parameters.AddWithValue("@ActorUserId",actor);
        return Convert.ToBoolean(await cmd.ExecuteScalarAsync(ct));
    }
    public async Task Start(long id,int actor,CancellationToken ct)
    {
        await using var cn = new SqlConnection(connectionString); await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("dbo.usp_TransitionOrderItem",cn) { CommandType=CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@OrderItemId",id); cmd.Parameters.AddWithValue("@ToStatus","Preparing"); cmd.Parameters.AddWithValue("@ActorUserId",actor);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
