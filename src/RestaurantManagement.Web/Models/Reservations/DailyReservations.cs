using System.Globalization;
using Microsoft.Data.SqlClient;
using System.Data;

namespace RestaurantManagement.Web.Models.Reservations;

public record DailyReservationItem(string Code, string CustomerName, string Phone, int GuestCount,
    string TimeSlot, string TableName, string Status, bool IsUpcoming = false, long Id = 0,
    string? HoldUntil = null, int ExtensionCount = 0);
public record DailyReservationPage(DateOnly Date, IReadOnlyList<DailyReservationItem> Reservations, DateTime EvaluatedAtUtc = default);

public static class DailyReservationRules
{
    public static bool IsUpcoming(DateTime startsAtUtc, string status, DateTime nowUtc) =>
        status is "Pending" or "Confirmed" && startsAtUtc >= nowUtc && startsAtUtc <= nowUtc.AddMinutes(30);
    public static bool IsSupportedFilter(string? status) => string.IsNullOrEmpty(status)
        || status is "Pending" or "Confirmed" or "Cancelled" or "NoShow";
    public static DateOnly Today(DateTime utcNow) => DateOnly.FromDateTime(VietnamTime.FromUtc(utcNow));
    public static (DateTime Start, DateTime End) UtcBounds(DateOnly date) =>
        (VietnamTime.ToUtc(date.ToDateTime(TimeOnly.MinValue)), VietnamTime.ToUtc(date.AddDays(1).ToDateTime(TimeOnly.MinValue)));

    // Current input and database contract support exactly ten ASCII digits.
    // Fail closed for unexpected legacy values rather than exposing a raw number.
    public static string MaskPhone(string phone) => phone.Length == 10 && phone.All(c => c is >= '0' and <= '9')
        ? phone[..3] + "****" + phone[7..] : "**********";
    public static string TimeSlot(DateTime startUtc, DateTime endUtc)
    {
        var start = VietnamTime.FromUtc(startUtc);
        var end = VietnamTime.FromUtc(endUtc);
        return start.ToString("HH:mm", CultureInfo.InvariantCulture) + "–" +
            end.ToString(start.Date == end.Date ? "HH:mm" : "HH:mm (dd/MM/yyyy)", CultureInfo.InvariantCulture);
    }
    public static string StatusLabel(string status) => status switch
    {
        "Pending" => "Chờ xác nhận", "Confirmed" => "Đã xác nhận", "Rejected" => "Đã từ chối",
        "Cancelled" => "Đã huỷ", "Arrived" => "Khách đã tới", "NoShow" => "Khách không tới", _ => "Không xác định"
    };
}

public class DailyReservationStore(string connectionString)
{
    public async Task<DailyReservationPage> Read(DateOnly date, CancellationToken cancellationToken, string? status = null, DateTime? utcNow = null)
    {
        if (!DailyReservationRules.IsSupportedFilter(status)) throw new ArgumentException("Unsupported reservation status.", nameof(status));
        var (start, end) = DailyReservationRules.UtcBounds(date);
        var now = utcNow ?? DateTime.UtcNow;
        await using var connection = new SqlConnection(connectionString);
        await using var command = new SqlCommand("""
            SELECT r.Code,r.CustomerName,r.Phone,r.GuestCount,r.StartsAt,r.EndsAt,t.Code AS TableName,r.Status,r.Id,r.HoldExtendedUntil,r.ExtensionCount
            FROM dbo.Reservations r LEFT JOIN dbo.DiningTables t ON t.Id=r.TableId
            WHERE r.StartsAt>=@start AND r.StartsAt<@end AND (@status IS NULL OR r.Status=@status)
            ORDER BY r.StartsAt ASC,r.Id ASC;
            """, connection);
        command.Parameters.Add("@start", SqlDbType.DateTime2).Value = start;
        command.Parameters.Add("@end", SqlDbType.DateTime2).Value = end;
        command.Parameters.Add("@status", SqlDbType.VarChar, 20).Value = string.IsNullOrEmpty(status) ? DBNull.Value : status;
        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<DailyReservationItem>();
        while (await reader.ReadAsync(cancellationToken))
            items.Add(new(reader.GetString(0).TrimEnd(), reader.GetString(1), DailyReservationRules.MaskPhone(reader.GetString(2)),
                reader.GetInt32(3), DailyReservationRules.TimeSlot(reader.GetDateTime(4), reader.GetDateTime(5)),
                reader.IsDBNull(6) ? "Chưa xếp bàn" : reader.GetString(6), DailyReservationRules.StatusLabel(reader.GetString(7)),
                DailyReservationRules.IsUpcoming(reader.GetDateTime(4), reader.GetString(7), now),reader.GetInt64(8),
                reader.IsDBNull(9) ? null : VietnamTime.FromUtc(reader.GetDateTime(9)).ToString("dd/MM/yyyy HH:mm:ss"),reader.GetByte(10)));
        // Stable partition retains SQL appointment/ID order within each group.
        return new(date, items.OrderByDescending(item => item.IsUpcoming).ToList(), DateTime.SpecifyKind(now, DateTimeKind.Utc));
    }
}
