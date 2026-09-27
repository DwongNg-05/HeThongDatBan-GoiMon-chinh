using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using RestaurantManagement.Web.Models;
using RestaurantManagement.Web.Authentication;
using RestaurantManagement.Data;
using RestaurantManagement.Data.Data;
using RestaurantManagement.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews(options => options.Filters.Add<SessionActivityFilter>());
string ConnectionString() => Environment.GetEnvironmentVariable("RM_CONNECTION_STRING")
    ?? builder.Configuration.GetConnectionString("RestaurantManagement")
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Configure RM_CONNECTION_STRING or ConnectionStrings:RestaurantManagement.");
builder.Configuration["ConnectionStrings:DefaultConnection"] = ConnectionString();
builder.Services.AddRazorPages();
builder.Services.AddDbContext<RestaurantDbContext>(options => options.UseSqlServer(ConnectionString()));
builder.Services.AddSingleton<DemoTableCatalog>();
builder.Services.AddSingleton<TableMapEventBroker>();
builder.Services.AddScoped<TableDetailsService>();
builder.Services.AddHostedService<TableStatusOutboxWorker>();
builder.Services.AddSingleton<InMemoryQuanLyMonStore>();
builder.Services.AddScoped(_ => new ManagementStore(ConnectionString()));
builder.Services.AddScoped(_ => new LoginSessionStore(ConnectionString()));
builder.Services.AddScoped<IdleSessionEvents>();
builder.Services.AddScoped<IEmployeeAccountStore>(_ => new SqlEmployeeAccountStore(ConnectionString()));
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.Name = "RestaurantManagement.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    // Absolute cookie lifetime; server-side sessions enforce the 30-minute idle timeout.
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
    options.SlidingExpiration = false;
    options.EventsType = typeof(IdleSessionEvents);
    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }
        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
});
builder.Services.AddResponseCompression(options => options.EnableForHttps = true);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseResponseCompression();
app.UseAuthentication();
app.UseAuthorization();
app.MapStaticAssets();
app.MapControllers();
app.MapRazorPages();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Management}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
