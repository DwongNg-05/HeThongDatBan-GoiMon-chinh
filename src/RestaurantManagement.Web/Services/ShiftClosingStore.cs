using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models.Cashier;

namespace RestaurantManagement.Web.Services;

/// <summary>
/// Đọc các bản chốt từ lịch sử đã lưu.
/// Không dùng số liệu hiện tại của ca để thay thế bản chốt cũ.
/// </summary>
public sealed class ShiftClosingStore(IConfiguration configuration)
{
    private string ConnectionString =>
        Environment.GetEnvironmentVariable("RM_CONNECTION_STRING")
        ?? configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException(
            "Chưa cấu hình kết nối database.");

    private static readonly string[] RequiredProperties =
    [
        "SnapshotVersion",
        "ShiftId",
        "ShiftName",
        "BusinessDate",
        "OpenedAt",
        "ClosingNumber",
        "ClosingEventId",
        "ClosedAt",
        "ClosedBy",
        "ClosedByName",
        "StatusAtClosing",
        "RestaurantName",
        "RestaurantAddress",
        "RestaurantPhone",
        "TimeZoneId",
        "OpeningCash",
        "InvoiceCount",
        "Revenue",
        "CashRevenue",
        "TransferRevenue",
        "DiscountTotal",
        "ServedTableCount",
        "CountedCash",
        "ExpectedCash",
        "CashDifference"
    ];

    public async Task<long?> GetLatestClosedShiftIdAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            new SqlConnection(ConnectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand("""
            SELECT TOP (1) ShiftId
            FROM dbo.ShiftEvents
            WHERE Action = 'Closed'
            ORDER BY Id DESC;
            """, connection);

        var result = await command.ExecuteScalarAsync(
            cancellationToken);

        return result is null || result == DBNull.Value
            ? null
            : Convert.ToInt64(result);
    }

    public async Task<ShiftClosingPdfViewModel?> GetAsync(
        long shiftId,
        long? eventId = null,
        CancellationToken cancellationToken = default)
    {
        if (shiftId <= 0 || eventId is <= 0)
        {
            return null;
        }

        await using var connection =
            new SqlConnection(ConnectionString);

        await connection.OpenAsync(cancellationToken);

        await using (var existsCommand = new SqlCommand("""
            SELECT COUNT(*)
            FROM dbo.Shifts
            WHERE Id = @ShiftId;
            """, connection))
        {
            existsCommand.Parameters.Add(
                "@ShiftId", SqlDbType.BigInt).Value = shiftId;

            var exists = Convert.ToInt32(
                await existsCommand.ExecuteScalarAsync(
                    cancellationToken));

            if (exists == 0)
            {
                return null;
            }
        }

        await using var command = new SqlCommand("""
            SELECT
                e.Id,
                e.OccurredAt,
                e.ActorUserId,
                e.SnapshotJson,

                ROW_NUMBER() OVER (
                    ORDER BY e.Id
                ) AS ClosingNumber,

                CAST(
                    CASE WHEN EXISTS (
                        SELECT 1
                        FROM dbo.ShiftEvents laterEvent
                        WHERE laterEvent.ShiftId = e.ShiftId
                          AND laterEvent.Action = 'Reopened'
                          AND laterEvent.Id > e.Id
                    )
                    THEN 1 ELSE 0 END
                    AS bit
                ) AS WasReopened

            FROM dbo.ShiftEvents e
            WHERE e.ShiftId = @ShiftId
              AND e.Action = 'Closed'
            ORDER BY e.Id DESC;
            """, connection);

        command.Parameters.Add(
            "@ShiftId", SqlDbType.BigInt).Value = shiftId;

        var closings = new List<ShiftClosingListItem>();

        var snapshots =
            new Dictionary<long, ShiftClosingSnapshot>();

        await using (var reader =
            await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetInt64(0);

                var closedAt = DateTime.SpecifyKind(
                    reader.GetDateTime(1),
                    DateTimeKind.Utc);

                var actorUserId = reader.GetInt32(2);

                var json = reader.IsDBNull(3)
                    ? null
                    : reader.GetString(3);

                var closingNumber = reader.GetInt64(4);
                var wasReopened = reader.GetBoolean(5);

                var snapshot = ReadSnapshot(
                    json,
                    shiftId,
                    id,
                    closingNumber,
                    actorUserId,
                    closedAt);

                if (snapshot is not null)
                {
                    snapshots.Add(id, snapshot);
                }

                closings.Add(new ShiftClosingListItem(
                    EventId: id,
                    ShiftId: shiftId,
                    ClosingNumber: closingNumber,
                    ClosedAt: closedAt,
                    ClosedByName: snapshot?.ClosedByName
                        ?? $"Tài khoản #{actorUserId}",
                    HasCompleteSnapshot: snapshot is not null,
                    WasReopened: wasReopened));
            }
        }

        if (closings.Count == 0)
        {
            return eventId.HasValue
                ? null
                : new ShiftClosingPdfViewModel
                {
                    ShiftId = shiftId,
                    ErrorMessage = "Ca này chưa có bản chốt đã lưu."
                };
        }

        var selected = eventId.HasValue
            ? closings.FirstOrDefault(
                item => item.EventId == eventId.Value)
            : closings[0];

        // Không cho chọn bản chốt thuộc một ca khác.
        if (selected is null)
        {
            return null;
        }

        snapshots.TryGetValue(
            selected.EventId,
            out var selectedSnapshot);

        return new ShiftClosingPdfViewModel
        {
            ShiftId = shiftId,
            Closings = closings,
            SelectedClosing = selectedSnapshot,
            WasReopened = selected.WasReopened,
            ErrorMessage = selectedSnapshot is null
                ? "Bản chốt này được lưu theo định dạng cũ "
                  + "hoặc thiếu dữ liệu nên chưa thể xuất PDF. "
                  + "Hệ thống giữ nguyên lịch sử, không thay "
                  + "bằng số liệu hiện tại của ca."
                : null
        };
    }

    private static ShiftClosingSnapshot? ReadSnapshot(
        string? json,
        long shiftId,
        long eventId,
        long closingNumber,
        int actorUserId,
        DateTime closedAt)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            // Không biến trường bị thiếu thành số 0 trên PDF.
            foreach (var property in RequiredProperties)
            {
                if (!root.TryGetProperty(property, out var value)
                    || value.ValueKind == JsonValueKind.Null)
                {
                    return null;
                }
            }

            var snapshot =
                JsonSerializer.Deserialize<ShiftClosingSnapshot>(
                    json);

            if (snapshot is null
                || snapshot.SnapshotVersion != 1
                || snapshot.ShiftId != shiftId
                || snapshot.ClosingEventId != eventId
                || snapshot.ClosingNumber != closingNumber
                || snapshot.ClosedBy != actorUserId
                || snapshot.StatusAtClosing != "Closed"
                || snapshot.ClosedAt != closedAt
                || string.IsNullOrWhiteSpace(
                    snapshot.RestaurantName)
                || string.IsNullOrWhiteSpace(
                    snapshot.ClosedByName))
            {
                return null;
            }

            if (snapshot.InvoiceCount < 0
                || snapshot.ServedTableCount < 0
                || snapshot.OpeningCash < 0
                || snapshot.CashRevenue < 0
                || snapshot.TransferRevenue < 0
                || snapshot.DiscountTotal < 0
                || snapshot.CountedCash < 0
                || snapshot.Revenue
                    != snapshot.CashRevenue
                       + snapshot.TransferRevenue
                || snapshot.ExpectedCash
                    != snapshot.OpeningCash
                       + snapshot.CashRevenue
                || snapshot.CashDifference
                    != snapshot.CountedCash
                       - snapshot.ExpectedCash)
            {
                return null;
            }

            return snapshot;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}