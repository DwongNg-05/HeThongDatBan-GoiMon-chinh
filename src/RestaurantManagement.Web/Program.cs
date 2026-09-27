using Microsoft.EntityFrameworkCore;
using RestaurantManagement.Data.Data;

var builder = WebApplication.CreateBuilder(args);
var sqlConnectionString = Environment.GetEnvironmentVariable("RM_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(sqlConnectionString))
    sqlConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(sqlConnectionString))
    throw new InvalidOperationException("Set RM_CONNECTION_STRING or ConnectionStrings:DefaultConnection before starting the web app.");
builder.Configuration["ConnectionStrings:DefaultConnection"] = sqlConnectionString;

// Add services to the container.
builder.Services.AddControllersWithViews();
<<<<<<< HEAD
// Support Razor Pages
builder.Services.AddRazorPages();

// Register the in-memory store as the application's store implementation
builder.Services.AddSingleton<RestaurantManagement.Web.Services.InMemoryQuanLyMonStore>();
=======
builder.Services.AddSingleton<RestaurantManagement.Web.Services.DemoTableCatalog>();
builder.Services.AddSingleton<RestaurantManagement.Web.Services.TableMapEventBroker>();
builder.Services.AddScoped<RestaurantManagement.Web.Services.TableDetailsService>();
builder.Services.AddHostedService<RestaurantManagement.Web.Services.TableStatusOutboxWorker>();

builder.Services.AddDbContext<RestaurantDbContext>(options =>
    options.UseSqlServer(sqlConnectionString));
>>>>>>> 0556fdc36df9dca8eb79e22b99ae4ad50f24208c

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

<<<<<<< HEAD
// Map Razor Pages endpoints
app.MapRazorPages();
=======
app.MapControllers();
>>>>>>> 0556fdc36df9dca8eb79e22b99ae4ad50f24208c

app.Run();
