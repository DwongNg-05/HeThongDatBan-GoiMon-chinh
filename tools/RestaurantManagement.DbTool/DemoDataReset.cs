using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;

namespace RestaurantManagement.DbTool;

internal static class DemoDataReset
{
    internal static async Task Run(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var database = builder.InitialCatalog;
        if (!database.EndsWith("_Dev", StringComparison.OrdinalIgnoreCase)
            && !database.EndsWith("_Demo", StringComparison.OrdinalIgnoreCase)
            && !database.EndsWith("_Staging", StringComparison.OrdinalIgnoreCase)
            && !database.Contains("_Test_", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("reset-demo chỉ chạy trên database có hậu tố _Dev, _Demo, _Staging hoặc _Test_.");

        var timer = Stopwatch.StartNew();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            await using (var lockCommand = new SqlCommand(
                "DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource='RestaurantDemoDataReset',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; IF @r<0 THROW 51219,'Demo reset lock unavailable.',1;",
                connection, transaction) { CommandTimeout = 15 })
                await lockCommand.ExecuteNonQueryAsync();

            var script = await File.ReadAllTextAsync(Path.Combine(DatabaseTool.Root, "database", "seeds", "ResetDemo.sql"));
            await using var command = new SqlCommand(script, connection, transaction) { CommandTimeout = 110 };
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) throw new InvalidOperationException("Lệnh reset không trả về số lượng dữ liệu.");
            var counts = new DemoResetCounts(
                reader.GetInt32(reader.GetOrdinal("Areas")),
                reader.GetInt32(reader.GetOrdinal("Tables")),
                reader.GetInt32(reader.GetOrdinal("Categories")),
                reader.GetInt32(reader.GetOrdinal("Dishes")),
                reader.GetInt32(reader.GetOrdinal("Accounts")),
                reader.GetInt32(reader.GetOrdinal("Reservations")),
                reader.GetInt32(reader.GetOrdinal("PendingReservations")));
            if (counts != DemoResetCounts.Expected)
                throw new InvalidOperationException($"Số lượng mẫu sai, giao dịch đã hoàn tác: {counts}.");
            await reader.CloseAsync();

            timer.Stop();
            if (timer.Elapsed >= TimeSpan.FromMinutes(2))
                throw new TimeoutException($"Khôi phục mất {timer.Elapsed.TotalSeconds:F1} giây, vượt giới hạn 2 phút; giao dịch đã hoàn tác.");

            await transaction.CommitAsync();
            Console.WriteLine($"Khôi phục mẫu thành công trong {timer.Elapsed.TotalSeconds:F2} giây: 3 khu vực, 25 bàn, 5 nhóm món, 60 món, 4 tài khoản, 20 lượt đặt (20 chờ xác nhận).");
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private sealed record DemoResetCounts(int Areas, int Tables, int Categories, int Dishes, int Accounts, int Reservations, int PendingReservations)
    {
        internal static DemoResetCounts Expected { get; } = new(3, 25, 5, 60, 4, 20, 20);
    }
}
