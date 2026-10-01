using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantManagement.Web.Controllers;
using RestaurantManagement.Web.Services;

internal static class TableMapTests
{
    public static async Task Run(Action<bool, string> check)
    {
        var reader = new DemoTableMapReader(new DemoTableCatalog());
        var snapshot = reader.Read();
        check(snapshot.Areas.Select(area => area.Name).SequenceEqual(new[] { "Tầng một", "Tầng hai", "Sân vườn" }), "Map preserves configured area order");
        check(snapshot.TotalCount == 60 && snapshot.Areas.All(area => area.Tables.Count == 20 && area.Tables.All(table => table.Area == area.Name)), "60 tables grouped into the correct areas");
        check(snapshot.Areas.All(area => area.AvailableCount > 0), "Each area reports available tables");
        var empty = DemoTableMapReader.Group(new[] { "Khu vực rỗng" }, []);
        check(empty.Areas.Count == 1 && empty.Areas[0].Tables.Count == 0, "Empty area is retained");
        check(DemoTableMapReader.Group([], []).Areas.Count == 0, "No areas returns an empty snapshot");
        var controller = new TableMapController(new FailingReader(), NullLogger<TableMapController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        check(controller.Index() is ViewResult { Model: TableMapSnapshot { LoadFailed: true } } && controller.Response.StatusCode == 503, "Map load failure renders retry state with 503");
        check(controller.Snapshot() is ObjectResult { StatusCode: 503 }, "Grouped API returns safe load failure");
        check(await controller.Changes("-1", CancellationToken.None) is BadRequestObjectResult, "Changes rejects negative cursor");
        check(await controller.Changes("not-a-number", CancellationToken.None) is BadRequestObjectResult, "Changes rejects malformed cursor");
        check(await controller.Changes("0", CancellationToken.None) is ObjectResult { StatusCode: 503 }, "Changes fails safely when tracking is unavailable");
        var failingDetails = new TableDetailsService(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), new DemoTableCatalog(), new TestHostEnvironment(), NullLogger<TableDetailsService>.Instance);
        var detailsController = new TableDetailsController(failingDetails, NullLogger<TableDetailsController>.Instance);
        detailsController.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier,"1"), new Claim(ClaimTypes.Role,"Waiter") },"test")) } };
        check(await detailsController.Get("A01", default) is ObjectResult { StatusCode: 503 }, "SQL detail failure returns 503 instead of invented sample guest or money");
        check(await detailsController.Get(new string('A',21), default) is BadRequestObjectResult, "Detail API rejects oversized table code");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        using var provider = services.BuildServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        foreach (var type in new[] { typeof(TableMapController), typeof(TableStatusController), typeof(TableDetailsController) })
        {
            var attribute = type.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
            var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireRole(attribute.Roles!.Split(',')).Build();
            foreach (var role in new[] { "Waiter", "Manager", "Kitchen", "Cashier" })
            {
                var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role) }, "test"));
                check((await authorization.AuthorizeAsync(principal, null, policy)).Succeeded == (role == "Waiter"), $"{type.Name} restricts {role} correctly");
            }
            check(!(await authorization.AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity()), null, policy)).Succeeded, $"{type.Name} rejects anonymous access");
        }
    }

    private sealed class FailingReader : ITableMapReader
    {
        public TableMapSnapshot Read() => throw new InvalidOperationException("Simulated load failure");
    }
}
