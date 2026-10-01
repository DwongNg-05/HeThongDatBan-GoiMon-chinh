using Microsoft.Data.SqlClient;

namespace RestaurantManagement.DbTool;

internal static partial class DatabaseTool
{
    private static string PerformanceDatabase(string connection)
    {
        var name = new SqlConnectionStringBuilder(connection).InitialCatalog;
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^RestaurantManagement_Perf_[a-f0-9]{32}$"))
            throw new ArgumentException("Performance commands require an isolated RestaurantManagement_Perf_<32 lowercase hex> database.");
        return name;
    }

    internal static async Task SeedMapPerformance(string connection)
    {
        PerformanceDatabase(connection);
        var count = Environment.GetEnvironmentVariable("RM_PERF_TABLE_COUNT") ?? "60";
        if (count is not ("60" or "80")) throw new ArgumentException("RM_PERF_TABLE_COUNT must be 60 or 80.");
        await Migrate(connection);
        await Seed(connection);
        await using var cn = new SqlConnection(connection);
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
        var sql = await File.ReadAllTextAsync(Path.Combine(Root, "database", "seeds", "TableMapPerformance.sql"));
        await using var command = new SqlCommand(sql, cn, tx) { CommandTimeout = 60 };
        command.Parameters.AddWithValue("@TargetCount", int.Parse(count));
        await command.ExecuteNonQueryAsync();
        await tx.CommitAsync();
        Console.WriteLine($"Performance fixture prepared: {count} tables, four statuses, future bookings and active ordered sessions.");
    }

    internal static async Task DropMapPerformance(string connection)
    {
        var name = PerformanceDatabase(connection);
        var builder = new SqlConnectionStringBuilder(connection) { InitialCatalog = "master" };
        await using var cn = new SqlConnection(builder.ConnectionString);
        await cn.OpenAsync();
        await using var command = new SqlCommand($"IF DB_ID(@Name) IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END", cn);
        command.Parameters.AddWithValue("@Name", name);
        await command.ExecuteNonQueryAsync();
        Console.WriteLine("Isolated performance database removed.");
    }
}
