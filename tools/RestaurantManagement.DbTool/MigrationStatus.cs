using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;

namespace RestaurantManagement.DbTool;

internal static partial class DatabaseTool
{
    // Read-only: use before updating an existing database, including when updates need approval.
    internal static async Task MigrationStatus(string connection)
    {
        await using var cn = new SqlConnection(connection);
        await cn.OpenAsync();
        Console.WriteLine($"Server: {cn.DataSource}; Database: {cn.Database}");
        var applied = new Dictionary<string,string>(StringComparer.Ordinal);
        await using (var cmd = new SqlCommand("""
            IF OBJECT_ID('dbo.SchemaVersions') IS NOT NULL SELECT Name,Sha256 FROM dbo.SchemaVersions;
            ELSE SELECT CAST(NULL AS nvarchar(200)) AS Name,CAST(NULL AS char(64)) AS Sha256 WHERE 1=0;
            """,cn))
        await using (var reader = await cmd.ExecuteReaderAsync())
            while(await reader.ReadAsync()) applied.Add(reader.GetString(0),reader.GetString(1));
        var pending=0; var changed=0;
        foreach(var path in Directory.GetFiles(Path.Combine(Root,"database","migrations"),"*.sql").Order(StringComparer.Ordinal))
        {
            var name=Path.GetFileName(path);
            var sql=(await File.ReadAllTextAsync(path)).Replace("\r\n","\n");
            var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql)));
            if(!applied.TryGetValue(name,out var saved)) { pending++; Console.WriteLine("Pending: "+name); }
            else if(saved!=hash) { changed++; Console.WriteLine("Changed applied file: "+name); }
        }
        await using(var cmd=new SqlCommand("""
            SELECT 'OrderItems.QuickNotes',CASE WHEN COL_LENGTH('dbo.OrderItems','QuickNotes') IS NULL THEN 0 ELSE 1 END
            UNION ALL SELECT 'OrderBatches.OriginSessionId',CASE WHEN COL_LENGTH('dbo.OrderBatches','OriginSessionId') IS NULL THEN 0 ELSE 1 END
            UNION ALL SELECT 'TableAdjacencies',CASE WHEN OBJECT_ID('dbo.TableAdjacencies') IS NULL THEN 0 ELSE 1 END
            UNION ALL SELECT 'DiningSessions.TableGroupLabel',CASE WHEN COL_LENGTH('dbo.DiningSessions','TableGroupLabel') IS NULL THEN 0 ELSE 1 END
            UNION ALL SELECT 'vw_ActiveTableGroups',CASE WHEN OBJECT_ID('dbo.vw_ActiveTableGroups','V') IS NULL THEN 0 ELSE 1 END
            UNION ALL SELECT 'OrderBatches.SplitFromBatchId',CASE WHEN COL_LENGTH('dbo.OrderBatches','SplitFromBatchId') IS NULL THEN 0 ELSE 1 END;
            """,cn))
        await using(var reader=await cmd.ExecuteReaderAsync())
            while(await reader.ReadAsync()) Console.WriteLine($"{reader.GetString(0)}: {(reader.GetInt32(1)==1 ? "present" : "MISSING")}");
        Console.WriteLine($"Pending migrations: {pending}; Changed applied migrations: {changed}. No database changes made.");
        if(changed>0) Environment.ExitCode=1;
    }
}
