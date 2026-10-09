using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models;

namespace RestaurantManagement.Web.Services;

public sealed class SessionOrderingService
{
    private readonly IConfiguration _configuration;

    public SessionOrderingService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    private string GetConnectionString()
    {
        return Environment.GetEnvironmentVariable("RM_CONNECTION_STRING")
            ?? _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Chưa cấu hình kết nối database.");
    }

    public async Task<int?> GetTableIdByCodeAsync(
        string tableCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tableCode))
        {
            return null;
        }

        await using var connection =
            new SqlConnection(GetConnectionString());

        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand("""
            SELECT Id
            FROM dbo.DiningTables
            WHERE Code = @Code AND IsActive = 1;
            """, connection);

        command.Parameters.Add("@Code", SqlDbType.NVarChar, -1)
            .Value = tableCode.Trim();

        var result =
            await command.ExecuteScalarAsync(cancellationToken);

        return result is null || result == DBNull.Value
            ? null
            : Convert.ToInt32(result);
    }

    public async Task<SessionOrderingViewModel?> GetByTableAsync(
        int tableId,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            new SqlConnection(GetConnectionString());

        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand("""
            SELECT t.Id, t.Code, s.Id, s.Status, root.Status
            FROM dbo.DiningTables t
            JOIN dbo.SessionTables st
                ON st.TableId = t.Id
                AND st.ReleasedAt IS NULL
            JOIN dbo.DiningSessions s
                ON s.Id = st.SessionId
            JOIN dbo.DiningSessions root
                ON root.Id = COALESCE(s.BillingSessionId, s.Id)
            WHERE t.Id = @TableId
                AND t.IsActive = 1
                AND s.Status <> 'Closed'
                AND root.Status <> 'Closed';
            """, connection);

        command.Parameters.Add("@TableId", SqlDbType.Int)
            .Value = tableId;

        SessionOrderingViewModel model;

        await using (var reader =
            await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            model = new SessionOrderingViewModel
            {
                TableId = reader.GetInt32(0),
                TableCode = reader.GetString(1),
                SessionId = reader.GetInt64(2),
                SessionStatus =
                    reader.GetString(3) == "Open"
                    && reader.GetString(4) == "Open"
                        ? "Open"
                        : "AwaitingPayment"
            };
        }

        await using var itemsCommand = new SqlCommand("""
            SELECT
                i.Id,
                b.BatchNumber,
                i.SubmittedAt,
                i.ItemName,
                i.Unit,
                i.UnitPrice,
                i.Quantity,
                i.LineTotal,
                i.Status,
                i.ChargeWhenCancelled
            FROM dbo.OrderBatches b
            JOIN dbo.OrderItems i
                ON i.BatchId = b.Id
            WHERE b.SessionId = @SessionId
            ORDER BY b.BatchNumber, i.Id;
            """, connection);

        itemsCommand.Parameters.Add("@SessionId", SqlDbType.BigInt)
            .Value = model.SessionId;

        await using var itemsReader =
            await itemsCommand.ExecuteReaderAsync(cancellationToken);

        while (await itemsReader.ReadAsync(cancellationToken))
        {
            var item = new SessionOrderItemViewModel
            {
                Id = itemsReader.GetInt64(0),
                BatchNumber = itemsReader.GetInt32(1),
                SubmittedAt = DateTime.SpecifyKind(
                    itemsReader.GetDateTime(2),
                    DateTimeKind.Utc),
                ItemName = itemsReader.GetString(3),
                Unit = itemsReader.GetString(4),
                UnitPrice = itemsReader.GetDecimal(5),
                Quantity = itemsReader.GetInt32(6),
                LineTotal = itemsReader.GetDecimal(7),
                Status = itemsReader.GetString(8),
                ChargeWhenCancelled = itemsReader.GetBoolean(9)
            };

            model.Items.Add(item);

            if (item.Status != "Cancelled" || item.ChargeWhenCancelled)
            {
                model.Subtotal += item.LineTotal;
            }
        }

        return model;
    }

    public async Task<long> SubmitAsync(
        long sessionId,
        Guid requestId,
        IReadOnlyList<SubmitOrderItem> items,
        int actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (sessionId <= 0)
        {
            throw new ArgumentException("Phiên không hợp lệ.");
        }

        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Mã yêu cầu không hợp lệ.");
        }

        if (actorUserId <= 0)
        {
            throw new ArgumentException("Nhân viên chưa đăng nhập.");
        }

        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0 || items.Count > 100)
        {
            throw new ArgumentException(
                "Giỏ món phải có từ 1 đến 100 dòng.");
        }

        if (items.Any(item =>
            item is null
            || item.MenuItemId <= 0
            || item.Quantity < 1
            || item.Quantity > 99))
        {
            throw new ArgumentException(
                "Món không hợp lệ hoặc số lượng phải từ 1 đến 99.");
        }

        await using var connection =
            new SqlConnection(GetConnectionString());

        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(
            "dbo.usp_SubmitOrder", connection)
        {
            CommandType = CommandType.StoredProcedure
        };

        command.Parameters.Add("@SessionId", SqlDbType.BigInt)
            .Value = sessionId;

        command.Parameters.Add("@RequestId", SqlDbType.UniqueIdentifier)
            .Value = requestId;

        command.Parameters.Add("@ActorUserId", SqlDbType.Int)
            .Value = actorUserId;

        command.Parameters.Add("@ItemsJson", SqlDbType.NVarChar, -1)
            .Value = JsonSerializer.Serialize(items);

        var result =
            await command.ExecuteScalarAsync(cancellationToken);

        if (result is null || result == DBNull.Value)
        {
            throw new InvalidOperationException(
                "Không nhận được mã đợt gọi món.");
        }

        return Convert.ToInt64(result);
    }

    public sealed class SubmitOrderItem
    {
        public int MenuItemId { get; set; }

        public int Quantity { get; set; }
    }
}