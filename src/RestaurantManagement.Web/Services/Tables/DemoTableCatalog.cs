using System.Collections.Concurrent;
using RestaurantManagement.Web.Models;

namespace RestaurantManagement.Web.Services;

/// <summary>Dữ liệu bàn mẫu trong bộ nhớ — chỉ còn dùng cho kiểm thử và chi tiết bàn dự phòng khi chạy Development không có SQL.
/// Sơ đồ bàn thật đọc từ <see cref="TableMapStore"/> (dbo.Areas, dbo.DiningTables).</summary>
public sealed class DemoTableCatalog
{
    private static readonly string[] AreaNames = ["Tầng một", "Tầng hai", "Sân vườn"];
    private static readonly string[] Statuses = ["Available", "Serving", "Reserved", "Cleaning"];
    private readonly ConcurrentDictionary<string, TableState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly DiningTableCard[] _tableInfo;
    private readonly object _stateLock = new();

    public DemoTableCatalog()
    {
        _tableInfo = Enumerable.Range(1, 60).Select(number =>
        {
            var areaIndex = (number - 1) / 20;
            var areaTableNumber = (number - 1) % 20 + 1;
            return new DiningTableCard(
                $"{(char)('A' + areaIndex)}{areaTableNumber:00}",
                AreaNames[areaIndex],
                areaTableNumber % 10 == 0 ? 8 : areaTableNumber % 3 == 0 ? 6 : 4,
                string.Empty,
                string.Empty,
                string.Empty,
                DateTimeOffset.UtcNow);
        }).ToArray();

        foreach (var (table, number) in _tableInfo.Select((table, index) => (table, index: index + 1)))
        {
            var statusIndex = number % 13 == 0 ? 3 : number % 7 == 0 ? 2 : number % 4 == 0 ? 1 : 0;
            _states[table.Code] = new TableState(Statuses[statusIndex], DateTimeOffset.UtcNow);
        }
    }

    public IReadOnlyList<DiningTableCard> GetAll() => _tableInfo.Select(ToCard).ToArray();

    public TableMapViewModel Get(string? area)
    {
        var selectedArea = AreaNames.FirstOrDefault(name => string.Equals(name, area?.Trim(), StringComparison.OrdinalIgnoreCase));
        var tables = GetAll();
        var areas = AreaNames.Select((name, index) => new RestaurantManagement.Web.Models.TableMapArea(index + 1, name, tables.Count(table => table.Area == name))).ToArray();
        return new TableMapViewModel
        {
            Tables = selectedArea is null ? tables : tables.Where(table => table.Area == selectedArea).ToArray(),
            Areas = areas,
            SelectedAreaId = areas.FirstOrDefault(item => item.Name == selectedArea)?.Id,
            TotalCount = tables.Count
        };
    }

    public bool TryUpdateStatus(string code, string? status, out DiningTableCard? table, out TableStatusTransition? transition)
    {
        table = null;
        transition = null;
        if (!_states.ContainsKey(code) || !TableStatusDisplay.TryNormalize(status, out var normalized))
            return false;

        var tableInfo = _tableInfo.First(info => string.Equals(info.Code, code, StringComparison.OrdinalIgnoreCase));
        lock (_stateLock)
        {
            var previous = ToCard(tableInfo);
            if (previous.Status == normalized)
            {
                table = previous;
                return true;
            }

            _states[tableInfo.Code] = new TableState(normalized, DateTimeOffset.UtcNow);
            table = ToCard(tableInfo);
            transition = TableStatusTransition.From(previous, table);
        }
        return true;
    }

    public void ApplyDatabaseStatus(string code, string status, DateTimeOffset changedAtUtc)
    {
        if (!_states.ContainsKey(code) || !TableStatusDisplay.TryNormalize(status, out var normalized))
            return;

        var info = _tableInfo.First(table => string.Equals(table.Code, code, StringComparison.OrdinalIgnoreCase));
        lock (_stateLock)
        {
            var current = _states[info.Code];
            if (current.ChangedAtUtc <= changedAtUtc)
                _states[info.Code] = new TableState(normalized, changedAtUtc);
        }
    }

    private DiningTableCard ToCard(DiningTableCard info)
    {
        TableState state;
        lock (_stateLock)
            state = _states[info.Code];
        var display = TableStatusDisplay.From(state.Status);
        return info with { Status = state.Status, StatusLabel = display.Label, StatusClass = display.CssClass, ChangedAtUtc = state.ChangedAtUtc };
    }

    private sealed record TableState(string Status, DateTimeOffset ChangedAtUtc);
}
