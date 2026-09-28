using Microsoft.EntityFrameworkCore;
using RestaurantManagement.Data.Models;
using RestaurantManagement.Data.Entities;

namespace RestaurantManagement.Data.Data
{
    public class RestaurantDbContext : DbContext
    {
        public RestaurantDbContext(DbContextOptions<RestaurantDbContext> options) : base(options) { }

        public DbSet<KhuVuc>? KhuVucs { get; set; }

        public DbSet<NhomMon> NhomMons { get; set; } = null!;
        public DbSet<MonAn> MonAns { get; set; } = null!;
        public DbSet<DonHang> DonHangs { get; set; } = null!;
        public DbSet<DongHang> DongHangs { get; set; } = null!;
        public DbSet<GhiNhanThayDoiGia> GhiNhanThayDoiGias { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<NhomMon>(entity =>
            {
                entity.ToTable("MenuCategories", "dbo");
                entity.Property(n => n.Ten).HasColumnName("Name").HasMaxLength(50);
                entity.Property(n => n.DangSuDung).HasColumnName("IsActive");
                entity.Property(n => n.ThuTuHienThi).HasColumnName("SortOrder");
            });
            modelBuilder.Entity<MonAn>(entity =>
            {
                entity.ToTable("MenuItems", "dbo");
                entity.Property(m => m.Ten).HasColumnName("Name").HasMaxLength(150);
                entity.Property(m => m.NhomMonId).HasColumnName("CategoryId");
                entity.Property(m => m.GiaBanVnd).HasColumnName("Price")
                    .HasConversion<decimal>().HasColumnType("decimal(18,0)");
                entity.Property(m => m.DonViTinh).HasColumnName("Unit").HasMaxLength(30);
                entity.Property(m => m.MoTaNgan).HasColumnName("Description").HasMaxLength(1000).IsRequired(false);
                entity.Property(m => m.ThoiGianCheBienPhut).HasColumnName("EstimatedPrepMinutes");
                entity.Property(m => m.TrangThai).HasColumnName("IsActive")
                    .HasConversion(value => value == TrangThaiMon.DangBan,
                        value => value ? TrangThaiMon.DangBan : TrangThaiMon.NgungBan);
            });
            modelBuilder.Entity<DonHang>().ToTable("DonHangs");
            modelBuilder.Entity<DongHang>().ToTable("DongHangs");
            modelBuilder.Entity<GhiNhanThayDoiGia>().ToTable("GhiNhanThayDoiGias");
        }
    }
}
