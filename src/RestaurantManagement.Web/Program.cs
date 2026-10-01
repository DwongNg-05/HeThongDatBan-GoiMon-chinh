using RestaurantManagement.Web.Models;
using RestaurantManagement.Web.Authentication;
using Microsoft.EntityFrameworkCore;
using RestaurantManagement.Data.Data;
using RestaurantManagement.Data.Models;
using RestaurantManagement.Data;
using RestaurantManagement.Web.Services;

var builder = WebApplication.CreateBuilder(args);
var sqlConnectionString = Environment.GetEnvironmentVariable("RM_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(sqlConnectionString))
    sqlConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(sqlConnectionString))
    throw new InvalidOperationException("Set RM_CONNECTION_STRING or ConnectionStrings:DefaultConnection to a SQL Server connection string before starting the web app.");
builder.Configuration["ConnectionStrings:DefaultConnection"] = sqlConnectionString;

builder.Services.AddScoped<AuditLogService>();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
builder.Services.AddScoped(_ => new ManagementStore(sqlConnectionString));
builder.Services.AddScoped(_ => new LoginSessionStore(sqlConnectionString));
// S1-05: nhật ký bảo mật riêng (dbo.SecurityAuditLogs).
builder.Services.AddScoped(_ => new SecurityAuditStore(sqlConnectionString));
builder.Services.AddScoped<IdleSessionEvents>();
builder.Services.AddScoped(_ => new PasswordChangeStore(sqlConnectionString));
builder.Services.AddScoped<IEmployeeAccountStore>(_ => new SqlEmployeeAccountStore(sqlConnectionString));
builder.Services.AddControllersWithViews(options => options.Filters.Add<SessionActivityFilter>());
// Support Razor Pages
builder.Services.AddRazorPages();

// register existing demo services
builder.Services.AddSingleton<RestaurantManagement.Web.Services.DemoTableCatalog>();
builder.Services.AddSingleton<RestaurantManagement.Web.Services.TableMapEventBroker>();
builder.Services.AddScoped<RestaurantManagement.Web.Services.TableDetailsService>();
builder.Services.AddScoped<RestaurantManagement.Web.Services.TableQrService>();
builder.Services.AddSingleton<RestaurantManagement.Web.Services.TableQrPdfBuilder>();
builder.Services.AddHostedService<RestaurantManagement.Web.Services.TableStatusOutboxWorker>();

// Use the same SQL Server database as the controllers and background worker.
builder.Services.AddDbContext<RestaurantManagement.Data.Data.RestaurantDbContext>(options =>
    options.UseSqlServer(sqlConnectionString));

// Register IHttpContextAccessor to capture current user in the SQL store
builder.Services.AddHttpContextAccessor();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession();
builder.Services.AddScoped<RestaurantManagement.Web.Security.ICurrentUser, RestaurantManagement.Web.Security.SessionCurrentUser>();

// Register SQL-backed store as the IQuanLyMonStore implementation
builder.Services.AddScoped<RestaurantManagement.Web.Services.IQuanLyMonStore, RestaurantManagement.Web.Services.SqlQuanLyMonStore>();

// In-memory store used by the ThucDon and GoiMon Razor Pages
builder.Services.AddSingleton<RestaurantManagement.Web.Services.InMemoryQuanLyMonStore>();

// Ảnh món tải lên (JPG/PNG): lưu tại wwwroot/uploads/mon-an, đường dẫn "/uploads/mon-an/..." ghi vào MenuItems.ImagePath.
var thuMucAnhMon = Path.Combine(builder.Environment.WebRootPath ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot"), "uploads", "mon-an");
builder.Services.AddSingleton<IKhoAnhMonAn>(_ => new KhoAnhMonAnTrenDia(thuMucAnhMon));

builder.Services.AddAuthentication("Cookies")
    .AddCookie("Cookies", options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.Cookie.Name = "RestaurantManagement.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = false;
        options.EventsType = typeof(IdleSessionEvents);
    });

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

// Phục vụ ảnh món tải lên lúc chạy (MapStaticAssets chỉ biết tệp có sẵn khi build). Đặt trước xác thực để khách xem được.
Directory.CreateDirectory(thuMucAnhMon);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(thuMucAnhMon),
    RequestPath = "/uploads/mon-an",
    OnPrepareResponse = context => context.Context.Response.Headers["X-Content-Type-Options"] = "nosniff"
});

app.UseRouting();
app.UseSession();

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<RequiredPasswordChangeMiddleware>();

app.MapStaticAssets().AllowAnonymous();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages();

app.Run();
