using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models;

namespace RestaurantManagement.Web.Services;

/// <summary>Reads active areas and tables from the same database as table management.</summary>
public sealed class SqlTableMapReader(IConfiguration configuration) : ITableMapReader
{
    public TableMapSnapshot Read()
    {
        using var connection = new SqlConnection(configuration.GetConnectionString("DefaultConnection"));
        connection.Open();
        using var command = new SqlCommand("""
            SELECT a.Id, a.Name, t.Code, t.MaxCapacity, t.Status, t.StatusChangedAt
            FROM dbo.Areas a
            LEFT JOIN dbo.DiningTables t ON t.AreaId = a.Id AND t.IsActive = 1
            WHERE a.IsActive = 1
            ORDER BY a.SortOrder, a.Id, t.SortOrder, t.Code;
            """, connection);
        using var reader = command.ExecuteReader();
        var areas = new List<TableMapArea>();
        List<DiningTableCard>? tables = null;
        int? previousAreaId = null;
        while (reader.Read())
        {
            var areaId = reader.GetInt32(0);
            var areaName = reader.GetString(1);
            if (previousAreaId != areaId)
            {
                tables = [];
                areas.Add(new TableMapArea(areaName, tables));
                previousAreaId = areaId;
            }
            if (reader.IsDBNull(2)) continue;
            var status = reader.GetString(4);
            var display = TableStatusDisplay.From(status);
            tables!.Add(new DiningTableCard(reader.GetString(2), areaName, reader.GetInt32(3),
                status, display.Label, display.CssClass,
                new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc))));
        }
        return new TableMapSnapshot(areas);
    }
}
