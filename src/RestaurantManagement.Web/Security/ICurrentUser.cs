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

        private ISession? Session => _http.HttpContext?.Session;

        public string? UserName => Session?.GetString(KeyUserName);
        public string? Role => Session?.GetString(KeyRole);
        public bool IsAuthenticated => !string.IsNullOrEmpty(UserName);
    }
}