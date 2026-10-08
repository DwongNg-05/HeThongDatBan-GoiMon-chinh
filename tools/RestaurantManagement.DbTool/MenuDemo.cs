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

    /// <summary>Hậu tố tên của 200 món kiểm thử hiệu năng (S2-01 Task 4), ví dụ "Cơm rang (mẫu 006)".</summary>
    internal const string LoadTestNamePattern = "N'% (mẫu [0-9][0-9][0-9])'";

    /// <summary>
    /// S2-01 Task 4: nạp 200 món kiểm thử thuộc 8 nhóm (20 món hết trong ngày, 8 món tên/mô tả dài).
    /// Chạy lại an toàn; không chạy trên database sản xuất.
    /// </summary>
    internal static async Task SeedMenu200(string connection)
    {
        await using var cn = new SqlConnection(connection);
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            await Batches(cn, tx, await File.ReadAllTextAsync(Path.Combine(Root, "database", "seeds", "MenuLoadTest200.sql")));
            await tx.CommitAsync();
        }
        catch { await tx.RollbackAsync(); throw; }
        await using var count = new SqlCommand($"SELECT COUNT(*) FROM dbo.vw_PublicMenu WHERE Name LIKE {LoadTestNamePattern};", cn);
        Console.WriteLine($"Menu load test ready: {await count.ExecuteScalarAsync()} sample dishes on the public menu (8 groups, 20 sold out today). Hide them with: hide-menu-200");
    }

    /// <summary>Ẩn 200 món kiểm thử (IsActive=0) và các nhóm chỉ còn món kiểm thử. Không xoá vì món có thể đã có trong đơn hàng.</summary>
    internal static async Task HideMenu200(string connection)
    {
        await Execute(connection, $"""
            UPDATE dbo.MenuItems SET IsActive=0,IsSoldOut=0,UpdatedAt=SYSUTCDATETIME() WHERE Name LIKE {LoadTestNamePattern} AND IsActive=1;
            UPDATE c SET IsActive=0 FROM dbo.MenuCategories c
            WHERE c.Name IN (N'Món nướng',N'Hải sản',N'Món chay') AND c.IsActive=1
              AND NOT EXISTS(SELECT 1 FROM dbo.MenuItems i WHERE i.CategoryId=c.Id AND i.IsActive=1);
            """);
        Console.WriteLine("Menu load test dishes hidden.");
    }
}
