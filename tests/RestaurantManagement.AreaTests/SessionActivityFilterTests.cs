using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using RestaurantManagement.Web.Authentication;
using RestaurantManagement.Web.Models;

/// <summary>
/// Lỗi đăng xuất: luồng trực tiếp của sơ đồ bàn (api/table-status/stream) đã gửi dữ liệu cho trình duyệt; khi người dùng đăng xuất,
/// luồng kết thúc và SessionActivityFilter từng cố đăng xuất + chuyển hướng trên phản hồi đã bắt đầu
/// → "Headers are read-only, response has already started". Bộ lọc phải bỏ qua phản hồi đã bắt đầu hoặc yêu cầu đã huỷ.
/// </summary>
internal static class SessionActivityFilterTests
{
    private sealed class StartedResponse : HttpResponseFeature
    {
        public override bool HasStarted => true;
    }

    internal static async Task Run(Action<bool, string> check)
    {
        // Chuỗi kết nối sai: nếu bộ lọc còn kiểm tra phiên (gọi database) thì ném lỗi ngay, đúng như tình huống lỗi cũ.
        var filter = new SessionActivityFilter(new LoginSessionStore("đây không phải chuỗi kết nối"));

        foreach (var (label, started, aborted) in new[]
        {
            ("the live table-map stream has already sent data", true, false),
            ("the browser already closed the request", false, true)
        })
        {
            var features = new FeatureCollection();
            features.Set<IHttpRequestFeature>(new HttpRequestFeature { Method = "GET", Path = "/api/table-status/stream" });
            features.Set<IHttpResponseFeature>(started ? new StartedResponse() : new HttpResponseFeature());
            features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(Stream.Null));
            var http = new DefaultHttpContext(features)
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, "1"),
                    new Claim(LoginSessionStore.SessionClaim, Guid.NewGuid().ToString())
                }, "Cookies"))
            };
            using var cancelled = new CancellationTokenSource();
            if (aborted) cancelled.Cancel();
            http.RequestAborted = cancelled.Token;
            http.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new AuthorizeAttribute()), "table-status stream"));
            var action = new ActionContext(http, new RouteData(), new ActionDescriptor(), new ModelStateDictionary());
            var executing = new ActionExecutingContext(action, new List<IFilterMetadata>(), new Dictionary<string, object?>(), new object());
            var executed = new ActionExecutedContext(action, new List<IFilterMetadata>(), new object()) { Result = new EmptyResult() };

            Exception? error = null;
            try { await filter.OnActionExecutionAsync(executing, () => Task.FromResult(executed)); }
            catch (Exception ex) { error = ex; }
            check(error is null && executed.Result is EmptyResult && !http.Response.Headers.ContainsKey("Set-Cookie"),
                $"Logout: when {label}, the session filter leaves the response alone instead of throwing 'Headers are read-only'");
        }
    }
}
