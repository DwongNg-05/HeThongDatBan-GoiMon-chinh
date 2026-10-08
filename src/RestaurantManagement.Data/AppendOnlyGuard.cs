using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace RestaurantManagement.Data;

/// <summary>
/// S1-05 Task 3: chặn ở tầng EF mọi thao tác sửa/xoá bản ghi nhật ký trước khi gửi tới database
/// (database còn có trigger chỉ-thêm và DENY cho tài khoản ứng dụng).
/// </summary>
public static class AppendOnlyGuard
{
    public const string Message = "Nhật ký chỉ được thêm, không được sửa hoặc xoá.";

    public static void Ensure<TEntity>(ChangeTracker tracker) where TEntity : class
    {
        if (tracker.Entries<TEntity>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException(Message);
    }
}
