using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using RestaurantManagement.DbTool;
using RestaurantManagement.Web.Authentication;
using RestaurantManagement.Web.Controllers;
using RestaurantManagement.Web.Models.Serving;
using RestaurantManagement.Web.Security;

/// <summary>
/// Món chờ mang ra (không cần database): thời gian chờ tính từ lúc hoàn thành, cảnh báo khi chờ quá 5 phút,
/// thứ tự theo thời điểm hoàn thành, món đã phục vụ không còn trên danh sách.
/// Kiểm thử với SQL Server + HTTP thật (Phục vụ thấy món, cảnh báo sau 5 phút, bấm "Đã mang ra"): KitchenCashierVerification
/// (lệnh <c>verify-api-permissions</c>). Phần đồng hồ chạy trên trình duyệt: tools/tests/serving-queue.test.cjs.
/// </summary>
internal static class ServingQueueTests
{
    internal static void Run(Action<bool, string> check)
    {
        var now = new DateTime(2026, 10, 11, 11, 0, 0, DateTimeKind.Utc);
        ReadyDishRow Ready(long id, string table, TimeSpan waited, string status = "Ready", DateTime? servedAt = null) =>
            new(id, table, $"Món {id}", "phần", 1, null, status, now - waited, servedAt);

        // T1. Chờ dưới 5 phút: không đánh dấu.
        var fresh = ServingQueue.Build([Ready(1, "A01", TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59))], now).Single();
        check(!fresh.IsOverdue && fresh.WaitLabel == "4:59", "Serving queue: a dish waiting 4:59 is not highlighted");
        check(!ServingQueue.Build([Ready(1, "A01", TimeSpan.Zero)], now).Single().IsOverdue,
            "Serving queue: a dish that has just been completed is not highlighted");
        check(!ServingQueue.Build([Ready(1, "A01", TimeSpan.FromMinutes(5))], now).Single().IsOverdue,
            "Serving queue: exactly 5:00 is not yet over the 5-minute threshold");

        // T2. Chờ quá 5 phút: đánh dấu nổi bật.
        var late = ServingQueue.Build([Ready(2, "A02", TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1))], now).Single();
        check(late.IsOverdue && late.WaitLabel == "5:01", "Serving queue: a dish waiting 5:01 is highlighted");
        check(ServingQueue.Build([Ready(2, "A02", TimeSpan.FromMinutes(5) + TimeSpan.FromMilliseconds(1))], now).Single().IsOverdue,
            "Serving queue: the highlight turns on as soon as the wait passes 5 minutes");
        check(ServingQueue.Build([Ready(2, "A02", TimeSpan.FromMinutes(83) + TimeSpan.FromSeconds(4))], now).Single().WaitLabel == "1:23:04",
            "Serving queue: waits over an hour are shown as h:mm:ss");

        // T3. Thứ tự theo thời điểm hoàn thành (dữ liệu vào lộn xộn), cảnh báo không đổi thứ tự.
        var rows = new[]
        {
            Ready(30, "B03", TimeSpan.FromMinutes(1)),
            Ready(10, "A01", TimeSpan.FromMinutes(7)),
            Ready(21, "C02", TimeSpan.FromMinutes(3)),
            Ready(20, "A05", TimeSpan.FromMinutes(3)),
        };
        var queue = ServingQueue.Build(rows, now);
        check(queue.Select(i => i.Id).SequenceEqual(new long[] { 10, 20, 21, 30 }),
            "Serving queue: sorted by completion time, oldest first; same completion time falls back to id");
        check(queue.Select(i => i.IsOverdue).SequenceEqual(new[] { true, false, false, false }),
            "Serving queue: only the dish waiting over 5 minutes is highlighted");
        var later = ServingQueue.Build(rows, now.AddMinutes(2) + TimeSpan.FromSeconds(30));
        check(later.Select(i => i.Id).SequenceEqual(queue.Select(i => i.Id)),
            "Serving queue: updating the warnings keeps the completion-time order");
        check(later.Select(i => i.IsOverdue).SequenceEqual(new[] { true, true, true, false }),
            "Serving queue: dishes that reach the threshold later are highlighted in place");

        // T4. Món đã phục vụ: không còn trên danh sách, không bị cảnh báo (kể cả đã chờ rất lâu trước khi mang ra).
        var withServed = ServingQueue.Build(
        [
            Ready(40, "A01", TimeSpan.FromMinutes(12), "Served", now.AddMinutes(-1)),
            Ready(41, "A02", TimeSpan.FromMinutes(9), "Ready", now.AddSeconds(-5)),
            Ready(42, "A03", TimeSpan.FromMinutes(8), "Cancelled"),
            Ready(43, "A04", TimeSpan.FromMinutes(6)),
            Ready(44, "A05", TimeSpan.FromMinutes(20), "Preparing"),
        ], now);
        check(withServed.Select(i => i.Id).SequenceEqual(new long[] { 43 }),
            "Serving queue: served, cancelled and not-yet-ready dishes are not on the waiting list");
        var snapshot = ServingQueue.Snapshot([Ready(40, "A01", TimeSpan.FromMinutes(12), "Served", now), Ready(43, "A04", TimeSpan.FromMinutes(6))], now);
        check(snapshot.OverdueCount == 1 && snapshot.Items.All(i => i.Id != 40),
            "Serving queue: a served dish is never counted as an overdue warning");

        // T5. Thời gian chờ được tính đúng mỗi lần danh sách cập nhật (tính lại từ ReadyAt, không cộng dồn).
        var readyAt = now.AddMinutes(-2);
        var row = new ReadyDishRow(50, "C10", "Phở bò", "tô", 2, "Ít hành", "Ready", readyAt, null);
        var first = ServingQueue.Build([row], now).Single();
        var second = ServingQueue.Build([row], now.AddSeconds(5)).Single();
        var third = ServingQueue.Build([row], now.AddMinutes(3).AddSeconds(1)).Single();
        check(first.WaitMs == 120_000 && second.WaitMs == 125_000 && third.WaitMs == 301_000,
            "Serving queue: wait time is recomputed from the completion time on every refresh (2:00 → 2:05 → 5:01)");
        check(!first.IsOverdue && !second.IsOverdue && third.IsOverdue && third.WaitLabel == "5:01",
            "Serving queue: the same dish becomes highlighted once a refresh finds it waiting over 5 minutes");
        check(ServingQueue.Build([row], readyAt.AddSeconds(-2)).Single().WaitMs == 0,
            "Serving queue: a small clock difference never shows a negative wait");
        check(first.ReadyAtVietnam == new DateTime(2026, 10, 11, 17, 58, 0) && first.Quantity == 2 && first.Notes == "Ít hành",
            "Serving queue: completion time is shown in Vietnam time with the dish details");

        // JSON cho trang tự cập nhật: ngưỡng 5 phút, thời gian chờ và cảnh báo của từng món theo đúng thứ tự.
        var json = System.Text.Json.JsonSerializer.Serialize(ServingQueue.ToJson(ServingQueue.Snapshot(rows, now)),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        check(json.Contains("\"overdueAfterMs\":300000") && json.Contains("\"overdueCount\":1")
            && json.IndexOf("\"id\":10", StringComparison.Ordinal) < json.IndexOf("\"id\":20", StringComparison.Ordinal)
            && json.IndexOf("\"id\":20", StringComparison.Ordinal) < json.IndexOf("\"id\":30", StringComparison.Ordinal)
            && json.Contains("\"waitMs\":420000") && json.Contains("\"readyAtUtc\":\"2026-10-11T10:53:00Z\""),
            "Serving queue: the refresh JSON carries the threshold, waits, warnings and the completion-time order");

        // ---- S3-08 Task 1 ----
        // Danh sách chờ có mã bàn, tên món, thời điểm hoàn thành; sắp từ sớm đến muộn.
        var listed = ServingQueue.Build(
        [
            new ReadyDishRow(71, "B07", "Gỏi cuốn", "phần", 1, null, "Ready", now.AddSeconds(-5), null),
            new ReadyDishRow(70, "A05", "Phở bò", "tô", 2, null, "Ready", now.AddSeconds(-40), null),
        ], now);
        check(listed.Select(i => (i.TableCode, i.ItemName, i.ReadyAtUtc)).SequenceEqual(new[]
            { ("A05", "Phở bò", now.AddSeconds(-40)), ("B07", "Gỏi cuốn", now.AddSeconds(-5)) }),
            "S3-08: the waiting list shows table code, dish name and completion time, earliest completion first");
        // Món mới hiện trong ≤ 5 giây: trang tải lại đủ thường xuyên, còn dư thời gian cho mạng.
        check(ServingQueue.RefreshIntervalMs <= ServingQueue.NotifyWithinMs - 1000 && ServingQueue.NotifyWithinMs == 5000,
            "S3-08: the page refreshes every 3 s so a newly finished dish is shown within 5 s");
        var refreshJson = System.Text.Json.JsonSerializer.Serialize(ServingQueue.ToJson(ServingQueue.Snapshot(
            [new ReadyDishRow(70, "A05", "Phở bò", "tô", 2, null, "Ready", now.AddSeconds(-1), null)], now)),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
            { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        check(refreshJson.Contains("\"tableCode\":\"A05\"") && refreshJson.Contains("\"itemName\":\"Phở bò\""),
            "S3-08: the refresh data used for the new-dish notice carries the table code and the dish name");

        // Xác nhận: người thành công; người đến sau (nhân viên khác) được báo ai đã mang ra; bấm lại chính mình.
        var servedTime = new DateTime(2026, 10, 11, 11, 1, 0, DateTimeKind.Utc); // 18:01 giờ Việt Nam
        var ok = ServingQueue.ServeOutcomeFor(true, 2, "A05", "Phở bò", 2, "Nguyễn An", servedTime);
        check(ok.Succeeded && ok.Message == "Đã mang ra: Bàn A05 · Phở bò.", "S3-08: a successful confirmation names the table and the dish");
        var other = ServingQueue.ServeOutcomeFor(false, 5, "A05", "Phở bò", 2, "Nguyễn An", servedTime);
        check(!other.Succeeded && other.Outcome == ServeOutcome.AlreadyServed
            && other.Message == "Bàn A05 · Phở bò đã được Nguyễn An mang ra lúc 18:01. Không cần mang lại.",
            "S3-08: the second staff member is told who already served the dish and when");
        var self = ServingQueue.ServeOutcomeFor(false, 2, "A05", "Phở bò", 2, "Nguyễn An", servedTime);
        check(!self.Succeeded && self.Message == "Bạn đã xác nhận mang ra Bàn A05 · Phở bò lúc 18:01 rồi.",
            "S3-08: confirming twice yourself is reported, not recorded again");
        check(ServingQueue.ServeOutcomeFor(false, 5, "A05", "Phở bò", null, null, null).Message == "Bàn A05 · Phở bò đã được nhân viên khác mang ra. Không cần mang lại.",
            "S3-08: the already-served message still reads well without a name or time");

        // Quyền: Phục vụ và Quản lý (Orders.Serve); Bếp, Thu ngân không. Tải lại tự động không gia hạn phiên đăng nhập.
        static string[] Roles(string action) => typeof(ServingController).GetMethod(action)!
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()
            .SelectMany(a => (a.Roles ?? "").Split(',')).Order().ToArray();
        check(new[] { nameof(ServingController.Index), nameof(ServingController.Queue), nameof(ServingController.Serve) }
                .All(a => Roles(a).SequenceEqual(new[] { AppRoles.Manager, AppRoles.Waiter })),
            "Serving queue: screen, refresh and \"Đã mang ra\" are open to Waiter and Manager only");
        check(typeof(ServingController).GetMethod(nameof(ServingController.Queue))!.IsDefined(typeof(PassiveSessionReadAttribute), false),
            "Serving queue: the automatic refresh does not keep an idle login alive");
        foreach (var role in AppRoles.All)
        {
            var expected = role is AppRoles.Manager or AppRoles.Waiter;
            check(ApiAccessMatrix.All.Where(e => e.Key.StartsWith("Serving.", StringComparison.Ordinal)).All(e => ApiAccessMatrix.IsAllowed(e, role) == expected),
                $"Serving queue: {role} {(expected ? "can" : "cannot")} use the serving queue");
        }
        var waiter = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, AppRoles.Waiter)], "Cookies"));
        check(RoleNavigation.For(waiter).Any(i => i.Key == "serving-queue" && i.Path == "/Serving" && i.Title == "Món chờ mang ra"),
            "Serving queue: Waiter sees \"Món chờ mang ra\" on the navigation");
    }
}
