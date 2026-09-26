using System.Data;
using System.Text;
using Microsoft.Data.SqlClient;

namespace RestaurantManagement.DbTool;

internal static partial class DatabaseTool
{
    internal static async Task SeedLoginDemo(string connection, string? password = null,
        string? userName = null, string? phone = null)
    {
        password ??= Environment.GetEnvironmentVariable("RM_DEMO_PASSWORD");
        userName ??= Environment.GetEnvironmentVariable("RM_DEMO_USERNAME") ?? "demo-manager";
        phone ??= Environment.GetEnvironmentVariable("RM_DEMO_PHONE") ?? "0000000099";
        userName = userName.Trim();
        if (string.IsNullOrWhiteSpace(password) || Encoding.UTF8.GetByteCount(password) > 72)
            throw new ArgumentException("Supply RM_DEMO_PASSWORD (1–72 UTF-8 bytes).");
        if (userName.Length is < 1 or > 50 || phone.Length != 10 || phone.Any(c => c is < '0' or > '9'))
            throw new ArgumentException("Supply a username of 1–50 characters and a 10-digit demo phone.");

        await using var cn = new SqlConnection(connection);
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            await using var cmd = new SqlCommand(await File.ReadAllTextAsync(
                Path.Combine(Root, "database", "seeds", "LoginDemo.sql")), cn, tx);
            cmd.Parameters.AddWithValue("@UserName", userName);
            cmd.Parameters.AddWithValue("@Phone", phone);
            cmd.Parameters.AddWithValue("@Hash", BCrypt.Net.BCrypt.HashPassword(password, 12));
            await cmd.ExecuteNonQueryAsync();
            await tx.CommitAsync();
            Console.WriteLine("Login demo ready. Existing accounts, passwords and menu prices preserved.");
        }
        catch { await tx.RollbackAsync(); throw; }
    }
}
