using System.Data;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models.Reservations;

namespace RestaurantManagement.Web.Services;

public sealed class ReservationConfirmationService(IConfiguration configuration)
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Missing database connection.");
    private static string? Optional(SqlDataReader r, string name) => r.IsDBNull(r.GetOrdinal(name)) ? null : r.GetString(r.GetOrdinal(name));
    private static DateTime Local(SqlDataReader r, string name) => VietnamTime.FromUtc(r.GetDateTime(r.GetOrdinal(name)));
    private static SqlCommand Command(SqlConnection cn, string procedure, int actor)
    {
        var command = new SqlCommand(procedure, cn) { CommandType = CommandType.StoredProcedure };
        command.Parameters.Add("@ActorUserId", SqlDbType.Int).Value = actor;
        return command;
    }
    public async Task<List<PendingReservation>> Pending(int actor, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await using var cmd = Command(cn,"dbo.usp_PendingReservations",actor);
        await cn.OpenAsync(ct);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var rows = new List<PendingReservation>();
        while(await r.ReadAsync(ct)) rows.Add(new()
        {
            Id=r.GetInt64(r.GetOrdinal("Id")), Code=r.GetString(r.GetOrdinal("Code")),
            CustomerName=r.GetString(r.GetOrdinal("CustomerName")), GuestCount=r.GetInt32(r.GetOrdinal("GuestCount")),
            StartsAt=Local(r,"StartsAt"), EndsAt=Local(r,"EndsAt"), AreaName=Optional(r,"AreaName")
        });
        return rows;
    }
    public async Task<ReservationConfirmation?> Details(int actor, long id, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await using var cmd = Command(cn,"dbo.usp_ReservationConfirmationDetails",actor);
        cmd.Parameters.Add("@ReservationId", SqlDbType.BigInt).Value=id;
        await cn.OpenAsync(ct);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if(!await r.ReadAsync(ct)) return null;
        var model=new ReservationConfirmation
        {
            Id=r.GetInt64(r.GetOrdinal("Id")), Code=r.GetString(r.GetOrdinal("Code")),
            CustomerName=r.GetString(r.GetOrdinal("CustomerName")), Phone=r.GetString(r.GetOrdinal("Phone")),
            Email=Optional(r,"Email"), GuestCount=r.GetInt32(r.GetOrdinal("GuestCount")),
            StartsAt=Local(r,"StartsAt"), EndsAt=Local(r,"EndsAt"), Status=r.GetString(r.GetOrdinal("Status")),
            AreaName=Optional(r,"AreaName"), TableCode=Optional(r,"TableCode"), EmailStatus=Optional(r,"EmailStatus"),
            EmailError=Optional(r,"EmailError"), AttemptCount=r.IsDBNull(r.GetOrdinal("AttemptCount"))?0:r.GetInt32(r.GetOrdinal("AttemptCount"))
        };
        await r.NextResultAsync(ct);
        while(await r.ReadAsync(ct)) model.Tables.Add(new(r.GetInt32(0),r.GetString(1),r.GetInt32(2),r.GetString(3)));
        return model;
    }
    public async Task Confirm(int actor,long id,int tableId,CancellationToken ct = default)
    {
        await using var cn=new SqlConnection(ConnectionString);
        await using var cmd=Command(cn,"dbo.usp_ConfirmReservation",actor);
        cmd.Parameters.Add("@ReservationId",SqlDbType.BigInt).Value=id;
        cmd.Parameters.Add("@TableId",SqlDbType.Int).Value=tableId;
        await cn.OpenAsync(ct);
        await cmd.ExecuteNonQueryAsync(ct);
    }
    public async Task<List<ReservedSlot>> Slots(int actor,DateTime day,CancellationToken ct = default)
    {
        await using var cn=new SqlConnection(ConnectionString);
        await using var cmd=Command(cn,"dbo.usp_ReservedTableSlots",actor);
        cmd.Parameters.Add("@Day",SqlDbType.Date).Value=day.Date;
        await cn.OpenAsync(ct);
        await using var r=await cmd.ExecuteReaderAsync(ct);
        var rows=new List<ReservedSlot>();
        while(await r.ReadAsync(ct)) rows.Add(new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetInt32(3),r.GetString(4),
            VietnamTime.FromUtc(r.GetDateTime(5)),VietnamTime.FromUtc(r.GetDateTime(6)),r.GetInt32(7)));
        return rows;
    }
}
