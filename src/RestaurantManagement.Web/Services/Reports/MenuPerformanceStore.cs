using System.Data;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models.Reports;

namespace RestaurantManagement.Web.Services;

/// <summary>Đọc báo cáo S4-06 từ thủ tục database để cùng một nguồn số liệu với dữ liệu hóa đơn và hủy món.</summary>
public sealed class MenuPerformanceStore(IConfiguration configuration)
{
    public async Task<MenuPerformanceReport> GetAsync(DateOnly from, DateOnly to, int actorUserId, CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Chưa cấu hình kết nối database.");
        await using var connection = new SqlConnection(connectionString);
        await using var command = new SqlCommand("dbo.usp_ReportTopItems", connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        command.Parameters.Add("@FromDate", SqlDbType.Date).Value = from.ToDateTime(TimeOnly.MinValue);
        command.Parameters.Add("@ToDate", SqlDbType.Date).Value = to.ToDateTime(TimeOnly.MinValue);
        command.Parameters.Add("@ActorUserId", SqlDbType.Int).Value = actorUserId;

        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var bestByQuantity = await ReadPerformanceAsync(reader, cancellationToken);
        await reader.NextResultAsync(cancellationToken);
        var bestByRevenue = await ReadPerformanceAsync(reader, cancellationToken);
        await reader.NextResultAsync(cancellationToken);
        var mostCancelled = await ReadCancelledAsync(reader, cancellationToken);
        return new MenuPerformanceReport(from, to, bestByQuantity, bestByRevenue, mostCancelled);
    }

    private static async Task<IReadOnlyList<MenuPerformanceItem>> ReadPerformanceAsync(SqlDataReader reader, CancellationToken cancellationToken)
    {
        var rows = new List<MenuPerformanceItem>();
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new MenuPerformanceItem(reader.GetInt32(0), reader.GetString(1), reader.GetBoolean(2),
                reader.GetInt32(3), reader.GetDecimal(4)));
        return rows;
    }

    private static async Task<IReadOnlyList<CancelledMenuItem>> ReadCancelledAsync(SqlDataReader reader, CancellationToken cancellationToken)
    {
        var rows = new List<CancelledMenuItem>();
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new CancelledMenuItem(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        return rows;
    }
}
