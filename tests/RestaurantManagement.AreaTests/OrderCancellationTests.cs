using System.Text.RegularExpressions;
using RestaurantManagement.Web.Models.Cashier;
using RestaurantManagement.Web.Models.Kitchen;
using RestaurantManagement.Web.Models.Orders;

/// <summary>
/// S3-05 Task 1 (không cần database): lý do huỷ bắt buộc (ba lý do cho Phục vụ), nút huỷ của Phục vụ bị vô hiệu khi bếp đã bắt đầu,
/// tạm tính bỏ dòng huỷ trước khi chế biến, thông báo kết quả (kể cả gửi lặp), màn hình bếp bỏ món đã huỷ và tải lại ≤ 3 giây,
/// hộp xác nhận không chọn sẵn lý do, và các quy tắc trong migration 056.
/// Phần chạy thật trên SQL Server + web: verify-cancel-item (hoặc verify).
/// </summary>
internal static class OrderCancellationTests
{
    internal static void Run(Action<bool, string> check)
    {
        // Lý do: Phục vụ đúng ba lý do; Quản lý thêm "Quản lý yêu cầu".
        check(OrderCancelRules.ReasonsFor(isManager: false).Select(r => r.Code).SequenceEqual(new[] { "ChangedMind", "Mistake", "SoldOut" })
              && OrderCancelRules.ReasonsFor(isManager: false).Select(r => r.Label).SequenceEqual(new[] { "Khách đổi ý", "Gọi nhầm món", "Hết nguyên liệu" }),
            "S3-05: the waiter chooses from exactly three reasons — changed mind, ordered by mistake, out of ingredients");
        check(OrderCancelRules.ReasonsFor(isManager: true).Select(r => r.Code).SequenceEqual(new[] { "ChangedMind", "Mistake", "SoldOut", "ManagerOverride" }),
            "S3-05: the manager also has the reason \"Quản lý yêu cầu\"");

        // Thiếu / sai lý do bị từ chối, kể cả khi gửi trực tiếp; "Quản lý yêu cầu" không dành cho Phục vụ.
        check(new[] { null, "", " ", "changedmind", "Hack", "ManagerOverride" }.All(reason => !OrderCancelRules.IsAllowed(reason, isManager: false)),
            "S3-05: a missing, unknown or manager-only reason is refused for the waiter");
        check(new[] { "ChangedMind", "Mistake", "SoldOut" }.All(reason => OrderCancelRules.IsAllowed(reason, isManager: false))
              && OrderCancelRules.IsAllowed("ManagerOverride", isManager: true) && !OrderCancelRules.IsAllowed("Hack", isManager: true),
            "S3-05: each of the three reasons is accepted; the manager may also use \"Quản lý yêu cầu\"");
        check(new[] { "Pending", "Preparing", "Ready", "Served" }.All(status => OrderCancelRules.IsKnownStatus(status))
              && !OrderCancelRules.IsKnownStatus("Cancelled") && !OrderCancelRules.IsKnownStatus(null) && !OrderCancelRules.IsKnownStatus("pending"),
            "S3-05: only a real line status is accepted as the status seen when the dialog was opened");

        // Nút huỷ: Phục vụ chỉ huỷ món còn chờ bếp; món bếp đã bắt đầu có nút bị vô hiệu kèm lý do.
        check(OrderCancelRules.WaiterCanCancel("Pending") && new[] { "Preparing", "Ready", "Served" }.All(status => !OrderCancelRules.WaiterCanCancel(status)),
            "S3-05: the waiter can cancel only a line still waiting for the kitchen");
        var now = new DateTime(2026, 10, 15, 19, 0, 0);
        var pending = new ProvisionalBillLine(1, "A01", "Phở bò", "tô", 60000m, 2, 120000m, "Pending", false, now, null, null);
        var preparing = pending with { Id = 2, Status = "Preparing" };
        check(pending.WaiterLockedReason is null
              && preparing.WaiterLockedReason == "Bếp đang chế biến món này — chỉ Quản lý được hủy và món vẫn tính tiền."
              && (pending with { Status = "Ready" }).WaiterLockedReason!.StartsWith("Bếp đã làm xong")
              && (pending with { Status = "Served" }).WaiterLockedReason!.StartsWith("Món đã được mang ra"),
            "S3-05: a line the kitchen has started shows why the waiter's cancel button is disabled");

        // Tạm tính: huỷ trước khi bếp làm thì không tính tiền; huỷ sau khi bếp làm vẫn tính.
        var free = pending with { Id = 3, LineTotal = 50000m, Status = "Cancelled", CancelReason = "SoldOut", CancelledAt = now, CancelledByName = "Phục vụ A" };
        var charged = pending with { Id = 4, LineTotal = 80000m, Status = "Cancelled", ChargeWhenCancelled = true, CancelReason = "ManagerOverride" };
        var bill = new ProvisionalBillViewModel(1, "A01", 2, "Open", now, new[] { pending, free, charged });
        check(bill.Subtotal == 200000m && bill.ChargedLineCount == 2 && bill.CancelledLines.Count == 2 && !free.IsCharged && charged.IsCharged,
            "S3-05: the subtotal leaves out a line cancelled before cooking and keeps one cancelled after cooking");
        check(free.CancelReasonLabel == "Hết nguyên liệu" && charged.CancelReasonLabel == "Quản lý yêu cầu" && free.WaiterLockedReason is null,
            "S3-05: the cancellation log shows the same reason labels as the dialog");
        check(!bill.IsClosed && (bill with { Status = "Closed" }).IsClosed, "S3-05: a paid bill offers no cancellation");

        // Thông báo kết quả.
        var at = new DateTime(2026, 10, 15, 12, 5, 0, DateTimeKind.Utc);   // 19:05 giờ Việt Nam
        var done = new OrderCancelResult("Cancelled", 5, "Phở bò", 2, "tô", 120000m, false, "ChangedMind", at, "Phục vụ A", 350000m);
        check(!done.AlreadyCancelled
              && done.Message == $"Đã hủy 2 tô Phở bò (Khách đổi ý): trừ {Vnd.Format(120000m)} khỏi tạm tính và gỡ khỏi màn hình bếp. Tạm tính hiện tại: {Vnd.Format(350000m)}.",
            "S3-05: the success message names the line, the reason, the amount removed and the new subtotal");
        var stillCharged = done with { Charged = true, CancelReason = "ManagerOverride" };
        check(stillCharged.Message.Contains("(Quản lý yêu cầu)") && stillCharged.Message.Contains("vẫn tính tiền"),
            "S3-05: a cancellation after cooking says the line is still charged");
        var repeated = done with { Outcome = "AlreadyCancelled" };
        check(repeated.AlreadyCancelled && repeated.Message.Contains("đã được hủy trước đó bởi Phục vụ A lúc 19:05")
              && repeated.Message.Contains("không trừ tiền thêm") && !repeated.Message.Contains("trừ " + Vnd.Format(120000m)),
            "S3-05: a repeated confirmation says the line was already cancelled and nothing more is deducted");

        // Lỗi từ database → thông báo cho nhân viên.
        const string started = "Bếp đã bắt đầu chế biến món này nên Phục vụ không huỷ được. Chỉ Quản lý được huỷ và món vẫn tính tiền.";
        check(OrderCancelRules.RejectionMessage(51032, "x") == OrderCancelRules.MissingReason
              && OrderCancelRules.RejectionMessage(51036, "x") == OrderCancelRules.ManagerOnlyReason
              && OrderCancelRules.RejectionMessage(51035, started) == started
              && OrderCancelRules.RejectionMessage(51000, "x").Contains("bận")
              && OrderCancelRules.RejectionMessage(51001, "x").Contains("không có quyền"),
            "S3-05: every refusal from the database is shown to the staff member in plain words");
        check(OrderCancelStore.NormalizeNote("   ") is null && OrderCancelStore.NormalizeNote(" khách gọi lại ") == "khách gọi lại"
              && OrderCancelStore.NormalizeNote(new string('a', 600))!.Length == 500,
            "S3-05: the optional note is trimmed, empty becomes nothing, and it is cut at 500 characters");

        // Màn hình bếp: món đã huỷ không nằm trong hàng đợi (một đợt chỉ còn món đã huỷ cũng biến mất).
        KitchenOrderItem KitchenLine(long id, string status, long batch, int number) =>
            new(id, "A01", "Phở bò", "tô", 1, null, status, now, 10) { SessionId = 7, BatchId = batch, BatchNumber = number, BatchSubmittedAt = now.AddMinutes(number) };
        var screen = new KitchenScreenViewModel(new[] { KitchenLine(1, "Pending", 70, 1), KitchenLine(2, "Cancelled", 70, 1), KitchenLine(3, "Cancelled", 71, 2) }, true);
        check(screen.Batches.Count == 1 && screen.Batches[0].Items.Select(i => i.Id).SequenceEqual(new long[] { 1 }),
            "S3-05: cancelled lines, and a round left with only cancelled lines, are not on the kitchen queue");

        var root = RestaurantManagement.DbTool.DatabaseTool.Root;
        string Source(params string[] parts) => File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
        var kitchenView = Source("src", "RestaurantManagement.Web", "Views", "Kitchen", "Index.cshtml");
        check(kitchenView.Contains("const refreshEveryMs = 3000;") && kitchenView.Contains("window.setInterval(refresh, refreshEveryMs);")
              && !kitchenView.Contains("if (batches.contains(document.activeElement)) return;"),
            "S3-05: the kitchen screen reloads every 3 seconds even while a button keeps the focus, so a cancelled line leaves within 5 seconds");
        check(Source("src", "RestaurantManagement.Web", "Controllers", "Kitchen", "KitchenController.cs").Contains("AND i.Status <> 'Cancelled'"),
            "S3-05: the kitchen query leaves out cancelled lines");

        // Hoá đơn: hộp xác nhận với lý do bắt buộc, không chọn sẵn; nút xác nhận tắt cho tới khi chọn lý do.
        var billView = Source("src", "RestaurantManagement.Web", "Views", "ProvisionalBill", "Index.cshtml");
        check(billView.Contains("<dialog class=\"provisional-cancel-dialog\" id=\"cancel-item-dialog\"")
              && billView.Contains("<input type=\"radio\" name=\"reason\" value=\"@option.Code\" required />")
              && !Regex.IsMatch(billView, "name=\"reason\"[^>]*checked") && !billView.Contains("<select id=\"reason-")
              && billView.Contains("data-cancel-confirm disabled") && billView.Contains("name=\"expectedStatus\""),
            "S3-05: the bill has a confirmation dialog with a required reason that is not pre-selected");

        // Migration 056.
        var migration = Source("database", "migrations", "056_S305CancelOrderItem.sql");
        check(migration.Contains("EXEC dbo.usp_LockOperations;") && migration.Contains("WITH (UPDLOCK)")
              && migration.Contains("THROW 51034") && migration.Contains("THROW 51035"),
            "S3-05 SQL: the latest status is checked under the shared lock; a line the kitchen started meanwhile is refused");
        check(migration.Contains("SET @outcome = 'AlreadyCancelled';")
              && migration.Contains("ChargeWhenCancelled = CASE WHEN @status = 'Pending' THEN 0 ELSE 1 END"),
            "S3-05 SQL: a repeated confirmation records nothing more; cancelling before cooking is free");
        check(migration.Contains("THROW 51033") && migration.Contains("THROW 51036") && migration.Contains("THROW 51032"),
            "S3-05 SQL: a line of another bill, the manager-only reason and a missing reason are refused");
        check(migration.Contains("INSERT dbo.OrderItemEvents(OrderItemId, FromStatus, ToStatus, ActorUserId, Reason)"),
            "S3-05 SQL: every successful cancellation is logged with the line, the actor, the time and the reason");
    }
}
