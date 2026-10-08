using System.Data;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Services.EmailVerification;

namespace RestaurantManagement.Web.Services;

/// <summary>Kết quả gửi email xác nhận ngay sau khi đặt bàn (S2-09 Task 1).</summary>
public enum BookingEmailOutcome
{
    /// <summary>Khách không nhập email nên không có email cần gửi.</summary>
    NotRequested,
    Sent,
    Failed
}

public sealed record BookingEmailResult(BookingEmailOutcome Outcome, string? Recipient = null, string? Error = null);

/// <summary>Một email đã được nhận (khoá) từ dbo.EmailOutbox để gửi. AttemptNumber: 1 = lần gửi đầu, 2–4 = lần gửi lại.</summary>
public sealed record ClaimedEmail(long Id, int AttemptNumber, string Recipient, string Subject, string PayloadJson, string MessageType,
    long? ReservationId = null);

/// <summary>Hàng đợi email của lượt đặt bàn (dbo.EmailOutbox).</summary>
public interface IBookingEmailOutbox
{
    Task<ClaimedEmail?> ClaimAsync(long reservationId, string messageType, CancellationToken cancellationToken = default);

    /// <summary>
    /// S2-09 Task 2: nhận một email đặt bàn đã thất bại, còn lần thử và đã tới giờ gửi lại (dbo.usp_ClaimDueBookingEmail).
    /// Trả về null khi không còn email nào cần gửi lại.
    /// </summary>
    Task<ClaimedEmail?> ClaimDueRetryAsync(CancellationToken cancellationToken = default) => Task.FromResult<ClaimedEmail?>(null);

    Task CompleteAsync(long emailId, int attemptNumber, bool succeeded, string? error, CancellationToken cancellationToken = default);
}

public sealed class SqlBookingEmailOutbox(string connectionString) : IBookingEmailOutbox
{
    public async Task<ClaimedEmail?> ClaimAsync(long reservationId, string messageType, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await using var command = new SqlCommand("dbo.usp_ClaimReservationEmail", connection) { CommandType = CommandType.StoredProcedure };
        command.Parameters.Add("@ReservationId", SqlDbType.BigInt).Value = reservationId;
        command.Parameters.Add("@MessageType", SqlDbType.VarChar, 30).Value = messageType;
        await connection.OpenAsync(cancellationToken);
        return await ReadClaimed(command, cancellationToken);
    }

    public async Task<ClaimedEmail?> ClaimDueRetryAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await using var command = new SqlCommand("dbo.usp_ClaimDueBookingEmail", connection) { CommandType = CommandType.StoredProcedure };
        await connection.OpenAsync(cancellationToken);
        return await ReadClaimed(command, cancellationToken);
    }

    private static async Task<ClaimedEmail?> ReadClaimed(SqlCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        long? reservationId = null;
        for (var i = 0; i < reader.FieldCount; i++)
            if (reader.GetName(i) == "ReservationId" && !reader.IsDBNull(i)) reservationId = reader.GetInt64(i);
        return new ClaimedEmail(
            reader.GetInt64(reader.GetOrdinal("Id")),
            reader.GetInt32(reader.GetOrdinal("AttemptCount")),
            reader.GetString(reader.GetOrdinal("Recipient")),
            reader.GetString(reader.GetOrdinal("Subject")),
            reader.GetString(reader.GetOrdinal("PayloadJson")),
            reader.GetString(reader.GetOrdinal("MessageType")),
            reservationId);
    }

    public async Task CompleteAsync(long emailId, int attemptNumber, bool succeeded, string? error, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await using var command = new SqlCommand("dbo.usp_CompleteEmail", connection) { CommandType = CommandType.StoredProcedure };
        command.Parameters.Add("@EmailId", SqlDbType.BigInt).Value = emailId;
        command.Parameters.Add("@AttemptNumber", SqlDbType.Int).Value = attemptNumber;
        command.Parameters.Add("@Succeeded", SqlDbType.Bit).Value = succeeded;
        command.Parameters.Add("@Error", SqlDbType.NVarChar, 2000).Value = (object?)error ?? DBNull.Value;
        await connection.OpenAsync(cancellationToken);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

/// <summary>
/// Gửi email xác nhận ngay sau khi lượt đặt bàn được ghi nhận và lưu kết quả vào dbo.EmailOutbox.
/// Lỗi gửi email không bao giờ làm mất lượt đặt bàn: đặt bàn đã được ghi trước khi gửi.
/// S2-09 Task 2: email lỗi được hẹn gửi lại sau 5 phút; <see cref="RetryDueAsync"/> (gọi từ BookingEmailRetryWorker)
/// gửi lại tối đa 3 lần rồi ghi kết quả cuối cùng (xem <see cref="EmailRetryPolicy"/>).
/// Email xác nhận (BookingReceived) kèm nút "Xác nhận đặt bàn" khi có <see cref="BookingConfirmationLinks"/>.
/// </summary>
public sealed class BookingEmailDispatcher(IBookingEmailOutbox outbox, IEmailSender sender, ILogger<BookingEmailDispatcher> logger,
    BookingConfirmationLinks? confirmationLinks = null)
{
    public const string BookingReceived = "BookingReceived";

    /// <summary>Thời gian tối đa chờ máy chủ email (AC: email gửi trong vòng 60 giây).</summary>
    public static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(30);

    public const int MaxErrorLength = 2000;

    public const string BookingCancelled = "BookingCancelled";

    public Task<BookingEmailResult> SendBookingReceivedAsync(long reservationId, CancellationToken cancellationToken = default) =>
        SendAsync(reservationId, BookingReceived, cancellationToken);

    /// <summary>Gửi ngay email báo huỷ sau khi quản lý huỷ lượt đặt bàn.</summary>
    public Task<BookingEmailResult> SendBookingCancelledAsync(long reservationId, CancellationToken cancellationToken = default) =>
        SendAsync(reservationId, BookingCancelled, cancellationToken);

    public async Task<BookingEmailResult> SendAsync(long reservationId, string messageType, CancellationToken cancellationToken = default)
    {
        ClaimedEmail? claimed;
        try
        {
            claimed = await outbox.ClaimAsync(reservationId, messageType, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Không lấy được email xác nhận của lượt đặt bàn {ReservationId}.", reservationId);
            return new BookingEmailResult(BookingEmailOutcome.Failed, Error: "Không đọc được hàng đợi email.");
        }
        if (claimed is null) return new BookingEmailResult(BookingEmailOutcome.NotRequested);
        return await DeliverAsync(claimed);
    }

    /// <summary>Số email tối đa gửi lại trong một lượt chạy của worker.</summary>
    public const int MaxRetriesPerRun = 50;

    /// <summary>
    /// S2-09 Task 2: gửi lại mọi email đặt bàn đã tới giờ thử lại (lần thử trước thất bại cách đây ≥ 5 phút, còn lần thử).
    /// Mỗi lần thử được ghi kết quả; hết 3 lần gửi lại mà vẫn lỗi thì database ghi kết quả cuối cùng là thất bại.
    /// </summary>
    /// <returns>Số email đã thử gửi lại.</returns>
    public async Task<int> RetryDueAsync(CancellationToken cancellationToken = default)
    {
        var processed = 0;
        while (processed < MaxRetriesPerRun && !cancellationToken.IsCancellationRequested)
        {
            var claimed = await outbox.ClaimDueRetryAsync(cancellationToken);
            if (claimed is null) break;
            var result = await DeliverAsync(claimed);
            processed++;
            logger.LogInformation("Gửi lại email {EmailId} ({Label}): {Outcome}.", claimed.Id, EmailRetryPolicy.AttemptLabel(claimed.AttemptNumber), result.Outcome);
        }
        return processed;
    }

    /// <summary>Gửi một email đã nhận và lưu kết quả của lần thử đó.</summary>
    public async Task<BookingEmailResult> DeliverAsync(ClaimedEmail claimed)
    {
        var messageType = claimed.MessageType;
        string? error = null;
        try
        {
            var details = BookingConfirmationEmail.ParsePayload(claimed.PayloadJson);
            var confirmUrl = messageType == BookingReceived ? confirmationLinks?.CreateUrl(details.Code) : null;
            var message = BookingConfirmationEmail.Create(claimed.Recipient, details, messageType, confirmUrl);
            // Không gắn với yêu cầu HTTP: khách đóng trình duyệt giữa chừng thì email vẫn gửi xong và kết quả vẫn được lưu.
            using var timeout = new CancellationTokenSource(SendTimeout);
            await sender.SendAsync(message, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            error = $"Hết thời gian chờ máy chủ email ({SendTimeout.TotalSeconds:0} giây).";
        }
        catch (Exception ex)
        {
            error = Describe(ex);
        }
        if (error is not null)
            logger.LogWarning("Gửi email {EmailId} của lượt đặt bàn {ReservationId} thất bại ({Label}): {Error}",
                claimed.Id, claimed.ReservationId, EmailRetryPolicy.AttemptLabel(claimed.AttemptNumber), error);

        try
        {
            await outbox.CompleteAsync(claimed.Id, claimed.AttemptNumber, error is null, error, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Không lưu được kết quả gửi email {EmailId}.", claimed.Id);
        }

        return error is null
            ? new BookingEmailResult(BookingEmailOutcome.Sent, claimed.Recipient)
            : new BookingEmailResult(BookingEmailOutcome.Failed, claimed.Recipient, error);
    }

    /// <summary>Mô tả lỗi để nhân viên đọc (kèm lỗi gốc), giới hạn độ dài cột LastError.</summary>
    public static string Describe(Exception ex)
    {
        var inner = ex.InnerException is null ? string.Empty : $" ({ex.InnerException.Message})";
        var text = $"{ex.GetType().Name}: {ex.Message}{inner}";
        return text.Length <= MaxErrorLength ? text : text[..MaxErrorLength];
    }
}
