using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using RestaurantManagement.Web.Models;

namespace RestaurantManagement.Web.Authentication;

/// <summary>
/// S1-04 Task 1 (đã chốt với PO): API bị chặn trả dữ liệu lỗi thuần dạng JSON, chưa cần trang giao diện.
/// Ví dụ: {"status":403,"error":"forbidden","message":"…","role":"Waiter","path":"/Dishes"}.
/// </summary>
public sealed record ApiError(int Status, string Error, string Message, string? Role, string Path);

public sealed class IdleSessionEvents(LoginSessionStore sessions, ITempDataDictionaryFactory tempDataFactory) : CookieAuthenticationEvents
{
    public const string ExpiredItem = "LoginSessionExpired";

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        if (context.Principal is not null && await sessions.Check(context.Principal)) return;
        context.RejectPrincipal();
        context.HttpContext.Items[ExpiredItem] = true;
        // Preserve the notice if an asset or public page discovers expiration first.
        var notice = tempDataFactory.GetTempData(context.HttpContext);
        notice[ExpiredItem] = true;
        notice.Save();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// S1-04 Task 4: lời gọi API (/api/... hoặc fetch có X-Requested-With) không bị chuyển hướng sang trang HTML
    /// mà nhận mã lỗi rõ ràng: 401 khi chưa đăng nhập, 403 khi vai trò không có quyền.
    /// </summary>
    public static bool IsApiRequest(HttpRequest request) =>
        request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
        || string.Equals(request.Headers.XRequestedWith, "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions ErrorJson = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    public const string ForbiddenMessage = "Tài khoản của bạn không có quyền dùng chức năng này.";
    public const string UnauthorizedMessage = "Bạn cần đăng nhập để dùng chức năng này.";

    /// <summary>S1-04 Task 1: nội dung lỗi quyền (401 chưa đăng nhập, 403 sai vai trò) cho lời gọi API.</summary>
    public static ApiError ErrorFor(int status, ClaimsPrincipal user, string path) => status == StatusCodes.Status401Unauthorized
        ? new ApiError(status, "unauthorized", UnauthorizedMessage, null, path)
        : new ApiError(status, "forbidden", ForbiddenMessage, user.FindFirstValue(ClaimTypes.Role), path);

    public static string Serialize(ApiError error) => JsonSerializer.Serialize(error, ErrorJson);

    private static Task WriteApiError(HttpContext http, int status)
    {
        http.Response.StatusCode = status;
        http.Response.ContentType = "application/json; charset=utf-8";
        return http.Response.WriteAsync(Serialize(ErrorFor(status, http.User, http.Request.Path.Value ?? "/")));
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        if (IsApiRequest(context.Request))
            return WriteApiError(context.HttpContext, StatusCodes.Status403Forbidden);
        // S1-04 Task 3: màn hình giao diện → 403 ngay tại đường dẫn đã gõ, kèm trang báo không có quyền.
        if (AccessDeniedPage.TryShow(context.HttpContext))
            return Task.CompletedTask;
        return base.RedirectToAccessDenied(context);
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        if (IsApiRequest(context.Request))
            return WriteApiError(context.HttpContext, StatusCodes.Status401Unauthorized);
        var url = context.RedirectUri;
        if (context.HttpContext.Items.ContainsKey(ExpiredItem))
            url += (url.Contains('?') ? "&" : "?") + "sessionExpired=true";
        context.Response.Redirect(url);
        return Task.CompletedTask;
    }
}
