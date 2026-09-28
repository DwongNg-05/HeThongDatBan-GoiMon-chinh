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

            modelBuilder.Entity<NhomMon>().ToTable("NhomMons");
            modelBuilder.Entity<MonAn>().ToTable("MonAns");
            modelBuilder.Entity<DonHang>().ToTable("DonHangs");
            modelBuilder.Entity<DongHang>().ToTable("DongHangs");
            modelBuilder.Entity<GhiNhanThayDoiGia>().ToTable("GhiNhanThayDoiGias");
        }
    }
}
