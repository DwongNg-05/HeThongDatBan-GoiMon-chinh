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
        private readonly string? _developmentUser;
        public SessionCurrentUser(IHttpContextAccessor http, IWebHostEnvironment environment, IConfiguration configuration)
        {
            _http = http;
            if (environment.IsDevelopment() && configuration.GetValue<bool>("DevelopmentUser:Enabled"))
                _developmentUser = configuration["DevelopmentUser:UserName"];
        }

        private ISession? Session => _http.HttpContext?.Session;

        public string? UserName => Session?.GetString(KeyUserName) ?? _developmentUser;
        public string? Role => Session?.GetString(KeyRole);
        public bool IsAuthenticated => !string.IsNullOrEmpty(UserName);
    }
}
