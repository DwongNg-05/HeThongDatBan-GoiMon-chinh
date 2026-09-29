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
            string? username)
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
                x.CreatedAt >= fromDate.Value.Date);

            // Lọc đến hết ngày
            var endDate = toDate.Value.Date.AddDays(1);

            query = query.Where(x =>
                x.CreatedAt < endDate);

            // Lọc theo tài khoản
            if (!string.IsNullOrWhiteSpace(username))
            {
                query = query.Where(x =>
                    x.Username == username);
            }

            // Danh sách tài khoản để đưa vào dropdown
            var usernames = await _context.AuditLogs
                .Select(x => x.Username)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();

            var logs = await query
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync();

            // Gửi dữ liệu sang View
            ViewBag.FromDate = fromDate.Value.ToString("yyyy-MM-dd");
            ViewBag.ToDate = toDate.Value.ToString("yyyy-MM-dd");
            ViewBag.Username = username;
            ViewBag.Usernames = usernames;

            return View(logs);
        }
    }
}