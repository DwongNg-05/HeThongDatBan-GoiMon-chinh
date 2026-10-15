using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models;

namespace RestaurantManagement.Web.Services;

internal static class TableGroupReader
{
    internal static TableGroupInfo? Read(SqlDataReader reader,int index)
    {
        if(reader.IsDBNull(index)) return null;
        using var members=JsonDocument.Parse(reader.GetString(index+3));
        return new(reader.GetString(index),reader.GetInt64(index+1),reader.GetString(index+2),
            members.RootElement.EnumerateArray().Select(m=>m.GetProperty("code").GetString()!).ToArray(),reader.GetString(index+4));
    }

    internal static async Task<TableGroupInfo?> GetAsync(SqlConnection connection,string tableCode,CancellationToken ct)
    {
        await using var cmd=new SqlCommand("""
            SELECT g.GroupLabel,g.MainSessionId,g.MainTableCode,g.MemberCodesJson,g.GroupStatus
            FROM dbo.vw_ActiveTableGroups g JOIN dbo.DiningTables t ON t.Id=g.TableId WHERE t.Code=@code;
            """,connection);
        cmd.Parameters.Add("@code",SqlDbType.VarChar,20).Value=tableCode;
        await using var reader=await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader,0) : null;
    }
}
