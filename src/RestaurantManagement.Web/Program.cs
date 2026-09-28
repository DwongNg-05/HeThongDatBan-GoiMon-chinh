
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RestaurantManagement.Web.Data;
using RestaurantManagement.Web.Models;
using RestaurantManagement.Web.Filters;

var builder = WebApplication.CreateBuilder(args);

// Đăng ký bộ lọc bắt buộc đổi mật khẩu
builder.Services.AddScoped<BatBuocDoiMatKhauFilter>();

// Đăng ký MVC và áp dụng bộ lọc cho tất cả các trang
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.AddService<BatBuocDoiMatKhauFilter>();
});

// Kết nối SQL Server
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

// Cấu hình Identity
builder.Services
    .AddIdentity<TaiKhoan, IdentityRole>(options =>
    {
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();
// Kiểm tra SecurityStamp ở mỗi yêu cầu
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.Zero;
});

var app = builder.Build();

// Cấu hình pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

// Tạo tài khoản nhân viên mẫu nếu chưa có
using (var scope = app.Services.CreateScope())
{
    var userManager = scope.ServiceProvider
        .GetRequiredService<UserManager<TaiKhoan>>();

    await SeedData.TaoTaiKhoanNhanVienAsync(userManager);
}

app.Run();