using Microsoft.EntityFrameworkCore;
using RestaurantManagement.Data.Models;

namespace RestaurantManagement.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<User> Users { get; set; }

        // S1-05 Task 3: nhật ký chỉ được thêm, không được sửa hoặc xoá qua EF.
        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            AppendOnlyGuard.Ensure<AuditLog>(ChangeTracker);
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            AppendOnlyGuard.Ensure<AuditLog>(ChangeTracker);
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
    }
}