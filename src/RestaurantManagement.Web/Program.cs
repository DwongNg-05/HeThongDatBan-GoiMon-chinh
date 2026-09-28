<<<<<<< HEAD

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RestaurantManagement.Web.Data;
using RestaurantManagement.Web.Models;
using RestaurantManagement.Web.Filters;
=======
using Microsoft.EntityFrameworkCore;
using RestaurantManagement.Data.Data;
>>>>>>> 8f2fcfccc8338f500b11e149d95ad95ad7ea5d8b

var builder = WebApplication.CreateBuilder(args);
var sqlConnectionString = Environment.GetEnvironmentVariable("RM_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(sqlConnectionString))
    sqlConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(sqlConnectionString))
    throw new InvalidOperationException("Set RM_CONNECTION_STRING or ConnectionStrings:DefaultConnection before starting the web app.");
builder.Configuration["ConnectionStrings:DefaultConnection"] = sqlConnectionString;

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
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();
builder.Services.AddResponseCompression(options => options.EnableForHttps = true);
builder.Services.AddSingleton<RestaurantManagement.Web.Services.InMemoryQuanLyMonStore>();
builder.Services.AddSingleton<RestaurantManagement.Web.Services.DemoTableCatalog>();
builder.Services.AddSingleton<RestaurantManagement.Web.Services.TableMapEventBroker>();
builder.Services.AddScoped<RestaurantManagement.Web.Services.TableDetailsService>();
builder.Services.AddScoped<RestaurantManagement.Web.Services.TableQrService>();
builder.Services.AddSingleton<RestaurantManagement.Web.Services.TableQrPdfBuilder>();
builder.Services.AddHostedService<RestaurantManagement.Web.Services.TableStatusOutboxWorker>();
builder.Services.AddDbContext<RestaurantDbContext>(options => options.UseSqlServer(sqlConnectionString));

var app = builder.Build();

>>>>>>> 8f2fcfccc8338f500b11e149d95ad95ad7ea5d8b
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseRouting();
<<<<<<< HEAD

app.UseAuthentication();
=======
app.UseResponseCompression();
>>>>>>> 8f2fcfccc8338f500b11e149d95ad95ad7ea5d8b
app.UseAuthorization();
app.MapStaticAssets();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();
<<<<<<< HEAD

// Tạo tài khoản nhân viên mẫu nếu chưa có
using (var scope = app.Services.CreateScope())
{
    var userManager = scope.ServiceProvider
        .GetRequiredService<UserManager<TaiKhoan>>();
=======
app.MapControllers();
app.MapRazorPages();
>>>>>>> 8f2fcfccc8338f500b11e149d95ad95ad7ea5d8b

    await SeedData.TaoTaiKhoanNhanVienAsync(userManager);
}

app.Run();