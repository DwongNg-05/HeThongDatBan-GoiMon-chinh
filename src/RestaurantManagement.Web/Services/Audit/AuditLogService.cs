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

        // Hàm mới dùng cho các chức năng cần ghi log chi tiết
        public async Task LogAsync(
            int? actorUserId,
            string? actorRole,
            string action,
            string entityType,
            string? entityId,
            string? oldValues,
            string? newValues,
            string? reason,
            string? ipAddress)
        {
            var log = new AuditLog
            {
                ActorUserId = actorUserId,
                ActorRole = Truncate(actorRole, 20),
                Action = Truncate(action, 80) ?? "",
                EntityType = Truncate(entityType, 60) ?? "",
                EntityId = Truncate(entityId, 60),
                OldValues = oldValues,
                NewValues = newValues,
                Reason = Truncate(reason, 500),
                IpAddress = Truncate(ipAddress, 45),
                OccurredAt = DateTime.UtcNow
            };

            _context.AuditLogs.Add(log);

            await _context.SaveChangesAsync();
        }

        // Hàm giữ tương thích với code cũ trong AccountController
        public async Task LogAsync(
            string username,
            string role,
            string action,
            string ipAddress)
        {
            await LogAsync(
                actorUserId: null,
                actorRole: role,
                action: action,
                entityType: "Account",
                entityId: username,
                oldValues: null,
                newValues: null,
                reason: null,
                ipAddress: ipAddress);
        }

        private static string? Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            return value.Length <= maxLength
                ? value
                : value.Substring(0, maxLength);
        }
    }
}