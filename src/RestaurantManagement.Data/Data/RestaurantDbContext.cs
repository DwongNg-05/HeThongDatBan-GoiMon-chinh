using Microsoft.EntityFrameworkCore;
using RestaurantManagement.Data.Models;
using RestaurantManagement.Data.Entities;

namespace RestaurantManagement.Data.Data
{
    public class RestaurantDbContext : DbContext
    {
        public RestaurantDbContext(DbContextOptions<RestaurantDbContext> options) : base(options) { }

        public DbSet<AreaEntity>? AreaEntities { get; set; }

        public DbSet<DishCategory> DishCategories { get; set; } = null!;
        public DbSet<Dish> Dishes { get; set; } = null!;
        public DbSet<Order> Orders { get; set; } = null!;
        public DbSet<OrderLine> OrderLines { get; set; } = null!;
        public DbSet<PriceChangeLog> PriceChangeLogs { get; set; } = null!;

        // S1-05 Task 3: nhật ký thay đổi giá chỉ được thêm, không được sửa hoặc xoá qua EF.
        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            AppendOnlyGuard.Ensure<PriceChangeLog>(ChangeTracker);
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            AppendOnlyGuard.Ensure<PriceChangeLog>(ChangeTracker);
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<DishCategory>(entity =>
            {
                entity.ToTable("MenuCategories", "dbo");
                entity.Property(n => n.Name).HasColumnName("Name").HasMaxLength(50);
                entity.Property(n => n.IsActive).HasColumnName("IsActive");
                entity.Property(n => n.SortOrder).HasColumnName("SortOrder");
                entity.Property(n => n.DefaultImagePath).HasColumnName("DefaultImagePath").HasMaxLength(500).IsRequired(false);
            });
            modelBuilder.Entity<Dish>(entity =>
            {
                entity.ToTable("MenuItems", "dbo");
                entity.Property(m => m.Name).HasColumnName("Name").HasMaxLength(150);
                entity.Property(m => m.CategoryId).HasColumnName("CategoryId");
                entity.Property(m => m.PriceVnd).HasColumnName("Price")
                    .HasConversion<decimal>().HasColumnType("decimal(18,0)");
                entity.Property(m => m.Unit).HasColumnName("Unit").HasMaxLength(30);
                entity.Property(m => m.ShortDescription).HasColumnName("Description").HasMaxLength(1000).IsRequired(false);
                entity.Property(m => m.PrepMinutes).HasColumnName("EstimatedPrepMinutes");
                entity.Property(m => m.ImagePath).HasColumnName("ImagePath").HasMaxLength(500).IsRequired(false);
                entity.Property(m => m.Status).HasColumnName("IsActive")
                    .HasConversion(value => value == DishStatus.OnSale,
                        value => value ? DishStatus.OnSale : DishStatus.Discontinued);
            });
            modelBuilder.Entity<Order>().ToTable("DonHangs");
            modelBuilder.Entity<OrderLine>().ToTable("DongHangs");
            modelBuilder.Entity<PriceChangeLog>().ToTable("GhiNhanThayDoiGias");
        }
    }
}
