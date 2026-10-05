using System.Data;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models.Reservations;

namespace RestaurantManagement.Web.Services.Reservations;

public sealed class ReservationSlotService(IConfiguration configuration)
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Chưa cấu hình ConnectionStrings:DefaultConnection.");

    public async Task<ReservationSlotsResult> GetSlotsAsync(DateOnly date)
    {
        var today = ReservationSchedulePolicy.Today;
        if (date < today)
            return new ReservationSlotsResult(date, [], "Không thể chọn ngày đã qua.");
        if (date > ReservationSchedulePolicy.LastReservableDate)
            return new ReservationSlotsResult(date, [], $"Chỉ nhận đặt bàn trước tối đa {ReservationSchedulePolicy.MaximumAdvanceDays} ngày.");

        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand("""
            SELECT Name FROM dbo.SpecialHolidays WHERE HolidayDate=@Date AND IsActive=1;
            SELECT IsClosed, OpensAt, ClosesAt FROM dbo.OpeningHours WHERE DayOfWeek=@Day;
            """, connection);
        command.Parameters.Add("@Date", SqlDbType.Date).Value = date.ToDateTime(TimeOnly.MinValue);
        command.Parameters.Add("@Day", SqlDbType.Int).Value = ReservationSchedulePolicy.ToOpeningDay(date);
        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
            return new ReservationSlotsResult(date, [], $"Nhà hàng đóng cửa: {reader.GetString(0)}.");
        await reader.NextResultAsync();
        if (!await reader.ReadAsync() || reader.GetBoolean(0))
            return new ReservationSlotsResult(date, [], "Nhà hàng không nhận đặt bàn vào ngày này.");
        if (reader.IsDBNull(1) || reader.IsDBNull(2))
            return new ReservationSlotsResult(date, [], "Nhà hàng chưa cấu hình giờ mở cửa cho ngày này.");

        var opensAt = TimeOnly.FromTimeSpan(reader.GetTimeSpan(1));
        var closesAt = TimeOnly.FromTimeSpan(reader.GetTimeSpan(2));
        var slots = ReservationSchedulePolicy.CreateSlots(opensAt, closesAt, date);
        return slots.Count == 0
            ? new ReservationSlotsResult(date, [], "Không còn khung giờ đặt bàn cho ngày này.")
            : new ReservationSlotsResult(date, slots);
    }
}
