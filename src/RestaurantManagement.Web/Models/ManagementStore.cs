using System.Data;
using Microsoft.Data.SqlClient;

namespace RestaurantManagement.Web.Models;

public sealed class ManagementStore(string connectionString)
{
    // Missing/inactive accounts still perform the same bcrypt work as valid accounts.
    private static readonly string DummyHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString(), 12);

    public async Task<LoginResult> Authenticate(string identifier, string password)
    {
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("dbo.usp_FindLogin", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.Add("@Identifier", SqlDbType.NVarChar, 50).Value = identifier.Trim();
        await using var reader = await cmd.ExecuteReaderAsync();
        LoginUser? user = null;
        string? hash = null;
        bool active = false;
        if (await reader.ReadAsync())
        {
            user = new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(5));
            hash = reader.IsDBNull(3) ? null : reader.GetString(3);
            active = reader.GetBoolean(4);
            // Reject ambiguous identifiers instead of signing in as an arbitrary account.
            if (await reader.ReadAsync()) { active = false; user = null; }
        }
        bool valid;
        try { valid = BCrypt.Net.BCrypt.Verify(password, hash ?? DummyHash); }
        catch (BCrypt.Net.SaltParseException) { BCrypt.Net.BCrypt.Verify(password, DummyHash); valid = false; }
        await reader.CloseAsync();
        await using var complete = new SqlCommand("dbo.usp_CompleteLogin", cn) { CommandType = CommandType.StoredProcedure };
        complete.Parameters.Add("@UserId", SqlDbType.Int).Value = (object?)user?.Id ?? DBNull.Value;
        complete.Parameters.Add("@Identifier", SqlDbType.NVarChar, 50).Value = identifier.Trim();
        complete.Parameters.Add("@PasswordValid", SqlDbType.Bit).Value = valid && active && hash is not null;
        complete.Parameters.Add("@ExpectedHash", SqlDbType.VarChar, 100).Value = (object?)hash ?? DBNull.Value;
        await using var decision = await complete.ExecuteReaderAsync();
        await decision.ReadAsync();
        return new(decision.GetBoolean(0) ? user : null, decision.GetInt32(1));
    }

    public async Task<ManagementMenu> GetMenu(int actorId)
    {
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("dbo.usp_ManagementMenu", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.Add("@ActorUserId", SqlDbType.Int).Value = actorId;
        await using var reader = await cmd.ExecuteReaderAsync();
        var items = new List<MenuItem>();
        var changes = new List<MenuChange>();
        while (await reader.ReadAsync()) items.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetDecimal(2), reader.GetBoolean(3)));
        await reader.NextResultAsync();
        while (await reader.ReadAsync()) changes.Add(new(reader.GetDateTime(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return new(items, changes);
    }

    public async Task ChangeMenu(int actorId, int itemId, decimal? price = null, bool? soldOut = null)
    {
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(price.HasValue ? "dbo.usp_UpdateMenuPrice" : "dbo.usp_SetMenuAvailability", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.Add("@ActorUserId", SqlDbType.Int).Value = actorId;
        cmd.Parameters.Add("@MenuItemId", SqlDbType.Int).Value = itemId;
        if (price.HasValue)
        {
            var p = cmd.Parameters.Add("@Price", SqlDbType.Decimal);
            p.Precision = 18; p.Scale = 0; p.Value = price.Value;
        }
        else cmd.Parameters.Add("@IsSoldOut", SqlDbType.Bit).Value = soldOut ?? throw new ArgumentException("Missing availability");
        await cmd.ExecuteNonQueryAsync();
    }
}
