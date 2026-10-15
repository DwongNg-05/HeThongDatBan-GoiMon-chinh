using System.Data;
using Microsoft.Data.SqlClient;

namespace RestaurantManagement.Web.Services;

public sealed record MergeCandidate(int TableId, string Code, long SessionId, string Version);
public sealed record MergeSelection(MergeCandidate Current, IReadOnlyList<MergeCandidate> Candidates);
public sealed record AdjacencyTable(int Id, string Code, string Area);
public sealed record AdjacencyPair(int TableId, int AdjacentTableId, string Code, string AdjacentCode);
public sealed record AdjacencyViewModel(IReadOnlyList<AdjacencyTable> Tables, IReadOnlyList<AdjacencyPair> Pairs);

public sealed class TableMergeService(IConfiguration configuration)
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Chưa cấu hình kết nối database.");

    public async Task<MergeSelection?> GetSelectionAsync(int tableId, CancellationToken ct)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        // One statement provides the current table and candidates from the same snapshot.
        await using var cmd = new SqlCommand("""
            SELECT t.Id,t.Code,s.Id,s.RowVersion
            FROM dbo.DiningTables t JOIN dbo.Areas a ON a.Id=t.AreaId
            JOIN dbo.SessionTables st ON st.TableId=t.Id AND st.ReleasedAt IS NULL
            JOIN dbo.DiningSessions s ON s.Id=st.SessionId
            WHERE t.IsActive=1 AND a.IsActive=1 AND t.Status='Serving'
              AND s.Status='Open' AND s.BillingSessionId IS NULL
              AND NOT EXISTS(SELECT 1 FROM dbo.DiningSessions x WHERE x.BillingSessionId=s.Id)
              AND (SELECT COUNT(*) FROM dbo.SessionTables x WHERE x.SessionId=s.Id AND x.ReleasedAt IS NULL)=1
              AND (t.Id=@table OR EXISTS(SELECT 1 FROM dbo.TableAdjacencies p
                   JOIN dbo.DiningTables c ON c.Id=@table AND c.AreaId=t.AreaId
                   WHERE (p.TableId=@table AND p.AdjacentTableId=t.Id) OR (p.AdjacentTableId=@table AND p.TableId=t.Id)))
            ORDER BY t.Code;
            """, cn);
        cmd.Parameters.Add("@table", SqlDbType.Int).Value=tableId;
        var entries = new List<MergeCandidate>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            entries.Add(new(reader.GetInt32(0),reader.GetString(1),reader.GetInt64(2),Convert.ToHexString((byte[])reader[3])));
        var current=entries.FirstOrDefault(x=>x.TableId==tableId);
        return current is null ? null : new(current,entries.Where(x=>x.TableId!=tableId).ToArray());
    }

    public async Task MergeAsync(long main, long child, string mainVersion, string childVersion, int actor, CancellationToken ct)
    {
        static byte[] Version(string value)
        {
            if (value is null || value.Length!=16 || !value.All(Uri.IsHexDigit))
                throw new ArgumentException("Thông tin xác nhận đã cũ. Vui lòng tải lại bàn.");
            return Convert.FromHexString(value);
        }
        var mv=Version(mainVersion); var cv=Version(childVersion);
        await using var cn = new SqlConnection(ConnectionString);
        await using var cmd = new SqlCommand("dbo.usp_MergeSessions",cn) { CommandType=CommandType.StoredProcedure };
        cmd.Parameters.Add("@MainSessionId",SqlDbType.BigInt).Value=main;
        cmd.Parameters.Add("@ChildSessionId",SqlDbType.BigInt).Value=child;
        cmd.Parameters.Add("@ActorUserId",SqlDbType.Int).Value=actor;
        cmd.Parameters.Add("@ExpectedMainVersion",SqlDbType.Binary,8).Value=mv;
        cmd.Parameters.Add("@ExpectedChildVersion",SqlDbType.Binary,8).Value=cv;
        await cn.OpenAsync(ct);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<AdjacencyViewModel> GetAdjacencyAsync(CancellationToken ct)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await using var cmd = new SqlCommand("""
            SELECT t.Id,t.Code,a.Name FROM dbo.DiningTables t JOIN dbo.Areas a ON a.Id=t.AreaId
            WHERE t.IsActive=1 AND a.IsActive=1 ORDER BY a.SortOrder,t.Code;
            SELECT p.TableId,p.AdjacentTableId,t.Code,b.Code FROM dbo.TableAdjacencies p
            JOIN dbo.DiningTables t ON t.Id=p.TableId JOIN dbo.DiningTables b ON b.Id=p.AdjacentTableId ORDER BY t.Code,b.Code;
            """,cn);
        await cn.OpenAsync(ct);
        await using var reader=await cmd.ExecuteReaderAsync(ct);
        var tables=new List<AdjacencyTable>(); var pairs=new List<AdjacencyPair>();
        while(await reader.ReadAsync(ct)) tables.Add(new(reader.GetInt32(0),reader.GetString(1),reader.GetString(2)));
        await reader.NextResultAsync(ct);
        while(await reader.ReadAsync(ct)) pairs.Add(new(reader.GetInt32(0),reader.GetInt32(1),reader.GetString(2),reader.GetString(3)));
        return new(tables,pairs);
    }

    public async Task SetAdjacencyAsync(int table, int adjacent, bool enabled, int actor, CancellationToken ct)
    {
        await using var cn=new SqlConnection(ConnectionString);
        await using var cmd=new SqlCommand("dbo.usp_SetTableAdjacency",cn) { CommandType=CommandType.StoredProcedure };
        cmd.Parameters.Add("@TableId",SqlDbType.Int).Value=table;
        cmd.Parameters.Add("@AdjacentTableId",SqlDbType.Int).Value=adjacent;
        cmd.Parameters.Add("@IsAdjacent",SqlDbType.Bit).Value=enabled;
        cmd.Parameters.Add("@ActorUserId",SqlDbType.Int).Value=actor;
        await cn.OpenAsync(ct); await cmd.ExecuteNonQueryAsync(ct);
    }
}
