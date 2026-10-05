using System.Data;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models.Reservations;

namespace RestaurantManagement.Web.Services.Reservations;

public sealed class ReservationSlotService(IConfiguration configuration)
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Chưa cấu hình ConnectionStrings:DefaultConnection.");

    public async Task<ReservationSlotsResult> GetSlotsAsync(DateOnly date, int? guestCount = null, int? preferredAreaId = null)
    {
        var today = ReservationSchedulePolicy.Today;
        if (date < today)
            return new ReservationSlotsResult(date, [], "Không thể chọn ngày đã qua.");
        if (date > ReservationSchedulePolicy.LastReservableDate)
            return new ReservationSlotsResult(date, [], $"Chỉ nhận đặt bàn trước tối đa {ReservationSchedulePolicy.MaximumAdvanceDays} ngày.");

        var schedule = await GetScheduleAsync(date);
        if (schedule.Message is not null) return schedule;
        if (guestCount is null) return schedule;
        if (guestCount is < 1 or > 20)
            return new ReservationSlotsResult(date, [], "Vui lòng nhập số khách từ 1 đến 20 để xem khung giờ.");
        return await FilterByCapacityAsync(schedule, guestCount.Value, preferredAreaId);
    }

    private async Task<ReservationSlotsResult> GetScheduleAsync(DateOnly date)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand("""
                SELECT Name FROM dbo.SpecialHolidays WHERE HolidayDate=@Date AND IsActive=1;
                SELECT IsClosed, OpensAt, ClosesAt FROM dbo.OpeningHours WHERE DayOfWeek=@Day;
                """, connection);
        command.Parameters.Add("@Date", SqlDbType.Date).Value = date.ToDateTime(TimeOnly.MinValue);
        command.Parameters.Add("@Day", SqlDbType.Int).Value = ReservationSchedulePolicy.ToOpeningDay(date);
        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync()) return new ReservationSlotsResult(date, [], $"Nhà hàng đóng cửa: {reader.GetString(0)}.");
        await reader.NextResultAsync();
        if (!await reader.ReadAsync() || reader.GetBoolean(0)) return new ReservationSlotsResult(date, [], "Nhà hàng không nhận đặt bàn vào ngày này.");
        if (reader.IsDBNull(1) || reader.IsDBNull(2)) return new ReservationSlotsResult(date, [], "Nhà hàng chưa cấu hình giờ mở cửa cho ngày này.");
        var opensAt = TimeOnly.FromTimeSpan(reader.GetTimeSpan(1));
        var closesAt = TimeOnly.FromTimeSpan(reader.GetTimeSpan(2));
        var slots = ReservationSchedulePolicy.CreateSlots(opensAt, closesAt, date);
        return slots.Count == 0
            ? new ReservationSlotsResult(date, [], "Không còn khung giờ đặt bàn cho ngày này.")
            : new ReservationSlotsResult(date, slots);
    }

    private async Task<ReservationSlotsResult> FilterByCapacityAsync(ReservationSlotsResult schedule, int guestCount, int? preferredAreaId)
    {
        var dayStart = VietnamTime.ToUtc(schedule.Date.ToDateTime(TimeOnly.MinValue));
        var dayEnd = VietnamTime.ToUtc(schedule.Date.AddDays(1).ToDateTime(TimeOnly.MinValue));
        var tableIds = new List<int>();
        var intervals = new List<TableReservationInterval>();
        var duration = 90;
        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand("""
                SELECT t.Id FROM dbo.DiningTables t JOIN dbo.Areas a ON a.Id=t.AreaId
                WHERE t.IsActive=1 AND a.IsActive=1 AND t.MaxCapacity>=@GuestCount
                  AND (@PreferredAreaId IS NULL OR t.AreaId=@PreferredAreaId);
                SELECT r.TableId, r.StartsAt, r.EndsAt FROM dbo.Reservations r JOIN dbo.DiningTables t ON t.Id=r.TableId
                WHERE r.Status IN ('Pending','Confirmed','Arrived') AND r.StartsAt<@DayEnd AND r.EndsAt>@DayStart
                  AND t.IsActive=1 AND t.MaxCapacity>=@GuestCount
                  AND (@PreferredAreaId IS NULL OR t.AreaId=@PreferredAreaId);
                SELECT DefaultBookingMinutes FROM dbo.RestaurantSettings WHERE Id=1;
                """, connection);
        command.Parameters.Add("@GuestCount", SqlDbType.Int).Value = guestCount;
        command.Parameters.Add("@PreferredAreaId", SqlDbType.Int).Value = (object?)preferredAreaId ?? DBNull.Value;
        command.Parameters.Add("@DayStart", SqlDbType.DateTime2).Value = dayStart;
        command.Parameters.Add("@DayEnd", SqlDbType.DateTime2).Value = dayEnd;
        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) tableIds.Add(reader.GetInt32(0));
        await reader.NextResultAsync();
        while (await reader.ReadAsync()) intervals.Add(new TableReservationInterval(reader.GetInt32(0), reader.GetDateTime(1), reader.GetDateTime(2)));
        await reader.NextResultAsync();
        if (await reader.ReadAsync()) duration = reader.GetInt32(0);

        if (tableIds.Count == 0)
            return new ReservationSlotsResult(schedule.Date, [], "Không có bàn đủ chỗ trong khu vực đã chọn. Vui lòng đổi khu vực hoặc số khách.");
        var slots = schedule.Slots.Where(slot =>
        {
            var start = VietnamTime.ToUtc(schedule.Date.ToDateTime(TimeOnly.Parse(slot)));
            return ReservationCapacityPolicy.HasAvailableTable(tableIds, intervals, start, start.AddMinutes(duration));
        }).ToList();
        return slots.Count == 0
            ? new ReservationSlotsResult(schedule.Date, [], "Khung giờ này vừa hết bàn phù hợp. Vui lòng chọn ngày, giờ hoặc khu vực khác.")
            : new ReservationSlotsResult(schedule.Date, slots);
    }
}
