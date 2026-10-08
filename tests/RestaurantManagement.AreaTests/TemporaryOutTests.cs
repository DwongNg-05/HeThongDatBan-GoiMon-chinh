using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using RestaurantManagement.DbTool;
using RestaurantManagement.Web.Controllers;
using RestaurantManagement.Web.Security;
using RestaurantManagement.Web.Services;

/// <summary>
/// S2-08 Task 1 (không cần database): quy tắc "Tạm hết" đã chốt với PO.
/// Kiểm thử đầy đủ với SQL Server + HTTP thật (bật/tắt, Bếp, Quản lý, không nhận order, ≤ 5 giây):
/// KitchenCashierVerification, lệnh <c>verify-api-permissions</c>.
/// </summary>
internal static class TemporaryOutTests
{
    internal static void Run(Action<bool, string> check)
    {
        // Quyền: Bếp và Quản lý; Phục vụ, Thu ngân không.
        static string[] Roles(string action) => typeof(KitchenController).GetMethod(action)!
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()
            .SelectMany(a => (a.Roles ?? "").Split(',')).Order().ToArray();
        check(TemporaryOutRules.AllowedRoles.Split(',').Order().SequenceEqual(new[] { AppRoles.Kitchen, AppRoles.Manager }),
            "S2-08: PO rule — only Kitchen and Manager may toggle \"Tạm hết\"");
        check(Roles(nameof(KitchenController.Dishes)).SequenceEqual(new[] { AppRoles.Kitchen, AppRoles.Manager })
            && Roles(nameof(KitchenController.TemporarilyOut)).SequenceEqual(new[] { AppRoles.Kitchen, AppRoles.Manager }),
            "S2-08: today's dish list and the one-tap toggle are open to Kitchen and Manager only");
        foreach (var role in new[] { AppRoles.Manager, AppRoles.Kitchen, AppRoles.Waiter, AppRoles.Cashier })
        {
            var endpoint = ApiAccessMatrix.All.Single(e => e.Key == "Kitchen.TemporarilyOut POST");
            var expected = role is AppRoles.Manager or AppRoles.Kitchen;
            check(ApiAccessMatrix.IsAllowed(endpoint, role) == expected, $"S2-08: {role} {(expected ? "can" : "cannot")} toggle \"Tạm hết\"");
        }
        var kitchen = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, AppRoles.Kitchen)], "Cookies"));
        check(RoleNavigation.For(kitchen).Any(i => i.Path == "/Kitchen/Dishes" && i.Title == "Món trong ngày"),
            "S2-08: Kitchen sees \"Món trong ngày\" on the navigation");

        // Trạng thái không nhận order: tạm hết hoặc hết trong ngày.
        var availability = new DishAvailability([3, 7], [7, 9]);
        check(availability.Unavailable.SequenceEqual(new[] { 3, 7, 9 }), "S2-08: unavailable list merges temporarily-out and sold-out-today, no duplicates");
        check(availability.IsUnavailable(3) && availability.IsUnavailable(9) && !availability.IsUnavailable(4),
            "S2-08: a temporarily-out dish takes no new order, other dishes do");

        // Cập nhật ≤ 5 giây: chu kỳ hỏi lại trạng thái để chừa thời gian cho mạng và máy chủ.
        check(TemporaryOutRules.PollIntervalMs > 0 && TemporaryOutRules.PollIntervalMs + 1000 <= TemporaryOutRules.MaxDisplayDelayMs
            && TemporaryOutRules.MaxDisplayDelayMs == 5000,
            "S2-08: pages refresh the status often enough to show changes within 5 seconds");
        var script = File.ReadAllText(Path.Combine(DatabaseTool.Root, "src", "RestaurantManagement.Web", "wwwroot", "js", "menu-availability.js"));
        check(script.Contains("data-availability-interval") && script.Contains("setInterval") && script.Contains("cache: 'no-store'"),
            "S2-08: menu-availability.js polls the status without browser cache");
        // S2-08 Task 2: nhật ký — chỉ Quản lý xem lịch sử; mỗi dòng ghi người thực hiện, món, trạng thái trước/sau, thời điểm.
        check(Roles(nameof(KitchenController.History)).SequenceEqual(new[] { AppRoles.Manager })
            && ApiAccessMatrix.IsAllowed(ApiAccessMatrix.All.Single(e => e.Key == "Kitchen.History GET"), AppRoles.Manager)
            && !ApiAccessMatrix.IsAllowed(ApiAccessMatrix.All.Single(e => e.Key == "Kitchen.History GET"), AppRoles.Kitchen),
            "S2-08 log: only Manager can open the temporarily-out history");
        var at = new DateTime(2026, 10, 6, 3, 15, 0, DateTimeKind.Utc);
        var turnedOn = new TemporaryOutLogEntry(1, 5, "Pepsi", false, true, 3, "Nguyễn Văn Bếp", "kitchen", "Bếp", at);
        var turnedOff = new TemporaryOutLogEntry(2, 5, "Pepsi", true, false, 1, "Trần Quản Lý", "manager", "Quản lý", at.AddMinutes(1));
        var system = new TemporaryOutLogEntry(3, 5, "Pepsi", true, false, null, null, null, null, at.AddHours(14));
        check(turnedOn is { ActionLabel: "Bật tạm hết", OldStateLabel: "Còn món", NewStateLabel: "Tạm hết", ActorLabel: "Nguyễn Văn Bếp (kitchen) · Bếp" },
            "S2-08 log: turning on shows the actor, Còn món → Tạm hết");
        check(turnedOff is { ActionLabel: "Tắt tạm hết", OldStateLabel: "Tạm hết", NewStateLabel: "Còn món", ActorLabel: "Trần Quản Lý (manager) · Quản lý" },
            "S2-08 log: turning off shows the actor, Tạm hết → Còn món");
        check(system.ActorLabel == TemporaryOutLogEntry.SystemActor && system.ActionLabel == "Tự đặt lại lúc 00:00" && turnedOn.ChangedAtUtc.Kind == DateTimeKind.Utc
            && RestaurantManagement.Web.Models.Reservations.VietnamTime.FromUtc(turnedOn.ChangedAtUtc).Hour == 10,
            "S2-08 log: time is stored in UTC and shown in Vietnam time; entries without a user are shown as the system");
        var sql = File.ReadAllText(Path.Combine(DatabaseTool.Root, "src", "RestaurantManagement.Web", "Services", "Menu", "DailyDishStore.cs"));
        check(sql.Contains("IF @old<>@IsTemporarilyOut") && sql.Contains("INSERT dbo.MenuTemporaryOutEvents(MenuItemId,OldIsTemporarilyOut,IsTemporarilyOut,ChangedBy,ChangedAt)")
            && !sql.Contains("OBJECT_ID('dbo.MenuTemporaryOutEvents'"),
            "S2-08 log: every real change always writes one log row in the same transaction (no change, no row)");

        // S2-08 Task 3: mốc 00:00 theo Asia/Ho_Chi_Minh (UTC+7) — 00:00 giờ Việt Nam = 17:00 UTC ngày hôm trước.
        DateTime Utc(int y, int mo, int d, int h, int mi, int s = 0, int ms = 0) => new(y, mo, d, h, mi, s, ms, DateTimeKind.Utc);
        check(TemporaryOutResetSchedule.Zone.BaseUtcOffset == TimeSpan.FromHours(7) && !TemporaryOutResetSchedule.Zone.SupportsDaylightSavingTime,
            "S2-08 reset: time zone is Asia/Ho_Chi_Minh (UTC+7, no daylight saving)");
        check(TemporaryOutResetSchedule.CurrentMidnightUtc(Utc(2026, 10, 6, 16, 59, 59, 999)) == Utc(2026, 10, 5, 17, 0)
            && TemporaryOutResetSchedule.NextMidnightUtc(Utc(2026, 10, 6, 16, 59, 59, 999)) == Utc(2026, 10, 6, 17, 0),
            "S2-08 reset: 23:59:59.999 (Vietnam) is still the old day; the next reset is 00:00 Vietnam = 17:00 UTC");
        check(TemporaryOutResetSchedule.CurrentMidnightUtc(Utc(2026, 10, 6, 17, 0)) == Utc(2026, 10, 6, 17, 0)
            && TemporaryOutResetSchedule.NextMidnightUtc(Utc(2026, 10, 6, 17, 0)) == Utc(2026, 10, 7, 17, 0),
            "S2-08 reset: at exactly 00:00 Vietnam the new day starts (reset due now, next one in 24 hours)");
        check(TemporaryOutResetSchedule.CurrentMidnightUtc(Utc(2026, 10, 6, 3, 0)) == Utc(2026, 10, 5, 17, 0)
            && TemporaryOutResetSchedule.CurrentMidnightUtc(Utc(2026, 12, 31, 18, 30)) == Utc(2026, 12, 31, 17, 0),
            "S2-08 reset: UTC morning and New Year's Eve map to the right Vietnam business day");
        var migration = File.ReadAllText(Path.Combine(DatabaseTool.Root, "database", "migrations", "040_S208DailyTemporaryOutReset.sql"));
        check(migration.Contains("e.ChangedAt>=@ScheduledFor") && migration.Contains("THROW 51090") && migration.Contains("JobName='TemporaryOutReset'"),
            "S2-08 reset: only dishes marked before 00:00 are reset, the cut-off must be 00:00 Vietnam, each day runs once");

        foreach (var view in new[] { "Views/Menu/Index.cshtml", "Pages/Shared/_MenuByCategory.cshtml", "Views/Kitchen/Dishes.cshtml" })
        {
            var html = File.ReadAllText(Path.Combine(DatabaseTool.Root, "src", "RestaurantManagement.Web", view));
            check(html.Contains("data-availability-url") && html.Contains("TemporaryOutRules.PollIntervalMs") && html.Contains("menu-availability.js"),
                $"S2-08: {view} refreshes \"Tạm hết\" automatically");
        }
    }
}
