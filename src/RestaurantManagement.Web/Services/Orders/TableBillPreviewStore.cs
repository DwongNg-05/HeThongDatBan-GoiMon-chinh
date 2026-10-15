using System.Data;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models.Orders;

namespace RestaurantManagement.Web.Services;

/// <summary>
/// S4-01 Task 1: đọc dữ liệu tạm tính của một bàn đang phục vụ (docs/S4-01-Task1.md).
/// CHỈ ĐỌC — xem tạm tính không được đổi bàn, phiên hay đơn gọi món:
/// - chỉ có câu SELECT (không INSERT/UPDATE/DELETE/MERGE/EXEC thủ tục);
/// - chạy trong một giao dịch luôn bị huỷ (ROLLBACK) ở cuối, nên kể cả khi sau này có ai thêm câu ghi vào đây cũng không lưu được;
/// - kết nối khai báo ApplicationIntent=ReadOnly (bản sao chỉ đọc nếu có).
/// Phiên của bàn: phiên chưa đóng đang giữ bàn (SessionTables.ReleasedAt IS NULL) → phiên thanh toán gốc (BillingSessionId),
/// lấy món của phiên gốc và mọi phiên đã gộp vào — cùng phạm vi với hoá đơn khi thanh toán.
/// </summary>
public sealed class TableBillPreviewStore(IConfiguration configuration)
{
    public const string SessionQuery = """
        SELECT t.Code,t.Status,root.Id,root.Status,
               COALESCE((SELECT STRING_AGG(CONVERT(nvarchar(max),x.Code),N', ') WITHIN GROUP (ORDER BY x.Code)
                         FROM dbo.SessionTables st2 JOIN dbo.DiningTables x ON x.Id=st2.TableId JOIN dbo.DiningSessions s2 ON s2.Id=st2.SessionId
                         WHERE (s2.Id=root.Id OR s2.BillingSessionId=root.Id) AND st2.ReleasedAt IS NULL),t.Code) AS Tables
        FROM dbo.DiningTables t
        OUTER APPLY (SELECT TOP(1) COALESCE(s.BillingSessionId,s.Id) AS RootId
                     FROM dbo.SessionTables st JOIN dbo.DiningSessions s ON s.Id=st.SessionId
                     WHERE st.TableId=t.Id AND st.ReleasedAt IS NULL AND s.Status<>'Closed'
                     ORDER BY s.OpenedAt DESC, s.Id DESC) cur
        LEFT JOIN dbo.DiningSessions root ON root.Id=cur.RootId AND root.Status<>'Closed'
        WHERE t.Code=@code;
        """;

    public const string ItemsQuery = """
        SELECT i.Id,i.ItemName,i.Unit,i.UnitPrice,i.Quantity,i.Status,i.ChargeWhenCancelled,i.Notes,i.QuickNotes,i.SubmittedAt
        FROM dbo.OrderItems i
        JOIN dbo.OrderBatches b ON b.Id=i.BatchId
        JOIN dbo.DiningSessions x ON x.Id=b.SessionId
        WHERE x.Id=@root OR x.BillingSessionId=@root
        ORDER BY i.SubmittedAt, i.Id;
        """;

    public async Task<TableBillPreviewResult> GetAsync(string code, CancellationToken cancellationToken)
    {
        code = (code ?? "").Trim();
        if (code.Length is 0 or > 20)
            return TableBillPreviewResult.UnknownTable(code);

        var builder = new SqlConnectionStringBuilder(configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Chưa cấu hình kết nối database."))
        { ApplicationIntent = ApplicationIntent.ReadOnly };
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        // ReadCommitted: chỉ thấy dữ liệu đã lưu, không giữ khoá sau mỗi câu đọc nên không chặn gọi món / bếp / thu ngân.
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            string tableCode, tableStatus, sessionStatus, tables;
            long sessionId;
            await using (var command = new SqlCommand(SessionQuery, connection, transaction))
            {
                command.Parameters.Add("@code", SqlDbType.VarChar, 20).Value = code;
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    return TableBillPreviewResult.UnknownTable(code);
                tableCode = reader.GetString(0);
                tableStatus = reader.GetString(1);
                if (reader.IsDBNull(2))
                    return TableBillPreviewResult.NotServing(tableCode);
                sessionId = reader.GetInt64(2);
                sessionStatus = reader.GetString(3);
                tables = reader.GetString(4);
            }

            var items = new List<BillSourceItem>();
            await using (var command = new SqlCommand(ItemsQuery, connection, transaction))
            {
                command.Parameters.Add("@root", SqlDbType.BigInt).Value = sessionId;
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                    items.Add(new BillSourceItem(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetDecimal(3),
                        reader.GetInt32(4), reader.GetString(5), reader.GetBoolean(6),
                        reader.IsDBNull(7) ? null : reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8),
                        reader.GetDateTime(9)));
            }
            return TableBillPreviewResult.Found(
                TableBillPreviewCalculator.Build(tableCode, tables, sessionId, sessionStatus, tableStatus, items));
        }
        finally
        {
            // Luôn huỷ giao dịch: xem tạm tính không bao giờ lưu thay đổi nào.
            if (transaction.Connection is not null)
                await transaction.RollbackAsync(CancellationToken.None);
        }
    }
}
