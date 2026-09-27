using Microsoft.EntityFrameworkCore;
using RestaurantManagement.Data.Entities;

namespace RestaurantManagement.Data.Data
{
    public class RestaurantDbContext : DbContext
    {
        public RestaurantDbContext(
            DbContextOptions<RestaurantDbContext> options)
            : base(options)
        {
        }

        public DbSet<KhuVuc> KhuVucs { get; set; }
    }
}