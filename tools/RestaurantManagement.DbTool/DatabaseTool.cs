using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace RestaurantManagement.DbTool;

internal static partial class DatabaseTool
{
    internal static readonly string Root = FindRoot();
    internal static async Task Run(string[] args)
    {
        try
        {
            var command = args.FirstOrDefault() ?? "help";
            var connection = Environment.GetEnvironmentVariable("RM_CONNECTION_STRING");
            if (command == "help" || string.IsNullOrWhiteSpace(connection))
            {
                Console.WriteLine("Commands: migrate | seed-demo | seed-login-demo | verify | maintenance | check\nSet RM_CONNECTION_STRING first. Seed commands also require RM_DEMO_PASSWORD.\nseed-login-demo accepts RM_DEMO_USERNAME and RM_DEMO_PHONE; existing accounts are preserved.\nverify creates and removes its own uniquely named test database.");
                Environment.ExitCode = command == "help" ? 0 : 1;
                return;
            }
            switch (command)
            {
                case "migrate": await Migrate(connection); break;
                case "seed-demo": await Seed(connection); break;
                case "seed-login-demo": await SeedLoginDemo(connection); break;
                case "verify": await Verification.Run(connection); break;
                case "maintenance": await Execute(connection, "EXEC dbo.usp_RunMaintenance;"); break;
                case "check": await Execute(connection, "SELECT TOP(1) Name FROM dbo.SchemaVersions;"); Console.WriteLine("Database connected."); break;
                default: throw new ArgumentException("Unknown command.");
            }
        }
        catch (Exception ex) { Console.Error.WriteLine($"Operation failed ({ex.GetType().Name}): {ex.Message}"); Environment.ExitCode = 1; }
    }

    internal static async Task Migrate(string connection)
    {
        var builder = new SqlConnectionStringBuilder(connection);
        var database = builder.InitialCatalog;
        if (!DatabaseName().IsMatch(database) || new[] { "master", "model", "msdb", "tempdb" }.Contains(database, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Use a dedicated database name with letters, digits and underscores.");
        builder.InitialCatalog = "master";
        await using (var master = new SqlConnection(builder.ConnectionString))
        {
            await master.OpenAsync();
            await using var cmd = new SqlCommand($"IF DB_ID(@name) IS NULL CREATE DATABASE [{database}];", master);
            cmd.Parameters.AddWithValue("@name", database);
            await cmd.ExecuteNonQueryAsync();
        }
        await using var cn = new SqlConnection(connection);
        await cn.OpenAsync();
        await using (var cmd = new SqlCommand("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource='RestaurantSchemaMigration',@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=30000; IF @r<0 THROW 51300,'Migration lock unavailable.',1;", cn))
            await cmd.ExecuteNonQueryAsync();
        try
        {
            await using (var cmd = new SqlCommand("IF OBJECT_ID('dbo.SchemaVersions') IS NULL CREATE TABLE dbo.SchemaVersions(Name nvarchar(200) NOT NULL PRIMARY KEY,Sha256 char(64) NOT NULL,AppliedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME());", cn))
                await cmd.ExecuteNonQueryAsync();
            foreach (var path in Directory.GetFiles(Path.Combine(Root, "database", "migrations"), "*.sql").Order(StringComparer.Ordinal))
            {
                var name = Path.GetFileName(path);
                var sql = (await File.ReadAllTextAsync(path)).Replace("\r\n", "\n");
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql)));
                await using var lookup = new SqlCommand("SELECT Sha256 FROM dbo.SchemaVersions WHERE Name=@name;", cn);
                lookup.Parameters.AddWithValue("@name", name);
                var existing = await lookup.ExecuteScalarAsync() as string;
                if (existing is not null)
                {
                    if (existing != hash) throw new InvalidOperationException($"Applied migration {name} has changed. Add a new numbered migration.");
                    Console.WriteLine($"Unchanged: {name}");
                    continue;
                }
                await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
                try
                {
                    await Batches(cn, tx, sql);
                    await using var insert = new SqlCommand("INSERT dbo.SchemaVersions(Name,Sha256) VALUES(@name,@hash);", cn, tx);
                    insert.Parameters.AddWithValue("@name", name);
                    insert.Parameters.AddWithValue("@hash", hash);
                    await insert.ExecuteNonQueryAsync();
                    await tx.CommitAsync();
                    Console.WriteLine($"Applied: {name}");
                }
                catch { await tx.RollbackAsync(); throw; }
            }
        }
        finally
        {
            await using var cmd = new SqlCommand("EXEC sys.sp_releaseapplock @Resource='RestaurantSchemaMigration',@LockOwner='Session';", cn);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    internal static async Task Seed(string connection, string? testPassword = null)
    {
        var password = testPassword ?? Environment.GetEnvironmentVariable("RM_DEMO_PASSWORD");
        if (password is null || password.Length < 8 || !password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            throw new ArgumentException("RM_DEMO_PASSWORD needs 8+ characters, including a letter and a digit.");
        await using var cn = new SqlConnection(connection);
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
        try
        {
            await using var check = new SqlCommand("SELECT COUNT(*) FROM dbo.SchemaVersions WHERE Name='DEMO_DATA';", cn, tx);
            if (Convert.ToInt32(await check.ExecuteScalarAsync()) > 0)
            {
                Console.WriteLine("Demo already seeded; existing data preserved.");
                await tx.RollbackAsync();
                return;
            }
            await Batches(cn, tx, await File.ReadAllTextAsync(Path.Combine(Root, "database", "seeds", "Demo.sql")));
            foreach (var user in new[] { "manager", "waiter", "kitchen", "cashier" })
            {
                await using var cmd = new SqlCommand("UPDATE dbo.Users SET PasswordHash=@hash,IsActive=1 WHERE UserName=@name;", cn, tx);
                cmd.Parameters.AddWithValue("@hash", BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12));
                cmd.Parameters.AddWithValue("@name", user);
                await cmd.ExecuteNonQueryAsync();
            }
            await using var marker = new SqlCommand("INSERT dbo.SchemaVersions(Name,Sha256) VALUES('DEMO_DATA',REPLICATE('0',64));", cn, tx);
            await marker.ExecuteNonQueryAsync();
            await tx.CommitAsync();
            Console.WriteLine("Demo seeded: 4 accounts, 3 areas, 25 tables, 60 menu items, 20 bookings. Password not logged.");
        }
        catch { await tx.RollbackAsync(); throw; }
    }

    internal static async Task Execute(string connection, string sql)
    {
        await using var cn = new SqlConnection(connection);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 60 };
        await cmd.ExecuteNonQueryAsync();
    }
    private static async Task Batches(SqlConnection cn, SqlTransaction tx, string sql)
    {
        foreach (var batch in GoSeparator().Split(sql).Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            await using var cmd = new SqlCommand(batch, cn, tx) { CommandTimeout = 120 };
            await cmd.ExecuteNonQueryAsync();
        }
    }
    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "database", "migrations"))) return dir.FullName;
        throw new InvalidOperationException("Run DbTool from repository root.");
    }
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]{0,100}$")] private static partial Regex DatabaseName();
    [GeneratedRegex(@"^\s*GO\s*(?:--[^\r\n]*)?$", RegexOptions.Multiline | RegexOptions.IgnoreCase)] private static partial Regex GoSeparator();
}

