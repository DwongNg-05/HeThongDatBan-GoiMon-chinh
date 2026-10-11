internal static class KitchenConnectionTests
{
    internal static void Run(Action<bool, string> check)
    {
        var repositoryRoot = FindRepositoryRoot(new DirectoryInfo(AppContext.BaseDirectory))
            ?? FindRepositoryRoot(new DirectoryInfo(Directory.GetCurrentDirectory()))
            ?? throw new DirectoryNotFoundException("Could not find repository root for kitchen connection checks.");
        var screen = File.ReadAllText(Path.Combine(repositoryRoot, "src", "RestaurantManagement.Web", "Views", "Kitchen", "Index.cshtml"));
        var tickets = File.ReadAllText(Path.Combine(repositoryRoot, "src", "RestaurantManagement.Web", "Views", "Kitchen", "_Tickets.cshtml"));

        check(screen.Contains("Mất kết nối", StringComparison.Ordinal)
            && screen.Contains("dữ liệu đã nhận trước khi mất kết nối", StringComparison.Ordinal)
            && screen.Contains("Đang tự kết nối lại", StringComparison.Ordinal),
            "Kitchen screen warns when disconnected and identifies visible tickets as stale");
        check(screen.Contains("if (!response.ok) throw", StringComparison.Ordinal)
            && screen.Contains("if (!parsed.querySelector('[data-kitchen-queue-snapshot]')) throw", StringComparison.Ordinal)
            && screen.Contains("showDisconnected();", StringComparison.Ordinal),
            "Failed or invalid snapshots keep current tickets visible under the disconnected warning");
        check(screen.Contains("Kết nối đã khôi phục", StringComparison.Ordinal)
            && screen.Contains("window.setTimeout(refreshQueue, 3000)", StringComparison.Ordinal),
            "Kitchen screen retries automatically and reports restored connection");
        check(screen.Contains("const arrivals = newIds.filter(id => !oldIds.has(id))", StringComparison.Ordinal)
            && screen.Contains("list.innerHTML = html", StringComparison.Ordinal)
            && screen.IndexOf("list.innerHTML = html", StringComparison.Ordinal)
                > screen.IndexOf("if (!parsed.querySelector('[data-kitchen-queue-snapshot]')) throw", StringComparison.Ordinal),
            "Reconnected snapshots add missed tickets without treating retained tickets as new");
        check(tickets.StartsWith("@model", StringComparison.Ordinal)
            && tickets.Contains("data-kitchen-queue-snapshot", StringComparison.Ordinal)
            && screen.Contains("lastSuccessfulSync = new Date()", StringComparison.Ordinal)
            && !screen.Contains("ReceivedAt = new Date", StringComparison.Ordinal),
            "Sync time is tracked separately and never replaces original ticket receive times");
    }

    private static string? FindRepositoryRoot(DirectoryInfo? directory)
    {
        for (var current = directory; current is not null; current = current.Parent)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src", "RestaurantManagement.Web"))) return current.FullName;
        }
        return null;
    }
}
