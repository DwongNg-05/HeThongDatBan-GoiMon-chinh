internal static class KitchenReadabilityTests
{
    internal static void Run(Action<bool, string> check)
    {
        var repositoryRoot = FindRepositoryRoot(new DirectoryInfo(AppContext.BaseDirectory))
            ?? FindRepositoryRoot(new DirectoryInfo(Directory.GetCurrentDirectory()))
            ?? throw new DirectoryNotFoundException("Could not find the repository root for kitchen readability checks.");
        var screen = File.ReadAllText(Path.Combine(repositoryRoot, "src", "RestaurantManagement.Web", "Views", "Kitchen", "Index.cshtml"));
        var tickets = File.ReadAllText(Path.Combine(repositoryRoot, "src", "RestaurantManagement.Web", "Views", "Kitchen", "_Tickets.cshtml"));
        var styles = File.ReadAllText(Path.Combine(repositoryRoot, "src", "RestaurantManagement.Web", "wwwroot", "css", "site.css"));

        check(styles.Contains(".kitchen-dish-name{display:block;min-width:0;font-size:20px", StringComparison.Ordinal),
            "Kitchen dish names use at least 20px in the standard mode");
        check(screen.Contains("kitchen-large-text-toggle", StringComparison.Ordinal)
            && screen.Contains("aria-pressed=\"false\"", StringComparison.Ordinal)
            && screen.Contains("sessionStorage", StringComparison.Ordinal),
            "Kitchen large-text mode is toggleable, accessible, and remembered for the tab session");
        check(styles.Contains(".kitchen-dish-name{font-size:30px", StringComparison.Ordinal)
            && styles.Contains(".kitchen-large-text .kitchen-col-notes", StringComparison.Ordinal),
            "Large-text mode enlarges dish names and supporting ticket details");
        check(styles.Contains(".kitchen-col-notes{width:30%;white-space:normal;overflow-wrap:anywhere;word-break:break-word}", StringComparison.Ordinal)
            && tickets.Contains("@item.ItemName", StringComparison.Ordinal),
            "Long dish names and notes wrap without clipping ticket content");
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
