using RestaurantManagement.Web.Models;
using RestaurantManagement.Web.Authentication;
using Microsoft.EntityFrameworkCore;
using RestaurantManagement.Data.Data;
using RestaurantManagement.Data.Models;
using RestaurantManagement.Data;
using RestaurantManagement.Web.Services;
using RestaurantManagement.Web.Services.EmailVerification;

var builder = WebApplication.CreateBuilder(args);
var sqlConnectionString = Environment.GetEnvironmentVariable("RM_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(sqlConnectionString))
    sqlConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(sqlConnectionString))
    throw new InvalidOperationException("Set RM_CONNECTION_STRING or ConnectionStrings:DefaultConnection to a SQL Server connection string before starting the web app.");
builder.Configuration["ConnectionStrings:DefaultConnection"] = sqlConnectionString;

builder.Services.AddScoped<AuditLogService>();
builder.Services.AddScoped<RestaurantManagement.Web.Services.Reservations.ReservationSlotService>();
builder.Services.AddScoped<ReservationStore>();
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));
builder.Services.AddSingleton<RestaurantManagement.Web.Services.CancellationSmtpEmailSender>();
if (!string.IsNullOrWhiteSpace(builder.Configuration["Smtp:Username"] ) &&
    !string.IsNullOrWhiteSpace(builder.Configuration["Smtp:Password"] ) &&
    !string.IsNullOrWhiteSpace(builder.Configuration["Smtp:FromEmail"] ))
    builder.Services.AddHostedService<EmailWorker>();

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
// Xác minh đăng nhập bằng mã gửi qua email (mọi vai trò trừ Quản lý). Cấu hình: mục "EmailVerification" và "Email".
builder.Services.Configure<EmailVerificationOptions>(builder.Configuration.GetSection(EmailVerificationOptions.Section));
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.Section));
builder.Services.AddScoped(_ => new EmailVerificationStore(sqlConnectionString));
builder.Services.AddSingleton<IEmailSender, RestaurantManagement.Web.Services.EmailVerification.SmtpEmailSender>();
builder.Services.AddScoped<EmailVerificationService>();
builder.Services.AddScoped<IEmployeeAccountStore>(_ => new SqlEmployeeAccountStore(sqlConnectionString));
// S2-09 Task 3: trạng thái email xác nhận của từng lượt đặt bàn (dbo.EmailOutbox).
builder.Services.AddScoped<IReservationEmailStatusStore>(_ => new SqlReservationEmailStatusStore(sqlConnectionString));
// S2-09 Task 1: gửi email xác nhận ngay sau khi đặt bàn (dbo.EmailOutbox + IEmailSender).
builder.Services.AddScoped<IBookingEmailOutbox>(_ => new SqlBookingEmailOutbox(sqlConnectionString));
builder.Services.AddScoped<BookingEmailDispatcher>();
// S2-09 Task 2: tự động gửi lại email đặt bàn thất bại (tối đa 3 lần, cách nhau 5 phút). Tắt bằng Email:RetryPollSeconds = 0.
builder.Services.AddHostedService<BookingEmailRetryWorker>();
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

// Register SQL-backed store as the IMenuStore implementation
builder.Services.AddScoped<RestaurantManagement.Web.Services.IMenuStore, RestaurantManagement.Web.Services.SqlMenuStore>();

// In-memory store used by the Menu and Ordering Razor Pages
builder.Services.AddSingleton<RestaurantManagement.Web.Services.InMemoryMenuStore>();

// Ảnh món tải lên (JPG/PNG): lưu tại wwwroot/uploads/mon-an, đường dẫn "/uploads/mon-an/..." ghi vào MenuItems.ImagePath.
var dishImageFolder = Path.Combine(builder.Environment.WebRootPath ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot"), "uploads", "mon-an");
builder.Services.AddSingleton<IDishImageStorage>(_ => new DiskDishImageStorage(dishImageFolder));

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

// S2-01 Task 4: nén HTML/JSON của thực đơn công khai (200 món ~ vài trăm KB còn vài chục KB).
// Chỉ áp dụng cho /Menu và /api/menu khi khách chưa đăng nhập (xem UseWhen bên dưới):
// trang có ô tìm kiếm phản hồi lại từ khoá, nên không nén khi trang có thể chứa token của nhân viên (tránh BREACH).
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
});
builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProviderOptions>(o => o.Level = System.IO.Compression.CompressionLevel.Fastest);
builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProviderOptions>(o => o.Level = System.IO.Compression.CompressionLevel.Fastest);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

// Phục vụ ảnh món tải lên lúc chạy (MapStaticAssets chỉ biết tệp có sẵn khi build). Đặt trước xác thực để khách xem được.
Directory.CreateDirectory(dishImageFolder);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(dishImageFolder),
    RequestPath = "/uploads/mon-an",
    OnPrepareResponse = context => context.Context.Response.Headers["X-Content-Type-Options"] = "nosniff"
});

app.UseRouting();
app.UseSession();

app.UseAuthentication();
// S2-01 Task 1: khách chưa đăng nhập mở "/" được đưa thẳng tới thực đơn công khai (/Menu), không cần đăng nhập.
app.Use((context, next) => RestaurantManagement.Web.Services.GuestMenuEntry.Invoke(context, next));
app.UseAuthorization();
app.UseMiddleware<EmailVerificationMiddleware>();
app.UseMiddleware<RequiredPasswordChangeMiddleware>();

app.UseWhen(
    context => context.User.Identity?.IsAuthenticated != true
        && (context.Request.Path.StartsWithSegments("/Menu") || context.Request.Path.StartsWithSegments("/api/menu")),
    branch => branch.UseResponseCompression());

app.MapStaticAssets().AllowAnonymous();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapControllers();

app.MapRazorPages();

// Old Vietnamese URLs (bookmarks, printed links) redirect to the English routes.
foreach (var (oldPath, newPath) in new[]
{
    ("/ThucDon", "/Menu"), ("/api/thuc-don", "/api/menu"), ("/GoiMon", "/Ordering"),
    ("/QuanLyMon", "/Dishes"), ("/QuanLyNhomMon", "/DishCategories")
})
{
    app.MapGet(oldPath, () => Results.Redirect(newPath, permanent: true)).AllowAnonymous();
}

app.Run();
