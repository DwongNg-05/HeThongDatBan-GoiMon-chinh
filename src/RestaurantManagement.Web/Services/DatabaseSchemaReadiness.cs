using Microsoft.Data.SqlClient;

namespace RestaurantManagement.Web.Services;

public sealed class DatabaseSchemaReadiness(IConfiguration configuration)
{
    private readonly SemaphoreSlim _gate=new(1,1);
    private bool? _ready;
    private DateTimeOffset _expires;

    public async Task<bool> IsReadyAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if(_ready is not null && DateTimeOffset.UtcNow<_expires) return _ready.Value;
            await using var cn=new SqlConnection(configuration.GetConnectionString("DefaultConnection"));
            await using var cmd=new SqlCommand("""
                SELECT CASE WHEN COL_LENGTH('dbo.OrderItems','QuickNotes') IS NOT NULL
                  AND COL_LENGTH('dbo.OrderBatches','OriginSessionId') IS NOT NULL
                  AND OBJECT_ID('dbo.TableAdjacencies','U') IS NOT NULL
                  AND COL_LENGTH('dbo.DiningSessions','TableGroupLabel') IS NOT NULL
                  AND OBJECT_ID('dbo.vw_ActiveTableGroups','V') IS NOT NULL THEN 1 ELSE 0 END;
                """,cn);
            await cn.OpenAsync(ct);
            _ready=Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))==1;
            _expires=DateTimeOffset.UtcNow.AddSeconds(_ready.Value ? 60 : 5);
            return _ready.Value;
        }
        finally { _gate.Release(); }
    }
}
