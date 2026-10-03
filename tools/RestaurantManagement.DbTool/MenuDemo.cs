using System.Data;
using Microsoft.Data.SqlClient;

namespace RestaurantManagement.DbTool;

internal static partial class DatabaseTool
{
    /// <summary>S2-01: nạp nhóm món, ảnh, mô tả, giá VND và trạng thái bán cho thực đơn công khai. Chạy lại an toàn.</summary>
    internal static async Task SeedMenuDemo(string connection)
    {
        await using var cn = new SqlConnection(connection);
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            await Batches(cn, tx, await File.ReadAllTextAsync(Path.Combine(Root, "database", "seeds", "PublicMenuDemo.sql")));
            await tx.CommitAsync();
            Console.WriteLine("Public menu demo ready: 5 groups with images, 16 dishes on sale, 1 stopped dish. Existing dishes preserved.");
        }
        catch { await tx.RollbackAsync(); throw; }
    }
}
