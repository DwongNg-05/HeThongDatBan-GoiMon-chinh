using RestaurantManagement.Web.Models;
using RestaurantManagement.Web.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using RestaurantManagement.Data.Data;
using RestaurantManagement.Data.Models;
using RestaurantManagement.Data;
using RestaurantManagement.Web.Services;
using RestaurantManagement.Web.Services.EmailVerification;

var builder = WebApplication.CreateBuilder(args);
if (builder.Environment.IsDevelopment())
{
    // Machine-specific settings stay outside Git; explicit environment/CLI values still win.
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
        .AddEnvironmentVariables()
        .AddCommandLine(args);
}
var sqlConnectionString = Environment.GetEnvironmentVariable("RM_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(sqlConnectionString))
{
    sqlConnectionString =
        builder.Configuration.GetConnectionString("DefaultConnection");
}

if (string.IsNullOrWhiteSpace(sqlConnectionString))
    throw new InvalidOperationException("Set RM_CONNECTION_STRING or ConnectionStrings:DefaultConnection to a SQL Server connection string before starting the web app.");
if (builder.Environment.IsDevelopment() && OperatingSystem.IsWindows())
{
    sqlConnectionString = DevelopmentLocalDb.Resolve(sqlConnectionString);
    Environment.SetEnvironmentVariable("RM_CONNECTION_STRING", sqlConnectionString);
}
builder.Configuration["ConnectionStrings:DefaultConnection"] = sqlConnectionString;

builder.Services.AddScoped<AuditLogService>();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddScoped<DailyOverviewStore>();
builder.Services.AddScoped<RestaurantManagement.Web.Services.Reservations.ReservationSlotService>();
builder.Services.AddScoped<ReservationStore>();
builder.Services.AddScoped<ReservationConfirmationService>();

builder.Services.AddScoped<ITableMapReader, SqlTableMapReader>();
// S4-01 Task 1: dữ liệu tạm tính của bàn đang phục vụ (chỉ đọc).
builder.Services.AddScoped<TableBillPreviewStore>();
// S4-01 Task 4: xuất PDF hoá đơn tạm tính khổ 80mm (font trong thư mục Fonts, nạp khi cần lần đầu).
builder.Services.AddSingleton(new RestaurantManagement.Web.Services.Pdf.BillPdfService(
    RestaurantManagement.Web.Services.Pdf.BillPdfService.FindFontDirectory(builder.Environment.ContentRootPath)));
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));
builder.Services.AddSingleton<RestaurantManagement.Web.Services.CancellationSmtpEmailSender>();
if (!string.IsNullOrWhiteSpace(builder.Configuration["Smtp:Username"] ) &&
    !string.IsNullOrWhiteSpace(builder.Configuration["Smtp:Password"] ) &&
    !string.IsNullOrWhiteSpace(builder.Configuration["Smtp:FromEmail"] ))
    builder.Services.AddHostedService<EmailWorker>();

builder.Services.AddScoped<
    RestaurantManagement.Web.Services.Ordering.GuestOrderService>();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

// Kiểm tra quyền ở máy chủ.
builder.Services.AddAuthorization(
    RestaurantManagement.Web.Security.AppRoles.Configure);

builder.Services.AddScoped(_ =>
    new ManagementStore(sqlConnectionString));

// Danh sách món trong ngày và trạng thái tạm hết.
builder.Services.AddScoped(_ =>
    new DailyDishStore(sqlConnectionString));

builder.Services.AddScoped(_ =>
    new LoginSessionStore(sqlConnectionString));

// Nhật ký bảo mật.
builder.Services.AddScoped(_ =>
    new SecurityAuditStore(sqlConnectionString));

builder.Services.AddScoped<IdleSessionEvents>();

builder.Services.AddScoped(_ =>
    new PasswordChangeStore(sqlConnectionString));

// Xác minh đăng nhập qua email.
builder.Services.Configure<EmailVerificationOptions>(
    builder.Configuration.GetSection(
        EmailVerificationOptions.Section));

builder.Services.Configure<EmailOptions>(
    builder.Configuration.GetSection(EmailOptions.Section));

builder.Services.AddScoped(_ =>
    new EmailVerificationStore(sqlConnectionString));

builder.Services.AddSingleton<
    IEmailSender,
    RestaurantManagement.Web.Services.EmailVerification.SmtpEmailSender>();

builder.Services.AddScoped<EmailVerificationService>();

builder.Services.AddScoped<IEmployeeAccountStore>(_ =>
    new SqlEmployeeAccountStore(sqlConnectionString));

// Trạng thái email xác nhận đặt bàn.
builder.Services.AddScoped<IReservationEmailStatusStore>(_ =>
    new SqlReservationEmailStatusStore(sqlConnectionString));

// Hàng đợi gửi email đặt bàn.
builder.Services.AddScoped<IBookingEmailOutbox>(_ =>
    new SqlBookingEmailOutbox(sqlConnectionString));

builder.Services.AddScoped<BookingEmailDispatcher>();

// Liên kết xác nhận đặt bàn trong email.
builder.Services.AddSingleton<BookingConfirmationLinks>();

// Tự động gửi lại email đặt bàn thất bại.
builder.Services.AddHostedService<BookingEmailRetryWorker>();

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<SessionActivityFilter>();
    options.Filters.Add<SessionActivityPageFilter>();
    options.Filters.Add<BookingAntiforgeryRecoveryFilter>();
});

builder.Services.AddRazorPages();

// Dịch vụ sơ đồ bàn.
builder.Services.AddSingleton<DemoTableCatalog>();
builder.Services.AddScoped<TableMapStore>();
builder.Services.AddSingleton<TableMapEventBroker>();
builder.Services.AddScoped<TableDetailsService>();
builder.Services.AddScoped<TableQrService>();

// Khách quét QR bàn trống để mở phiên gọi món.
builder.Services.AddScoped<GuestTableSessionService>();

// Xuất mã QR bàn thành PDF.
builder.Services.AddSingleton<TableQrPdfBuilder>();

builder.Services.AddHostedService<TableStatusOutboxWorker>();

// Dùng cùng SQL Server với các controller và dịch vụ nền.
builder.Services.AddDbContext<
    RestaurantManagement.Data.Data.RestaurantDbContext>(options =>
        options.UseSqlServer(sqlConnectionString));

builder.Services.AddHttpContextAccessor();
builder.Services.AddDistributedMemoryCache();

var dataProtectionKeyFolder = Path.Combine(
    builder.Environment.ContentRootPath,
    "App_Data",
    "data-protection-keys");

Directory.CreateDirectory(dataProtectionKeyFolder);

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(
        new DirectoryInfo(dataProtectionKeyFolder))
    .SetApplicationName("RestaurantManagement.Web");

builder.Services.AddSession();

builder.Services.AddScoped<
    RestaurantManagement.Web.Security.ICurrentUser,
    RestaurantManagement.Web.Security.SessionCurrentUser>();

// Kho thực đơn dùng SQL Server.
builder.Services.AddScoped<IMenuStore, SqlMenuStore>();

builder.Services.AddSingleton<InMemoryMenuStore>();

// Lưu ảnh món tải lên.
var dishImageFolder = Path.Combine(
    builder.Environment.WebRootPath
        ?? Path.Combine(
            builder.Environment.ContentRootPath,
            "wwwroot"),
    "uploads",
    "mon-an");

builder.Services.AddSingleton<IDishImageStorage>(_ =>
    new DiskDishImageStorage(dishImageFolder));

builder.Services.AddAuthentication("Cookies")
    .AddCookie("Cookies", options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";

        options.Cookie.Name = "RestaurantManagement.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;

        options.Cookie.SecurePolicy =
            builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;

        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = false;
        options.EventsType = typeof(IdleSessionEvents);
    });

// Nén nội dung thực đơn công khai cho khách chưa đăng nhập.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;

    options.Providers.Add<
        Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();

    options.Providers.Add<
        Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
});

builder.Services.Configure<
    Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProviderOptions>(
    options =>
    {
        options.Level =
            System.IO.Compression.CompressionLevel.Fastest;
    });

builder.Services.Configure<
    Microsoft.AspNetCore.ResponseCompression.GzipCompressionProviderOptions>(
    options =>
    {
        options.Level =
            System.IO.Compression.CompressionLevel.Fastest;
    });

builder.Services.AddHostedService<TemporaryOutResetWorker>();
// Gọi thêm món vào phiên đang mở.
builder.Services.AddScoped<SessionOrderingService>();
// S4-04 Task 3: đọc bản chốt đã lưu và tạo PDF.
builder.Services.AddScoped<ShiftClosingStore>();
builder.Services.AddSingleton<ShiftClosingPdfBuilder>();
builder.Services.AddScoped<RestaurantManagement.Web.Services.TableMergeService>();
builder.Services.AddSingleton<DatabaseSchemaReadiness>();
var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

// Phục vụ ảnh món được tải lên trong lúc ứng dụng chạy.
Directory.CreateDirectory(dishImageFolder);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider =
        new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
            dishImageFolder),

    RequestPath = "/uploads/mon-an",

    OnPrepareResponse = context =>
    {
        context.Context.Response.Headers[
            "X-Content-Type-Options"] = "nosniff";
    }
});

// Hiển thị trang từ chối quyền tại đường dẫn được yêu cầu.
app.UseStatusCodePagesWithReExecute(AccessDeniedPage.Path);

app.Use((context, next) =>
    AccessDeniedPage.DisableByDefault(context, next));

// Ghi nhớ địa chỉ web để tạo liên kết xác nhận đặt bàn.
var bookingConfirmationLinks =
    app.Services.GetRequiredService<BookingConfirmationLinks>();

app.Use((context, next) =>
{
    bookingConfirmationLinks.Observe(context.Request);
    return next(context);
});

app.UseRouting();
app.UseSession();

app.UseAuthentication();

// Khách chưa đăng nhập mở "/" được đưa đến thực đơn.
app.Use((context, next) =>
    GuestMenuEntry.Invoke(context, next));

// Xác minh email và yêu cầu đổi mật khẩu trước khi phân quyền.
app.UseMiddleware<EmailVerificationMiddleware>();
app.UseMiddleware<RequiredPasswordChangeMiddleware>();

app.UseAuthorization();
app.UseMiddleware<DatabaseSchemaMiddleware>();

app.UseWhen(
    context =>
        context.User.Identity?.IsAuthenticated != true
        && (
            context.Request.Path.StartsWithSegments("/Menu")
            || context.Request.Path.StartsWithSegments("/api/menu")
        ),
    branch => branch.UseResponseCompression());

app.MapStaticAssets().AllowAnonymous();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapControllers();
app.MapRazorPages();

// Chuyển các đường dẫn tiếng Việt cũ sang đường dẫn hiện tại.
foreach (var (oldPath, newPath) in new[]
{
    ("/ThucDon", "/Menu"),
    ("/api/thuc-don", "/api/menu"),
    ("/GoiMon", "/Ordering"),
    ("/QuanLyMon", "/Dishes"),
    ("/QuanLyNhomMon", "/DishCategories")
})
{
    app.MapGet(
        oldPath,
        () => Results.Redirect(newPath, permanent: true))
        .AllowAnonymous();
}

app.Run();
