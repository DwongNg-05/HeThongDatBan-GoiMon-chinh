using System.Data;
using Microsoft.Data.SqlClient;

namespace RestaurantManagement.Web.Models;

public sealed class PasswordChangeStore(string connectionString)
{
    private async Task<(string Hash, bool Required)?> State(int userId)
    {
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("dbo.usp_PasswordChangeState", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() && !reader.IsDBNull(0) ? (reader.GetString(0), reader.GetBoolean(1)) : null;
    }

    public async Task<bool> IsRequired(int userId) => (await State(userId))?.Required ?? true;

    public async Task<bool> Change(int userId, Guid sessionId, string currentPassword, string newPassword)
    {
        var state = await State(userId);
        if (state is null) return false;
        try { if (!BCrypt.Net.BCrypt.Verify(currentPassword, state.Value.Hash)) return false; }
        catch (BCrypt.Net.SaltParseException) { return false; }
        var hash = BCrypt.Net.BCrypt.HashPassword(newPassword, 12);
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("dbo.usp_ChangeOwnPassword", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
        cmd.Parameters.Add("@SessionId", SqlDbType.UniqueIdentifier).Value = sessionId;
        cmd.Parameters.Add("@ExpectedHash", SqlDbType.VarChar, 100).Value = state.Value.Hash;
        cmd.Parameters.Add("@NewHash", SqlDbType.VarChar, 100).Value = hash;
        return Convert.ToBoolean(await cmd.ExecuteScalarAsync());
    }
}
