

using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RestaurantManagement.Web.Models;

namespace RestaurantManagement.Web.Data
{
    public class ApplicationDbContext
        : IdentityDbContext<TaiKhoan>
    {
        public ApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }
    }
}


