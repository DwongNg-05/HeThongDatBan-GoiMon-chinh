using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.DbTool;
using RestaurantManagement.Web.Controllers;
using RestaurantManagement.Web.Security;

internal static class PermissionMatrixTests
{
    internal static void Run(Action<bool, string> check)
    {
        var matrix = RolePermissionMatrix.Get();
        check(matrix.Roles.Select(role => role.Code).SequenceEqual(AppRoles.All)
            && matrix.Modules.Select(module => module.Key).SequenceEqual(RoleNavigation.Items.Select(item => item.Key))
            && matrix.Modules.All(module => module.Permissions.Keys.SequenceEqual(AppRoles.All)),
            "Permission matrix: API provides every role and navigation module in stable order");

        var price = ApiAccessMatrix.All.Single(endpoint => endpoint.Key == "MenuPrice.UpdatePrice PUT");
        var revenue = ApiAccessMatrix.All.Single(endpoint => endpoint.Key == "Cashier.RevenueReport GET");
        var page = ApiAccessMatrix.All.Single(endpoint => endpoint.Key == "PermissionMatrix.Index GET");
        var data = ApiAccessMatrix.All.Single(endpoint => endpoint.Key == "PermissionMatrix.Data GET");
        check(ApiAccessMatrix.IsAllowed(price, "Manager")
            && new[] { "Waiter", "Kitchen", "Cashier" }.All(role => !ApiAccessMatrix.IsAllowed(price, role))
            && ApiAccessMatrix.IsAllowed(revenue, "Manager") && ApiAccessMatrix.IsAllowed(revenue, "Cashier")
            && new[] { "Waiter", "Kitchen" }.All(role => !ApiAccessMatrix.IsAllowed(revenue, role)),
            "Sensitive APIs: only Manager changes menu prices; Manager and Cashier view revenue");
        check(ApiAccessMatrix.IsAllowed(page, "Manager") && ApiAccessMatrix.IsAllowed(data, "Manager")
            && AppRoles.All.Where(role => role != AppRoles.Manager).All(role => !ApiAccessMatrix.IsAllowed(page, role) && !ApiAccessMatrix.IsAllowed(data, role)),
            "Permission matrix: both the page and API are manager-only");

        var response = new PermissionMatrixController().Data() as OkObjectResult;
        check(response?.Value is PermissionMatrix payload
            && payload.Modules.Select(module => module.Key).SequenceEqual(RoleNavigation.Items.Select(item => item.Key)),
            "Permission matrix: authenticated endpoint returns the configured role-module matrix");

        var root = DatabaseTool.Root;
        var view = File.ReadAllText(Path.Combine(root, "src", "RestaurantManagement.Web", "Views", "PermissionMatrix", "Index.cshtml"));
        check(view.Contains("Ma trận phân quyền") && view.Contains("RolePermissionMatrix.Get()")
            && view.Contains("Được phép") && view.Contains("Không được phép") && view.Contains("/api/permissions/matrix"),
            "Permission matrix: manager screen renders each role/module permission and links to its JSON API");
    }
}
