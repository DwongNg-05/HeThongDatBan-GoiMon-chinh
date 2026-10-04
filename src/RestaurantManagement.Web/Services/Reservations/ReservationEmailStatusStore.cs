using System.Data;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models.Reservations;

namespace RestaurantManagement.Web.Services;

/// <summary>
/// S2-09 Task 3: đọc trạng thái gửi email (dbo.EmailOutbox) của một lượt đặt bàn.
/// S2-09 Task 2: kèm lịch sử từng lần thử (dbo.EmailAttempts).
/// </summary>
public interface IReservationEmailStatusStore
{
    Task<IReadOnlyList<ReservationEmailRecord>> GetByReservationAsync(long reservationId, CancellationToken cancellationToken = default);
}

public sealed class SqlReservationEmailStatusStore(string connectionString) : IReservationEmailStatusStore
{
    // Lọc theo ReservationId ngay trong SQL: mỗi màn hình chỉ nhận email của đúng lượt đặt bàn đang xem.
    private const string Query = """
        SELECT Id,ReservationId,MessageType,Recipient,Status,AttemptCount,
               LastAttemptAt,NextAttemptAt,SentAt,LastError,CreatedAt
        FROM dbo.EmailOutbox
        WHERE ReservationId=@reservationId
        ORDER BY CreatedAt DESC,Id DESC;
        SELECT a.EmailId,a.AttemptNumber,a.Status,a.StartedAt,a.CompletedAt,a.Error
        FROM dbo.EmailAttempts a JOIN dbo.EmailOutbox e ON e.Id=a.EmailId
        WHERE e.ReservationId=@reservationId
        ORDER BY a.EmailId,a.AttemptNumber;
        """;

    public async Task<IReadOnlyList<ReservationEmailRecord>> GetByReservationAsync(long reservationId, CancellationToken cancellationToken = default)
    {
        var rows = new List<ReservationEmailRecord>();
        await using var connection = new SqlConnection(connectionString);
        await using var command = new SqlCommand(Query, connection);
        command.Parameters.Add("@reservationId", SqlDbType.BigInt).Value = reservationId;
        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            DateTime? NullableDate(string column) =>
                reader.IsDBNull(reader.GetOrdinal(column)) ? null : reader.GetDateTime(reader.GetOrdinal(column));

            rows.Add(new ReservationEmailRecord(
                reader.GetInt64(reader.GetOrdinal("Id")),
                reader.GetInt64(reader.GetOrdinal("ReservationId")),
                reader.GetString(reader.GetOrdinal("MessageType")),
                reader.GetString(reader.GetOrdinal("Recipient")),
                reader.GetString(reader.GetOrdinal("Status")),
                reader.GetInt32(reader.GetOrdinal("AttemptCount")),
                NullableDate("LastAttemptAt"),
                reader.GetDateTime(reader.GetOrdinal("NextAttemptAt")),
                NullableDate("SentAt"),
                reader.IsDBNull(reader.GetOrdinal("LastError")) ? null : reader.GetString(reader.GetOrdinal("LastError")),
                reader.GetDateTime(reader.GetOrdinal("CreatedAt"))));
        }

        var attempts = new Dictionary<long, List<EmailAttemptRecord>>();
        if (await reader.NextResultAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
            {
                var emailId = reader.GetInt64(reader.GetOrdinal("EmailId"));
                if (!attempts.TryGetValue(emailId, out var list)) attempts[emailId] = list = [];
                list.Add(new EmailAttemptRecord(
                    reader.GetInt32(reader.GetOrdinal("AttemptNumber")),
                    reader.GetString(reader.GetOrdinal("Status")),
                    reader.GetDateTime(reader.GetOrdinal("StartedAt")),
                    reader.IsDBNull(reader.GetOrdinal("CompletedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("CompletedAt")),
                    reader.IsDBNull(reader.GetOrdinal("Error")) ? null : reader.GetString(reader.GetOrdinal("Error"))));
            }
        return rows.Select(r => attempts.TryGetValue(r.Id, out var list) ? r with { Attempts = list } : r).ToArray();
    }
}
