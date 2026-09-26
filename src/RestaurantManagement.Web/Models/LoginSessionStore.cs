using System.Data;
using System.Security.Claims;
using Microsoft.Data.SqlClient;

namespace RestaurantManagement.Web.Models;

public sealed class LoginSessionStore(string connectionString)
{
    public const string SessionClaim = "LoginSessionId";

    public async Task<Guid> Create(int userId)
    {
        var id = Guid.NewGuid();
        await Execute("dbo.usp_CreateLoginSession", id, userId);
        return id;
    }

    public async Task<bool> Check(ClaimsPrincipal user, bool touch = false)
    {
        if (!TryIdentity(user, out var id, out var userId)) return false;
        return Convert.ToBoolean(await Execute("dbo.usp_CheckLoginSession", id, userId, touch));
    }

    public async Task Revoke(ClaimsPrincipal user)
    {
        if (TryIdentity(user, out var id, out var userId))
            await Execute("dbo.usp_RevokeLoginSession", id, userId);
    }

    private static bool TryIdentity(ClaimsPrincipal user, out Guid id, out int userId)
    {
        userId = 0;
        return Guid.TryParse(user.FindFirstValue(SessionClaim), out id)
            && int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
    }

    private async Task<object?> Execute(string procedure, Guid id, int userId, bool? touch = null)
    {
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(procedure, cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = id;
        cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
        if (touch.HasValue) cmd.Parameters.Add("@Touch", SqlDbType.Bit).Value = touch.Value;
        return await cmd.ExecuteScalarAsync();
    }
}
