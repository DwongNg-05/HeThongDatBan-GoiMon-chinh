using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Razor.Hosting;
using Microsoft.Extensions.DependencyInjection;
using RestaurantManagement.DbTool;
using RestaurantManagement.Web.Security;

/// <summary>
/// S1-04 Task 4 (không cần database): rà soát bằng phản chiếu TOÀN BỘ action MVC và Razor Page trong web.
/// - Mọi API phải có mặt trong bảng phân quyền ApiAccessMatrix và ngược lại (không API nào bị bỏ sót / không mục thừa).
/// - Mọi API không công khai phải gắn kiểm tra quyền rõ ràng ([Authorize]), không dựa vào cấu hình mặc định.
/// - Chạy đúng các chính sách phân quyền của web (AppRoles.Configure) cho: chưa đăng nhập, 4 vai trò, vai trò không xác định,
///   và đăng nhập nhưng không có vai trò — kết quả phải khớp bảng phân quyền.
/// </summary>
internal static class ApiAuthorizationCoverageTests
{
    private sealed record Discovered(string Key, IReadOnlyList<IAuthorizeData> Authorize, bool AllowAnonymous);

    internal static async Task Run(Action<bool, string> check)
    {
        var assembly = typeof(AppRoles).Assembly;
        var discovered = Controllers(assembly).Concat(Pages(assembly)).ToList();
        var matrix = ApiAccessMatrix.All.Where(e => !e.Key.StartsWith("Minimal ", StringComparison.Ordinal)).ToDictionary(e => e.Key);

        check(discovered.Count >= 80, $"API coverage: found {discovered.Count} MVC actions and Razor Pages by reflection");
        var duplicate = discovered.GroupBy(d => d.Key).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        check(duplicate.Length == 0, "API coverage: every API has a unique key" + Show(duplicate));
        var missing = discovered.Select(d => d.Key).Where(k => !matrix.ContainsKey(k)).ToArray();
        check(missing.Length == 0, "API coverage: every API in the code is listed in the permission matrix" + Show(missing));
        var stale = matrix.Keys.Where(k => discovered.All(d => d.Key != k)).ToArray();
        check(stale.Length == 0, "API coverage: the permission matrix lists no API that does not exist" + Show(stale));

        var unguarded = discovered.Where(d => !d.AllowAnonymous && d.Authorize.Count == 0).Select(d => d.Key).ToArray();
        check(unguarded.Length == 0, "API coverage: every non-public API declares its own authorization check" + Show(unguarded));
        var unexpectedAnonymous = discovered.Where(d => d.AllowAnonymous && matrix.TryGetValue(d.Key, out var e) && e.Allowed != ApiAccessMatrix.Anonymous)
            .Select(d => d.Key).ToArray();
        check(unexpectedAnonymous.Length == 0, "API coverage: [AllowAnonymous] only on public APIs" + Show(unexpectedAnonymous));

        // Đánh giá chính sách thật của web cho từng loại người gọi.
        await using var services = new ServiceCollection().AddLogging().AddAuthorizationCore(AppRoles.Configure).BuildServiceProvider();
        var policies = services.GetRequiredService<IAuthorizationPolicyProvider>();
        var authorization = services.GetRequiredService<IAuthorizationService>();
        var callers = new (string Label, ClaimsPrincipal Principal, string? Role)[]
        {
            ("anonymous", new ClaimsPrincipal(new ClaimsIdentity()), null),
            ("Manager", Staff("Manager"), "Manager"), ("Waiter", Staff("Waiter"), "Waiter"),
            ("Kitchen", Staff("Kitchen"), "Kitchen"), ("Cashier", Staff("Cashier"), "Cashier"),
            ("unknown role", Staff("Guest"), "Guest"), ("no role", Staff(null), "")
        };
        var wrong = new List<string>();
        foreach (var api in discovered.Where(d => matrix.ContainsKey(d.Key)))
        {
            var expected = matrix[api.Key];
            var policy = api.AllowAnonymous ? null
                : await AuthorizationPolicy.CombineAsync(policies, api.Authorize) ?? await policies.GetFallbackPolicyAsync();
            foreach (var (label, principal, role) in callers)
            {
                var allowed = policy is null || (await authorization.AuthorizeAsync(principal, null, policy)).Succeeded;
                if (allowed != ApiAccessMatrix.IsAllowed(expected, role))
                    wrong.Add($"{api.Key} / {label}: {(allowed ? "allowed" : "blocked")}");
            }
        }
        check(wrong.Count == 0, "API coverage: anonymous, each role, unknown role and no role get exactly the access in the matrix" + Show(wrong));

        // Chặn hoàn toàn vai trò không xác định / không có vai trò ở mọi API cần đăng nhập (trừ đăng xuất).
        check(discovered.Count(d => matrix.TryGetValue(d.Key, out var e) && e.Allowed is not (ApiAccessMatrix.Anonymous or ApiAccessMatrix.SignedIn)) >= 70
            && matrix.Values.Count(e => e.Allowed == ApiAccessMatrix.SignedIn) == 1,
            "API coverage: unknown roles can only sign out; every other signed-in API needs a known role");
        check(AppRoles.All.SequenceEqual(ApiAccessMatrix.Roles) && !AppRoles.IsKnown("Guest") && !AppRoles.IsKnown(null) && AppRoles.IsKnown("Cashier"),
            "API coverage: the role list matches dbo.Roles");
    }

    private static ClaimsPrincipal Staff(string? role)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "7"), new(ClaimTypes.Name, "tester") };
        if (role is not null) claims.Add(new Claim(ClaimTypes.Role, role));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookies"));
    }

    private static string Show(IReadOnlyCollection<string> items) => items.Count == 0 ? "" : ": " + string.Join("; ", items.Take(15));

    /// <summary>Action MVC: phương thức public của controller, khoá "{Controller}.{Action} {VERB}" (không ghi verb = ANY).</summary>
    private static IEnumerable<Discovered> Controllers(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t)))
        {
            var controller = type.Name.EndsWith("Controller", StringComparison.Ordinal) ? type.Name[..^"Controller".Length] : type.Name;
            var classAuthorize = type.GetCustomAttributes(true).OfType<IAuthorizeData>().ToArray();
            var classAnonymous = type.GetCustomAttributes(true).OfType<IAllowAnonymous>().Any();
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                         .Where(m => !m.IsSpecialName && !m.IsDefined(typeof(NonActionAttribute), true)))
            {
                var name = method.GetCustomAttribute<ActionNameAttribute>()?.Name ?? method.Name;
                if (name.EndsWith("Async", StringComparison.Ordinal) && method.GetCustomAttribute<ActionNameAttribute>() is null) name = name[..^5];
                var verbs = method.GetCustomAttributes(true).OfType<IActionHttpMethodProvider>().SelectMany(a => a.HttpMethods).Distinct().ToArray();
                var authorize = classAuthorize.Concat(method.GetCustomAttributes(true).OfType<IAuthorizeData>()).ToArray();
                var anonymous = classAnonymous || method.GetCustomAttributes(true).OfType<IAllowAnonymous>().Any();
                foreach (var verb in verbs.Length == 0 ? new[] { "ANY" } : verbs)
                    yield return new Discovered($"{controller}.{name} {verb}", authorize, anonymous);
            }
        }
    }

    /// <summary>Razor Page đã biên dịch (có tệp .cshtml), khoá "Page {đường dẫn}"; quyền lấy từ trang và PageModel.</summary>
    private static IEnumerable<Discovered> Pages(Assembly assembly)
    {
        foreach (var item in assembly.GetCustomAttributes<RazorCompiledItemAttribute>().Where(i => i.Kind == "mvc.1.0.razor-page"))
        {
            var route = item.Identifier.Replace("/Pages", "", StringComparison.Ordinal).Replace(".cshtml", "", StringComparison.Ordinal);
            var model = item.Type.GetProperty("Model", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)?.PropertyType;
            var attributes = item.Type.GetCustomAttributes(true).Concat(model?.GetCustomAttributes(true) ?? Array.Empty<object>()).ToArray();
            yield return new Discovered($"Page {route}", attributes.OfType<IAuthorizeData>().ToArray(), attributes.OfType<IAllowAnonymous>().Any());
        }
    }
}
