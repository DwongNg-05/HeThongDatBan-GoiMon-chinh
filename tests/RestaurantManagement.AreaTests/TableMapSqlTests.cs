using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using RestaurantManagement.Web.Services;

internal static class TableMapSqlTests
{
    public static async Task Run()
    {
        var source = Environment.GetEnvironmentVariable("RM_CONNECTION_STRING") ?? throw new InvalidOperationException("Set RM_CONNECTION_STRING.");
        var database = "RestaurantManagement_TableMapTest_" + Guid.NewGuid().ToString("N");
        var builder = new SqlConnectionStringBuilder(source) { InitialCatalog = "master" };
        await using var master = new SqlConnection(builder.ConnectionString);
        await master.OpenAsync();
        await new SqlCommand($"CREATE DATABASE [{database}]", master).ExecuteNonQueryAsync();
        builder.InitialCatalog = database;
        var connectionString = builder.ConnectionString;
        try
        {
            await Execute(connectionString, """
                CREATE ROLE restaurant_app;
                CREATE TABLE dbo.Areas(Id int PRIMARY KEY,Name nvarchar(80),SortOrder int,IsActive bit);
                CREATE TABLE dbo.DiningTables(Id int PRIMARY KEY,AreaId int,Code varchar(20),MaxCapacity int,Status varchar(20),StatusChangedAt datetime2(3),SortOrder int,IsActive bit);
                INSERT dbo.Areas VALUES(1,N'Tầng một',1,1),(2,N'Tầng hai',2,1),(3,N'Sân vườn',3,1);
                INSERT dbo.DiningTables VALUES(1,1,'A01',4,'Available',SYSUTCDATETIME(),1,1),(2,2,'B01',6,'Available',SYSUTCDATETIME(),1,1);
                """);
            var root = Directory.GetCurrentDirectory();
            foreach (var file in new[] { "012_TableStatusChangeEvents.sql", "023_TableMapStatusTimestamp.sql" })
            {
                var sql = await File.ReadAllTextAsync(Path.Combine(root, "database", "migrations", file));
                foreach (var batch in System.Text.RegularExpressions.Regex.Split(sql, @"(?im)^\s*GO\s*$"))
                    if (!string.IsNullOrWhiteSpace(batch)) await Execute(connectionString, batch);
            }
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = connectionString }).Build();
            var service = new SqlTableMapReader(configuration);
            var snapshot = service.Read();
            Require(snapshot.Areas.Count == 3 && snapshot.Areas[2].Tables.Count == 0, "SQL snapshot retains an empty area");
            Require((await service.ReadChangesAsync(long.Parse(snapshot.Cursor), default)).Tables.Count == 0, "Unchanged poll returns no tables");
            var watch = Stopwatch.StartNew();
            await Execute(connectionString, "UPDATE dbo.DiningTables SET Status='Serving' WHERE Id=1; UPDATE dbo.DiningTables SET Status='Cleaning' WHERE Id=2;");
            var changes = await service.ReadChangesAsync(long.Parse(snapshot.Cursor), default);
            Require(changes.Tables.Count == 2 && changes.Tables.Any(t => t.Status == "Serving") && changes.Tables.Any(t => t.Status == "Cleaning"), "Multiple committed SQL changes are returned");
            Require(watch.Elapsed < TimeSpan.FromSeconds(5), "SQL changes available within five seconds (server check)");
            Require(changes.Tables.All(table => table.ChangedAtUtc >= snapshot.Areas.SelectMany(a => a.Tables).First(previous => previous.Code == table.Code).ChangedAtUtc), "SQL trigger records a fresh change timestamp");
            await Execute(connectionString, "UPDATE dbo.DiningTables SET Status='Serving' WHERE Id=1;");
            Require((await service.ReadChangesAsync(long.Parse(changes.Cursor), default)).Tables.Count == 0, "Same-status update does not emit an event");
            await Task.WhenAll(Execute(connectionString, "UPDATE dbo.DiningTables SET Status='Reserved' WHERE Id=1;"), Execute(connectionString, "UPDATE dbo.DiningTables SET Status='Available' WHERE Id=1;"));
            var concurrent = await service.ReadChangesAsync(long.Parse(changes.Cursor), default);
            var latest = service.Read().Areas.SelectMany(a => a.Tables).Single(t => t.Code == "A01");
            Require(concurrent.Tables.Count == 1 && concurrent.Tables[0].Status == latest.Status, "Concurrent writes to one table collapse to last committed state");
            Require((await service.ReadChangesAsync(long.Parse(concurrent.Cursor), default)).Tables.Count == 0, "Acknowledged changes are not replayed");
            await TableDetailsSqlTests.Run(connectionString, configuration);
            Console.WriteLine("Table map SQL integration checks passed.");
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await new SqlCommand($"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}];", master).ExecuteNonQueryAsync();
        }
    }
    private static async Task Execute(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await new SqlCommand(sql, connection).ExecuteNonQueryAsync();
    }
    private static void Require(bool value, string label)
    {
        if (!value) throw new Exception("FAIL: " + label);
        Console.WriteLine("PASS: " + label);
    }
}
