namespace RestaurantManagement.Web.Security;

public sealed record PermissionMatrixRole(string Code, string Name);

public sealed record PermissionMatrixModule(string Key, string Name, string Path, IReadOnlyDictionary<string, bool> Permissions);

public sealed record PermissionMatrix(IReadOnlyList<PermissionMatrixRole> Roles, IReadOnlyList<PermissionMatrixModule> Modules);

public static class RolePermissionMatrix
{
    public static PermissionMatrix Get() => new(
        AppRoles.All.Select(role => new PermissionMatrixRole(role, AppRoles.DisplayName(role))).ToArray(),
        RoleNavigation.Items.Select(item => new PermissionMatrixModule(
            item.Key,
            item.Title,
            item.Path,
            AppRoles.All.ToDictionary(role => role, role => item.Roles.Contains(role, StringComparer.Ordinal)))).ToArray());
}
