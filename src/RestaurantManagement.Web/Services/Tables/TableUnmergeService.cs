using System.Data;
using Microsoft.Data.SqlClient;

namespace RestaurantManagement.Web.Services;

public sealed record UnmergeMember(string Code,long SessionId,decimal Subtotal);
public sealed record UnmergePreview(long MergeId,long ChildSessionId,string MainVersion,string ChildVersion,
    string Label,string Status,IReadOnlyList<UnmergeMember> Members);

public sealed class TableUnmergeService(IConfiguration configuration)
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Chưa cấu hình kết nối database.");

    public async Task<UnmergePreview?> PreviewAsync(int tableId,CancellationToken ct)
    {
        await using var cn=new SqlConnection(ConnectionString);
        await using var cmd=new SqlCommand("""
            SELECT m.Id,child.Id,root.RowVersion,child.RowVersion,root.TableGroupLabel,root.Status,
                   t.Code,member.Id,COALESCE(total.Subtotal,0)
            FROM dbo.SessionTables selected
            JOIN dbo.DiningSessions currentSession ON currentSession.Id=selected.SessionId
            JOIN dbo.DiningSessions root ON root.Id=COALESCE(currentSession.BillingSessionId,currentSession.Id)
            JOIN dbo.DiningSessions child ON child.BillingSessionId=root.Id
            JOIN dbo.SessionMerges m ON m.MainSessionId=root.Id AND m.ChildSessionId=child.Id AND m.UndoneAt IS NULL
            JOIN dbo.DiningSessions member ON member.Id IN(root.Id,child.Id)
            JOIN dbo.SessionTables st ON st.SessionId=member.Id AND st.ReleasedAt IS NULL
            JOIN dbo.DiningTables t ON t.Id=st.TableId
            OUTER APPLY(SELECT SUM(CASE WHEN i.Status<>'Cancelled' OR i.ChargeWhenCancelled=1 THEN i.LineTotal ELSE 0 END) Subtotal
              FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId
              WHERE b.SessionId IN(root.Id,child.Id) AND i.OriginalTableId=t.Id) total
            WHERE selected.TableId=@table AND selected.ReleasedAt IS NULL
              AND root.Status IN('Open','AwaitingPayment') AND child.Status IN('Open','AwaitingPayment')
              AND root.TableGroupLabel IS NOT NULL
            ORDER BY t.Code;
            """,cn);
        cmd.Parameters.Add("@table",SqlDbType.Int).Value=tableId;
        await cn.OpenAsync(ct);
        await using var reader=await cmd.ExecuteReaderAsync(ct);
        var members=new List<UnmergeMember>();
        UnmergePreview? preview=null;
        while(await reader.ReadAsync(ct))
        {
            preview ??= new(reader.GetInt64(0),reader.GetInt64(1),Convert.ToHexString((byte[])reader[2]),
                Convert.ToHexString((byte[])reader[3]),reader.GetString(4),reader.GetString(5),members);
            members.Add(new(reader.GetString(6),reader.GetInt64(7),reader.GetDecimal(8)));
        }
        return members.Count==2 ? preview : null;
    }

    public async Task UnmergeAsync(int tableId,long child,long merge,string mainVersion,string childVersion,int actor,CancellationToken ct)
    {
        static byte[] Version(string value)
        {
            if(value is null || value.Length!=16 || !value.All(Uri.IsHexDigit))
                throw new ArgumentException("Thông tin nhóm không hợp lệ. Vui lòng tải lại để xác nhận.");
            return Convert.FromHexString(value);
        }
        var mv=Version(mainVersion); var cv=Version(childVersion);
        if(tableId<=0 || child<=0 || merge<=0) throw new ArgumentException("Thông tin nhóm không hợp lệ. Vui lòng tải lại.");
        await using var cn=new SqlConnection(ConnectionString);
        await using var cmd=new SqlCommand("dbo.usp_UnmergeSession",cn) { CommandType=CommandType.StoredProcedure };
        cmd.Parameters.Add("@ChildSessionId",SqlDbType.BigInt).Value=child;
        cmd.Parameters.Add("@ActorUserId",SqlDbType.Int).Value=actor;
        cmd.Parameters.Add("@ExpectedMergeId",SqlDbType.BigInt).Value=merge;
        cmd.Parameters.Add("@ExpectedMainVersion",SqlDbType.Binary,8).Value=mv;
        cmd.Parameters.Add("@ExpectedChildVersion",SqlDbType.Binary,8).Value=cv;
        cmd.Parameters.Add("@ExpectedTableId",SqlDbType.Int).Value=tableId;
        await cn.OpenAsync(ct); await cmd.ExecuteNonQueryAsync(ct);
    }
}
