<<<<<<< HEAD

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RestaurantManagement.Web.Data;
using RestaurantManagement.Web.Models;
using RestaurantManagement.Web.Filters;
=======
using Microsoft.EntityFrameworkCore;
using RestaurantManagement.Data.Data;
<<<<<<< HEAD
using RestaurantManagement.Data.Models;
=======
>>>>>>> 8f2fcfccc8338f500b11e149d95ad95ad7ea5d8b
>>>>>>> 064a089b26bab3c3b077c41262a45b33e2ea8d96

var builder = WebApplication.CreateBuilder(args);
var sqlConnectionString = Environment.GetEnvironmentVariable("RM_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(sqlConnectionString))
    sqlConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(sqlConnectionString))
    sqlConnectionString = "Data Source=restaurant.db";
builder.Configuration["ConnectionStrings:DefaultConnection"] = sqlConnectionString;

<<<<<<< HEAD
// Add services to the container.
=======
<<<<<<< HEAD
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
=======
>>>>>>> 064a089b26bab3c3b077c41262a45b33e2ea8d96
builder.Services.AddControllersWithViews();
// Support Razor Pages
builder.Services.AddRazorPages();

// register existing demo services
builder.Services.AddSingleton<RestaurantManagement.Web.Services.DemoTableCatalog>();
builder.Services.AddSingleton<RestaurantManagement.Web.Services.TableMapEventBroker>();
builder.Services.AddScoped<RestaurantManagement.Web.Services.TableDetailsService>();
builder.Services.AddHostedService<RestaurantManagement.Web.Services.TableStatusOutboxWorker>();

// Register DbContext for SQLite and Sql store
builder.Services.AddDbContext<RestaurantManagement.Data.Data.RestaurantDbContext>(options =>
    options.UseSqlite(sqlConnectionString));

// Register IHttpContextAccessor to capture current user in the SQL store
builder.Services.AddHttpContextAccessor();

// Register SQL-backed store as the IQuanLyMonStore implementation
builder.Services.AddScoped<RestaurantManagement.Web.Services.IQuanLyMonStore, RestaurantManagement.Web.Services.SqlQuanLyMonStore>();

// In-memory store used by the ThucDon and GoiMon Razor Pages
builder.Services.AddSingleton<RestaurantManagement.Web.Services.InMemoryQuanLyMonStore>();

var app = builder.Build();

<<<<<<< HEAD
// Configure the HTTP request pipeline.
=======
>>>>>>> 8f2fcfccc8338f500b11e149d95ad95ad7ea5d8b
>>>>>>> 064a089b26bab3c3b077c41262a45b33e2ea8d96
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseRouting();
<<<<<<< HEAD

=======
<<<<<<< HEAD

app.UseAuthentication();
=======
app.UseResponseCompression();
>>>>>>> 8f2fcfccc8338f500b11e149d95ad95ad7ea5d8b
>>>>>>> 064a089b26bab3c3b077c41262a45b33e2ea8d96
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();
<<<<<<< HEAD

=======
<<<<<<< HEAD

// Tạo tài khoản nhân viên mẫu nếu chưa có
using (var scope = app.Services.CreateScope())
{
    var userManager = scope.ServiceProvider
        .GetRequiredService<UserManager<TaiKhoan>>();
=======
app.MapControllers();
>>>>>>> 064a089b26bab3c3b077c41262a45b33e2ea8d96
app.MapRazorPages();
>>>>>>> 8f2fcfccc8338f500b11e149d95ad95ad7ea5d8b

<<<<<<< HEAD
=======
    await SeedData.TaoTaiKhoanNhanVienAsync(userManager);
}

>>>>>>> 064a089b26bab3c3b077c41262a45b33e2ea8d96
app.Run();