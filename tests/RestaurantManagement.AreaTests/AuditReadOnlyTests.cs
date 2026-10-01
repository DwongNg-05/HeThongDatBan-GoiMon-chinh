using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using RestaurantManagement.Data;
using RestaurantManagement.Data.Data;
using RestaurantManagement.Web.Controllers;
using RestaurantManagement.Web.Models;
using RestaurantManagement.Web.Pages.QuanLyMon;
using RestaurantManagement.Web.Services;

/// <summary>S1-05 Task 3: không tồn tại chức năng sửa/xoá nhật ký trong mã web; EF chặn sửa/xoá; màn hình chỉ cho Quản lý.</summary>
internal static class AuditReadOnlyTests
{
    internal static async Task Run(Action<bool, string> check)
    {
        var web = typeof(AuditLogsController).Assembly;
        var declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        // 1. Không có hàm nào tên kiểu Xoá/Sửa/Cập nhật nhật ký ở bất kỳ đâu trong ứng dụng web.
        var writeName = new Regex("^(Delete|Remove|Update|Edit|Clear|Truncate|Purge|Xoa|Sua|CapNhat)\\w*(Audit|NhatKy|Log)", RegexOptions.IgnoreCase);
        var offenders = web.GetTypes().SelectMany(t => t.GetMethods(declared).Select(m => $"{t.Name}.{m.Name}"))
            .Where(n => writeName.IsMatch(n[(n.IndexOf('.') + 1)..])).ToArray();
        check(offenders.Length == 0, "Read-only: no method that edits or deletes logs exists in the web app" + (offenders.Length > 0 ? ": " + string.Join(", ", offenders) : ""));

        // 2. Controller nhật ký: chỉ GET, chỉ Quản lý.
        var auditControllers = web.GetTypes().Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract
            && Regex.IsMatch(t.Name, "Audit|NhatKy", RegexOptions.IgnoreCase)).ToArray();
        check(auditControllers.SequenceEqual(new[] { typeof(AuditLogsController) }), "Read-only: AuditLogsController is the only log controller");
        var actions = typeof(AuditLogsController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        check(actions.Select(a => a.Name).SequenceEqual(new[] { "Index" })
            && actions.All(a => a.GetCustomAttributes<HttpMethodAttribute>().SelectMany(v => v.HttpMethods).SequenceEqual(new[] { "GET" })),
            "Read-only: log controller has a single GET action");
        check(typeof(AuditLogsController).GetCustomAttribute<AuthorizeAttribute>()?.Roles == "Manager"
            && AuditLogsController.PermissionDeniedError == 51001, "Access: log controller limited to Manager and maps database denial to 403");

        // 3. Trang Razor nhật ký giá: chỉ Quản lý, mọi phương thức ghi trả 405.
        var logPages = web.GetTypes().Where(t => typeof(PageModel).IsAssignableFrom(t) && Regex.IsMatch(t.Name, "Audit|NhatKy", RegexOptions.IgnoreCase)).ToArray();
        check(logPages.SequenceEqual(new[] { typeof(NhatKyGiaModel) }), "Read-only: price history is the only log page");
        check(typeof(NhatKyGiaModel).GetCustomAttribute<AuthorizeAttribute>()?.Roles == "Manager", "Access: price history page limited to Manager");
        var pageModel = new NhatKyGiaModel(new InMemoryQuanLyMonStore());
        var writes = new Func<IActionResult>[] { pageModel.OnPost, pageModel.OnPut, pageModel.OnDelete, pageModel.OnPatch };
        check(writes.All(h => h() is StatusCodeResult { StatusCode: 405 }), "Read-only: price history page rejects POST/PUT/DELETE/PATCH with 405");
        var handlers = typeof(NhatKyGiaModel).GetMethods(declared).Where(m => m.Name.StartsWith("On")).Select(m => m.Name).OrderBy(n => n).ToArray();
        check(handlers.SequenceEqual(new[] { "OnDelete", "OnGet", "OnPatch", "OnPost", "OnPut" }), "Read-only: price history page has no other handlers");

        // 4. Kho nhật ký bảo mật chỉ có ghi đăng nhập, tìm kiếm và danh sách tài khoản.
        var storeMethods = typeof(SecurityAuditStore).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name).OrderBy(n => n).ToArray();
        check(storeMethods.SequenceEqual(new[] { "Accounts", "Search", "WriteLogin" }), "Read-only: security log store exposes no update or delete");

        // 5. EF: sửa/xoá bản ghi nhật ký bị chặn trước khi chạm database (database giả, không kết nối được).
        const string fakeDb = "Server=127.0.0.1,1;Database=None;Connect Timeout=1;Encrypt=False";
        await using (var appDb = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(fakeDb).Options))
        {
            var log = new RestaurantManagement.Data.Models.AuditLog { Id = 1, CreatedAt = DateTime.UtcNow, Username = "manager", Role = "Manager", Action = "Login" };
            appDb.Attach(log);
            appDb.Entry(log).State = EntityState.Modified;
            check(Throws(() => appDb.SaveChanges()) && await ThrowsAsync(() => appDb.SaveChangesAsync()), "Read-only: EF refuses to update an audit log row");
            appDb.Entry(log).State = EntityState.Deleted;
            check(Throws(() => appDb.SaveChanges()) && await ThrowsAsync(() => appDb.SaveChangesAsync()), "Read-only: EF refuses to delete an audit log row");
            appDb.Entry(log).State = EntityState.Added;
            var allowed = true;
            try { AppendOnlyGuard.Ensure<RestaurantManagement.Data.Models.AuditLog>(appDb.ChangeTracker); } catch (InvalidOperationException) { allowed = false; }
            check(allowed, "Read-only: adding a log row is still allowed");
        }
        await using (var menuDb = new RestaurantDbContext(new DbContextOptionsBuilder<RestaurantDbContext>().UseSqlServer(fakeDb).Options))
        {
            var entry = new RestaurantManagement.Data.Models.GhiNhanThayDoiGia { Id = 1, MonAnId = 1, TenMon = "Gỏi cuốn", GiaCuVnd = 1, GiaMoiVnd = 2, NguoiSua = "manager" };
            menuDb.Attach(entry);
            menuDb.Entry(entry).State = EntityState.Deleted;
            check(Throws(() => menuDb.SaveChanges()), "Read-only: EF refuses to delete a price-history row");
        }
    }

    private static bool Throws(Action action)
    {
        try { action(); return false; }
        catch (InvalidOperationException ex) { return ex.Message == AppendOnlyGuard.Message; }
    }

    private static async Task<bool> ThrowsAsync(Func<Task> action)
    {
        try { await action(); return false; }
        catch (InvalidOperationException ex) { return ex.Message == AppendOnlyGuard.Message; }
    }
}
