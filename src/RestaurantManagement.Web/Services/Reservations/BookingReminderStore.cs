using System.Data;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models.Reservations;

namespace RestaurantManagement.Web.Services;

/// <summary>S4-08 Task 1: đọc danh sách email nhắc lịch (dbo.EmailOutbox, MessageType = BookingReminder).</summary>
public interface IBookingReminderStore
{
    /// <summary>Email nhắc mới nhất trước; <paramref name="sentOnly"/> = chỉ email đã gửi thành công.</summary>
    Task<IReadOnlyList<BookingReminderRecord>> ListAsync(bool sentOnly, int take, CancellationToken cancellationToken = default);

    /// <summary>Số phút gửi nhắc trước giờ hẹn (dbo.RestaurantSettings.ReminderLeadMinutes).</summary>
    Task<int> LeadMinutesAsync(CancellationToken cancellationToken = default);
}

public sealed class SqlBookingReminderStore(string connectionString) : IBookingReminderStore
{
    public const int MaxRows = 200;

    // Bàn hiển thị là bàn đã ghi trong email (PayloadJson); email cũ chưa có thì lấy bàn hiện tại của lượt đặt.
    private const string Query = """
        SELECT TOP(@take) e.Id AS EmailId,e.ReservationId,r.Code,r.CustomerName,r.StartsAt,
               COALESCE(JSON_VALUE(e.PayloadJson,'$.TableCode'),t.Code) AS TableCode,
               e.Recipient,e.Status,e.AttemptCount,e.SentAt,e.LastError,e.CreatedAt
        FROM dbo.EmailOutbox e
        JOIN dbo.Reservations r ON r.Id=e.ReservationId
        LEFT JOIN dbo.DiningTables t ON t.Id=r.TableId
        WHERE e.MessageType='BookingReminder' AND (@sentOnly=0 OR e.Status='Sent')
        ORDER BY COALESCE(e.SentAt,e.CreatedAt) DESC,e.Id DESC;
        """;

    public async Task<IReadOnlyList<BookingReminderRecord>> ListAsync(bool sentOnly, int take, CancellationToken cancellationToken = default)
    {
        var rows = new List<BookingReminderRecord>();
        await using var connection = new SqlConnection(connectionString);
        await using var command = new SqlCommand(Query, connection);
        command.Parameters.Add("@take", SqlDbType.Int).Value = Math.Clamp(take, 1, MaxRows);
        command.Parameters.Add("@sentOnly", SqlDbType.Bit).Value = sentOnly;
        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string? Text(string column) => reader.IsDBNull(reader.GetOrdinal(column)) ? null : reader.GetString(reader.GetOrdinal(column));
            rows.Add(new BookingReminderRecord(
                reader.GetInt64(reader.GetOrdinal("EmailId")),
                reader.GetInt64(reader.GetOrdinal("ReservationId")),
                reader.GetString(reader.GetOrdinal("Code")),
                reader.GetString(reader.GetOrdinal("CustomerName")),
                reader.GetDateTime(reader.GetOrdinal("StartsAt")),
                Text("TableCode"),
                reader.GetString(reader.GetOrdinal("Recipient")),
                reader.GetString(reader.GetOrdinal("Status")),
                reader.GetInt32(reader.GetOrdinal("AttemptCount")),
                reader.IsDBNull(reader.GetOrdinal("SentAt")) ? null : reader.GetDateTime(reader.GetOrdinal("SentAt")),
                Text("LastError"),
                reader.GetDateTime(reader.GetOrdinal("CreatedAt"))));
        }
        return rows;
    }

    public async Task<int> LeadMinutesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await using var command = new SqlCommand("SELECT ReminderLeadMinutes FROM dbo.RestaurantSettings WHERE Id=1;", connection);
        await connection.OpenAsync(cancellationToken);
        return await command.ExecuteScalarAsync(cancellationToken) is int minutes ? minutes : BookingReminderRules.DefaultLeadMinutes;
    }
}
