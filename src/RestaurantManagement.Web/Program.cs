using Microsoft.AspNetCore.Authentication.Cookies;
using RestaurantManagement.Web.Models;
using RestaurantManagement.Web.Authentication;
using RestaurantManagement.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews(options => options.Filters.Add<SessionActivityFilter>());
string ConnectionString() => Environment.GetEnvironmentVariable("RM_CONNECTION_STRING")
    ?? builder.Configuration.GetConnectionString("RestaurantManagement")
    ?? throw new InvalidOperationException("Configure RM_CONNECTION_STRING or ConnectionStrings:RestaurantManagement.");
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
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Management}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
