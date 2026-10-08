using Microsoft.Data.SqlClient;
using System.Data;
using RestaurantManagement.Web.Models.Reservations;

namespace RestaurantManagement.Web.Services;

public record CancellationShift(long Id,string Name,string OpenedAt,string? ClosedAt,string Status);
public record ShiftCancellationEntry(long EventId,long SessionId,long BatchId,int BatchNumber,long OrderItemId,string TableCode,
    string ItemName,int Quantity,decimal UnitPrice,decimal LineTotal,int? ActorUserId,string Actor,string OccurredAt,
    string Reason,string? Note,string PreviousStatus,bool ChargeWhenCancelled,decimal ChargedAmount);
public record ShiftCancellationReport(long? SelectedShiftId,IReadOnlyList<CancellationShift> Shifts,IReadOnlyList<ShiftCancellationEntry> Entries);

public class ShiftCancellationReportStore(string connectionString)
{
    private static string Time(DateTime utc) => VietnamTime.FromUtc(utc).ToString("dd/MM/yyyy HH:mm:ss",System.Globalization.CultureInfo.InvariantCulture);
    public static string ReasonLabel(string reason) => reason switch {
        "ChangedMind"=>"Khách đổi ý", "Mistake"=>"Gọi nhầm", "SoldOut"=>"Hết nguyên liệu", "ManagerOverride"=>"Quản lý huỷ", _=>"Không xác định" };
    public async Task<ShiftCancellationReport> Read(int actor,long? shiftId,CancellationToken ct)
    {
        await using var cn=new SqlConnection(connectionString);await cn.OpenAsync(ct);
        await using var cmd=new SqlCommand("dbo.usp_ShiftCancellationReport",cn){CommandType=CommandType.StoredProcedure};
        cmd.Parameters.AddWithValue("@ActorUserId",actor);cmd.Parameters.Add("@ShiftId",SqlDbType.BigInt).Value=(object?)shiftId??DBNull.Value;
        await using var reader=await cmd.ExecuteReaderAsync(ct);
        var shifts=new List<CancellationShift>();
        while(await reader.ReadAsync(ct)) shifts.Add(new(reader.GetInt64(0),reader.GetString(1),Time(reader.GetDateTime(2)),reader.IsDBNull(3)?null:Time(reader.GetDateTime(3)),reader.GetString(4)));
        await reader.NextResultAsync(ct);await reader.ReadAsync(ct);long? selected=reader.IsDBNull(0)?null:reader.GetInt64(0);
        await reader.NextResultAsync(ct);var entries=new List<ShiftCancellationEntry>();
        while(await reader.ReadAsync(ct))
        {
            var charged=reader.GetBoolean(17);var total=reader.GetDecimal(9);
            var actorName=reader.IsDBNull(11)?"Tài khoản không còn thông tin":reader.GetString(11)+" ("+reader.GetString(12)+")";
            entries.Add(new(reader.GetInt64(0),reader.GetInt64(1),reader.GetInt64(2),reader.GetInt32(3),reader.GetInt64(4),reader.GetString(5),
                reader.GetString(6),reader.GetInt32(7),reader.GetDecimal(8),reader.GetDecimal(9),reader.IsDBNull(10)?null:reader.GetInt32(10),
                actorName,Time(reader.GetDateTime(13)),ReasonLabel(reader.GetString(14)),reader.IsDBNull(15)?null:reader.GetString(15),reader.GetString(16),charged,charged?total:0));
        }
        return new(selected,shifts,entries);
    }
}
