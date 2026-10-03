using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantManagement.Data;

namespace RestaurantManagement.Web.Controllers
{
    public class AuditLogsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AuditLogsController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(
            DateTime? fromDate,
            DateTime? toDate,
            string? actorRole)
        {
            // Mặc định: 7 ngày gần nhất
            if (!fromDate.HasValue)
            {
                fromDate = DateTime.Today.AddDays(-6);
            }

            if (!toDate.HasValue)
            {
                toDate = DateTime.Today;
            }

            var query = _context.AuditLogs.AsQueryable();

            // Lọc từ ngày
            query = query.Where(x =>
                x.OccurredAt >= fromDate.Value.Date);

            // Lọc đến hết ngày
            var endDate = toDate.Value.Date.AddDays(1);

            query = query.Where(x =>
                x.OccurredAt < endDate);

            // Lọc theo vai trò người thực hiện
            if (!string.IsNullOrWhiteSpace(actorRole))
            {
                query = query.Where(x =>
                    x.ActorRole == actorRole);
            }

            // Danh sách vai trò để đưa vào dropdown
            var actorRoles = await _context.AuditLogs
                .Where(x => x.ActorRole != null)
                .Select(x => x.ActorRole!)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();

            var logs = await query
                .OrderByDescending(x => x.OccurredAt)
                .ToListAsync();

            // Gửi dữ liệu sang View
            ViewBag.FromDate = fromDate.Value.ToString("yyyy-MM-dd");
            ViewBag.ToDate = toDate.Value.ToString("yyyy-MM-dd");
            ViewBag.ActorRole = actorRole;
            ViewBag.ActorRoles = actorRoles;

            return View(logs);
        }
    }
}