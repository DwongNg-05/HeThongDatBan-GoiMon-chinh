using RestaurantManagement.Data;
using RestaurantManagement.Data.Models;

namespace RestaurantManagement.Web.Services
{
    public class AuditLogService
    {
        private readonly ApplicationDbContext _context;

        public AuditLogService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task LogAsync(
            string username,
            string role,
            string action,
            string ipAddress)
        {
            // Cắt theo độ dài cột để tên đăng nhập quá dài không làm lỗi lưu (HTTP 500)
            var log = new AuditLog
            {
                CreatedAt = DateTime.Now,
                Username = Truncate(username, 100),
                Role = Truncate(role, 50),
                Action = Truncate(action, 100),
                IpAddress = Truncate(ipAddress, 45)
            };

            _context.AuditLogs.Add(log);

            await _context.SaveChangesAsync();
        }

        private static string Truncate(string? value, int maxLength)
        {
            value ??= "";
            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }
    }
}