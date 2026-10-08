using System.Data;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models;

namespace RestaurantManagement.Web.Services;

/// <summary>
/// Sơ đồ bàn đọc trực tiếp dữ liệu của màn hình "Khu vực &amp; bàn" (dbo.Areas, dbo.DiningTables).
/// Chỉ hiển thị khu vực đang hoạt động và bàn đang sử dụng, theo đúng thứ tự hiển thị đã cấu hình.
/// </summary>
public sealed class TableMapStore(IConfiguration configuration)
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Chưa cấu hình ConnectionStrings:DefaultConnection.");

    private const string TablesSql = """
        SELECT t.Code, a.Id, a.Name, t.MaxCapacity, t.Status, t.StatusChangedAt
        FROM dbo.DiningTables t
        JOIN dbo.Areas a ON a.Id = t.AreaId
        WHERE t.IsActive = 1 AND a.IsActive = 1
        ORDER BY a.SortOrder, a.Name, a.Id, t.SortOrder, t.Code;
        """;

    public async Task<TableMapViewModel> GetMapAsync(int? areaId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var areas = new List<(int Id, string Name)>();
        await using (var areaCommand = new SqlCommand(
            "SELECT Id, Name FROM dbo.Areas WHERE IsActive = 1 ORDER BY SortOrder, Name, Id;", connection))
        await using (var reader = await areaCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                areas.Add((reader.GetInt32(0), reader.GetString(1)));
        }

        var tables = await ReadTablesAsync(connection, cancellationToken);
        var counts = tables.GroupBy(table => table.AreaId).ToDictionary(group => group.Key, group => group.Count());
        var areaTabs = areas
            .Select(area => new RestaurantManagement.Web.Models.TableMapArea(area.Id, area.Name, counts.GetValueOrDefault(area.Id)))
            .ToArray();

        var selected = areaId is null ? null : areaTabs.FirstOrDefault(area => area.Id == areaId);
        return new TableMapViewModel
        {
            Areas = areaTabs,
            SelectedAreaId = selected?.Id,
            Tables = (selected is null ? tables : tables.Where(table => table.AreaId == selected.Id))
                .Select(table => table.Card).ToArray(),
            TotalCount = tables.Count
        };
    }

    public async Task<IReadOnlyList<DiningTableCard>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return (await ReadTablesAsync(connection, cancellationToken)).Select(table => table.Card).ToArray();
    }

    /// <summary>
    /// Ghi trạng thái mới vào dbo.DiningTables. Trigger tr_DiningTables_StatusChanged ghi sự kiện,
    /// TableStatusOutboxWorker đọc sự kiện đó và đẩy tới mọi sơ đồ đang mở.
    /// </summary>
    /// <returns>Bàn sau khi cập nhật, hoặc null nếu mã bàn/trạng thái không hợp lệ.</returns>
    public async Task<DiningTableCard?> UpdateStatusAsync(string code, string? status, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code) || !TableStatusDisplay.TryNormalize(status, out var normalized))
            return null;

        // dbo.DiningTables có trigger nên OUTPUT phải ghi vào biến bảng (OUTPUT trần bị SQL Server từ chối, lỗi 334).
        const string sql = """
            SET NOCOUNT ON;
            DECLARE @changed TABLE (Code varchar(20), AreaId int, AreaName nvarchar(80), MaxCapacity int,
                Status varchar(20), StatusChangedAt datetime2(3));
            UPDATE t
            SET Status = @Status,
                StatusChangedAt = CASE WHEN t.Status = @Status THEN t.StatusChangedAt ELSE SYSUTCDATETIME() END
            OUTPUT inserted.Code, a.Id, a.Name, inserted.MaxCapacity, inserted.Status, inserted.StatusChangedAt
            INTO @changed
            FROM dbo.DiningTables t
            JOIN dbo.Areas a ON a.Id = t.AreaId
            WHERE t.Code = @Code AND t.IsActive = 1 AND a.IsActive = 1;
            SELECT Code, AreaId, AreaName, MaxCapacity, Status, StatusChangedAt FROM @changed;
            """;
        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Code", SqlDbType.VarChar, 20).Value = code.Trim().ToUpperInvariant();
        command.Parameters.Add("@Status", SqlDbType.VarChar, 20).Value = normalized;
        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRow(reader).Card : null;
    }

    private static async Task<List<TableRow>> ReadTablesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(TablesSql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var tables = new List<TableRow>();
        while (await reader.ReadAsync(cancellationToken))
            tables.Add(ReadRow(reader));
        return tables;
    }

    private static TableRow ReadRow(SqlDataReader reader)
    {
        var status = reader.GetString(4);
        var display = TableStatusDisplay.From(status);
        var changedAt = new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc));
        var card = new DiningTableCard(reader.GetString(0), reader.GetString(2), reader.GetInt32(3),
            status, display.Label, display.CssClass, changedAt);
        return new TableRow(reader.GetInt32(1), card);
    }

    private sealed record TableRow(int AreaId, DiningTableCard Card);
}
