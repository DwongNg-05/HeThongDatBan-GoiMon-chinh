using RestaurantManagement.Web.Models;

namespace RestaurantManagement.Web.Services;

/// <summary>Development display data until the web app is connected to dbo.vw_TableMap.</summary>
public sealed class DemoTableCatalog
{
    private static readonly string[] AreaNames = ["Tầng một", "Tầng hai", "Sân vườn"];
    private static readonly string[] Statuses = ["Available", "Serving", "Reserved", "Cleaning"];
    private static readonly string[] StatusLabels = ["Trống", "Đang phục vụ", "Đã đặt", "Đang dọn"];

    public TableMapViewModel Get(string? area)
    {
        var tables = Enumerable.Range(1, 60).Select(number =>
        {
            var areaIndex = (number - 1) / 20;
            var areaTableNumber = (number - 1) % 20 + 1;
            var statusIndex = number % 13 == 0 ? 3 : number % 7 == 0 ? 2 : number % 4 == 0 ? 1 : 0;
            return new DiningTableCard(
                $"{(char)('A' + areaIndex)}{areaTableNumber:00}",
                AreaNames[areaIndex],
                areaTableNumber % 10 == 0 ? 8 : areaTableNumber % 3 == 0 ? 6 : 4,
                Statuses[statusIndex],
                StatusLabels[statusIndex]);
        }).ToArray();

        var selectedArea = AreaNames.FirstOrDefault(name => string.Equals(name, area?.Trim(), StringComparison.OrdinalIgnoreCase));
        return new TableMapViewModel
        {
            Tables = selectedArea is null ? tables : tables.Where(table => table.Area == selectedArea).ToArray(),
            Areas = AreaNames,
            SelectedArea = selectedArea,
            TotalCount = tables.Length
        };
    }
}
