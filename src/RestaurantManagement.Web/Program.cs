using Microsoft.EntityFrameworkCore;
using RestaurantManagement.Data;
using RestaurantManagement.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<AuditLogService>();
builder.Services.AddScoped<ReservationStore>();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.Configure<SmtpOptions>(
    builder.Configuration.GetSection("Smtp"));

builder.Services.AddSingleton<SmtpEmailSender>();

// Chỉ bật EmailWorker khi SMTP đã được cấu hình đầy đủ.
var smtpUsername = builder.Configuration["Smtp:Username"];
var smtpPassword = builder.Configuration["Smtp:Password"];
var smtpFromEmail = builder.Configuration["Smtp:FromEmail"];

if (!string.IsNullOrWhiteSpace(smtpUsername) &&
    !string.IsNullOrWhiteSpace(smtpPassword) &&
    !string.IsNullOrWhiteSpace(smtpFromEmail))
{
    builder.Services.AddHostedService<EmailWorker>();
}

builder.Services.AddControllersWithViews();

builder.Services.AddAuthentication("Cookies")
    .AddCookie("Cookies", options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
    });

var app = builder.Build();

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

app.Run();