using Microsoft.AspNetCore.Http;

namespace RestaurantManagement.Web.Security
{
    public interface ICurrentUser
    {
        bool IsAuthenticated { get; }
        string? UserName { get; }
        string? Role { get; }
    }

    public class SessionCurrentUser : ICurrentUser
    {
        public const string KeyUserName = "UserName";
        public const string KeyRole = "Role";

        private readonly IHttpContextAccessor _http;
        public SessionCurrentUser(IHttpContextAccessor http) => _http = http;

        private System.Security.Claims.ClaimsPrincipal? User => _http.HttpContext?.User;
        public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;
        public string? UserName => IsAuthenticated ? User?.Identity?.Name : null;
        public string? Role => IsAuthenticated ? User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value : null;
    }
}
