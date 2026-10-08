using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Controllers;
using RestaurantManagement.Web.Models;

/// <summary>S1-05 Task 1: quy tắc ghi và hiển thị nhật ký bảo mật, quyền truy cập màn hình nhật ký.</summary>
internal static class SecurityAuditTests
{
    internal static void Run(Action<bool, string> check)
    {
        check(SecurityAuditStore.MaskIdentifier("khach-la") == "kh*** (không tồn tại)", "Audit: unknown identifier masked to two characters");
        check(SecurityAuditStore.MaskIdentifier("  0999999999 ") == "09*** (không tồn tại)", "Audit: unknown phone masked");
        check(SecurityAuditStore.MaskIdentifier("a") == "a*** (không tồn tại)" && SecurityAuditStore.MaskIdentifier("  ") == "(trống)" && SecurityAuditStore.MaskIdentifier(null) == "(trống)",
            "Audit: short and empty identifiers handled");
        check(SecurityAuditStore.MaskIdentifier(new string('x', 200)).Length <= 50, "Audit: masked identifier fits UserName column");

        check(ClientIp.Normalize(IPAddress.Parse("::ffff:192.168.1.20")) == "192.168.1.20", "Audit: IPv4-mapped IPv6 shown as IPv4");
        check(ClientIp.Normalize(IPAddress.IPv6Loopback) == "::1" && ClientIp.Normalize(IPAddress.Loopback) == "127.0.0.1", "Audit: loopback addresses kept");
        check(ClientIp.Normalize((IPAddress?)null) == "unknown" && ClientIp.Normalize(" ") == "unknown", "Audit: missing IP recorded as unknown");
        check(ClientIp.Normalize(new string('1', 60)).Length == 45, "Audit: IP fits column");

        var utc = new DateTime(2026, 10, 1, 17, 30, 5, DateTimeKind.Utc);
        var entry = new SecurityAuditEntry(1, utc, "manager", "Manager", "Quản lý", SecurityAuditEntry.LoginSucceeded, null, "127.0.0.1");
        check(entry.OccurredAtVietnam == new DateTime(2026, 10, 2, 0, 30, 5) && entry.ActionLabel == "Đăng nhập thành công" && entry.RoleLabel == "Quản lý",
            "Audit: time shown in UTC+7 with Vietnamese action and role");
        check((entry with { Action = SecurityAuditEntry.LoginFailed, RoleCode = null, RoleName = null }) is { ActionLabel: "Đăng nhập thất bại", RoleLabel: "Không xác định" },
            "Audit: failed login for unknown account labelled");
        check((entry with { Action = SecurityAuditEntry.PriceChanged }).ActionLabel == "Sửa giá món", "Audit: price change labelled");

        var authorize = typeof(AuditLogsController).GetCustomAttribute<AuthorizeAttribute>();
        check(authorize?.Roles == "Manager" && typeof(AuditLogsController).GetCustomAttribute<AllowAnonymousAttribute>() is null,
            "Audit: screen restricted to Manager role");
        var actions = typeof(AuditLogsController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        check(actions.All(m => m.GetCustomAttribute<HttpGetAttribute>() is not null) && actions.All(m => m.GetCustomAttribute<AllowAnonymousAttribute>() is null),
            "Audit: screen is read-only (GET only, no anonymous action)");

        // S1-05 Task 2: bộ lọc khoảng ngày (giờ Việt Nam) và tài khoản.
        var today = new DateOnly(2026, 10, 1);
        check(SecurityAuditFilter.TodayVietnam(new DateTime(2026, 9, 30, 17, 0, 0, DateTimeKind.Utc)) == today
            && SecurityAuditFilter.TodayVietnam(new DateTime(2026, 9, 30, 16, 59, 59, DateTimeKind.Utc)) == new DateOnly(2026, 9, 30),
            "Audit filter: 'today' follows Vietnam midnight (17:00 UTC)");
        var known = new[] { 1, 2 };
        var byDefault = SecurityAuditFilter.Resolve(null, null, null, today, known);
        check(byDefault is { IsValid: true, IsDefault: true, UserId: null, Days: 7 } && byDefault.FromDate == new DateOnly(2026, 9, 25) && byDefault.ToDate == today,
            "Audit filter: default is today and the 6 days before");
        check(byDefault.FromUtc == new DateTime(2026, 9, 24, 17, 0, 0) && byDefault.ToUtcExclusive == new DateTime(2026, 10, 1, 17, 0, 0),
            "Audit filter: Vietnam day bounds converted to UTC [from 00:00, next day 00:00)");
        var range = SecurityAuditFilter.Resolve(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 10), 2, today, known);
        check(range is { IsValid: true, IsDefault: false, UserId: 2, UnknownAccounts: false, Days: 10 }, "Audit filter: date range with account");
        check(SecurityAuditFilter.Resolve(new DateOnly(2026, 9, 20), null, null, today, known) is { IsValid: true, Days: 12 } r1 && r1.ToDate == today,
            "Audit filter: missing end date means today");
        check(SecurityAuditFilter.Resolve(null, new DateOnly(2026, 9, 10), null, today, known).FromDate == new DateOnly(2026, 9, 4),
            "Audit filter: missing start date means 7 days ending at the chosen date");
        check(SecurityAuditFilter.Resolve(new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 10), null, today, known) is { IsValid: true, Days: 1 },
            "Audit filter: single day allowed");
        check(SecurityAuditFilter.Resolve(new DateOnly(2026, 9, 11), new DateOnly(2026, 9, 10), null, today, known).Errors.SequenceEqual(new[] { SecurityAuditFilter.ErrorOrder }),
            "Audit filter: start after end rejected");
        check(SecurityAuditFilter.Resolve(new DateOnly(2026, 7, 3), today, null, today, known).Errors.SequenceEqual(new[] { SecurityAuditFilter.ErrorTooLong })
            && SecurityAuditFilter.Resolve(new DateOnly(2026, 7, 4), today, null, today, known) is { IsValid: true, Days: 90 },
            "Audit filter: 90 days allowed, 91 days rejected");
        check(SecurityAuditFilter.Resolve(null, null, 0, today, known) is { IsValid: true, UnknownAccounts: true, IsDefault: false },
            "Audit filter: 0 selects unknown identifiers");
        check(SecurityAuditFilter.Resolve(null, null, 99, today, known).Errors.Contains(SecurityAuditFilter.ErrorAccount)
            && SecurityAuditFilter.Resolve(null, null, -1, today, known).Errors.Contains(SecurityAuditFilter.ErrorAccount),
            "Audit filter: account must come from the list");
        check(SecurityAuditFilter.Resolve(DateOnly.MinValue, DateOnly.MinValue, null, today, known) is { IsValid: false } bad
            && bad.Errors.Contains(SecurityAuditFilter.ErrorInvalidDate) && bad.FromDate == new DateOnly(2026, 9, 25),
            "Audit filter: out-of-range dates rejected without overflow");
        check(SecurityAuditFilter.Resolve(null, null, null, today, known, invalidInput: true).Errors.SequenceEqual(new[] { SecurityAuditFilter.ErrorInvalidDate }),
            "Audit filter: malformed date input reported once");
        var vm = new SecurityAuditViewModel(range, new[] { new SecurityAuditAccount(2, "waiter", "Phục vụ", true), new SecurityAuditAccount(3, "old", "Bếp", false) }, SecurityAuditPage.Empty);
        check(vm.AccountLabel == "waiter" && vm.Accounts[1].Label == "old — Bếp (ngừng hoạt động)"
            && (vm with { Filter = byDefault }).AccountLabel == "Tất cả tài khoản", "Audit filter: account labels");
        var actionParams = typeof(AuditLogsController).GetMethod(nameof(AuditLogsController.Index))!.GetParameters().Select(p => p.Name).ToArray();
        check(actionParams.SequenceEqual(new[] { "fromDate", "toDate", "userId", "page" }), "Audit filter: query parameters fromDate, toDate, userId, page");

        // Phân trang nhật ký hệ thống.
        var rows = Enumerable.Range(1, 50).Select(i => entry with { Id = i }).ToArray();
        var first = new SecurityAuditPage(rows, 120, 1, 50);
        check(first is { TotalPages: 3, HasPrevious: false, HasNext: true, FirstRow: 1, LastRow: 50 }, "Audit paging: first page of 120 rows");
        var last = new SecurityAuditPage(rows[..20], 120, 3, 50);
        check(last is { HasPrevious: true, HasNext: false, FirstRow: 101, LastRow: 120 }, "Audit paging: last page shows remaining rows");
        check(SecurityAuditPage.Empty is { TotalPages: 1, HasPrevious: false, HasNext: false, FirstRow: 0, LastRow: 0 }, "Audit paging: empty result has one page");
        check(new SecurityAuditPage(rows, 100, 2, 50).TotalPages == 2 && new SecurityAuditPage(rows, 101, 1, 50).TotalPages == 3,
            "Audit paging: page count rounds up");
        check(SecurityAuditPage.Skip(1) == 0 && SecurityAuditPage.Skip(3) == 2 * SecurityAuditFilter.PageSize && SecurityAuditPage.Skip(0) == 0 && SecurityAuditPage.Skip(-5) == 0
            && SecurityAuditPage.Skip(int.MaxValue) == int.MaxValue, "Audit paging: offset from page number, invalid pages treated as page 1, no overflow");
        check(new SecurityAuditPage(rows, 1000, 6, 50).PageLinks().SequenceEqual(new int?[] { 1, null, 4, 5, 6, 7, 8, null, 20 }),
            "Audit paging: links keep first, last and two pages around current");
        check(new SecurityAuditPage(rows, 120, 1, 50).PageLinks().SequenceEqual(new int?[] { 1, 2, 3 })
            && SecurityAuditPage.Empty.PageLinks().SequenceEqual(new int?[] { 1 }), "Audit paging: short lists have no ellipsis");

        var candidate = new LoginUser(1, "manager", "Quản lý mẫu", "Manager");
        check(new LoginResult(null, 0, candidate) is { User: null, Candidate.UserName: "manager" }, "Audit: failed login keeps matched account for logging");
    }
}
