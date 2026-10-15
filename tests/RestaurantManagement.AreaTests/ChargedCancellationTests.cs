using RestaurantManagement.DbTool;
using RestaurantManagement.Web.Models.Cashier;
using RestaurantManagement.Web.Models.Kitchen;
using RestaurantManagement.Web.Models.Orders;

/// <summary>
/// S3-05 Task 3 (không cần database): Quản lý huỷ món đã bắt đầu chế biến, món vẫn tính tiền.
/// Trạng thái được huỷ, ba lý do, ô xác nhận, nhãn "Đã huỷ có tính tiền", tạm tính không giảm, thẻ "DỪNG MÓN" của bếp,
/// báo cáo cuối ca, quyền (API ghi nhận dừng món chỉ cho Bếp) và quy tắc trong migration 058.
/// Phần chạy thật trên SQL Server + web: verify-charged-cancel (hoặc verify).
/// </summary>
internal static class ChargedCancellationTests
{
    internal static void Run(Action<bool, string> check)
    {
        // Trạng thái: Quản lý huỷ được Chờ bếp (không tính tiền), Đang nấu, Đã xong (có tính tiền); Đã mang ra thì không.
        check(new[] { "Pending", "Preparing", "Ready" }.All(OrderCancelRules.ManagerCanCancel) && !OrderCancelRules.ManagerCanCancel("Served")
              && !OrderCancelRules.ManagerCanCancel("Cancelled"),
            "S3-05 charged: the manager can cancel a line waiting, being cooked or ready, but not a served one");
        check(OrderCancelRules.IsChargedStatus("Preparing") && OrderCancelRules.IsChargedStatus("Ready")
              && !OrderCancelRules.IsChargedStatus("Pending") && !OrderCancelRules.IsChargedStatus("Served") && !OrderCancelRules.IsChargedStatus(null),
            "S3-05 charged: cancelling a line being cooked or ready is a charged cancellation");
        check(OrderCancelRules.ChargedReasons.Select(r => r.Code).SequenceEqual(new[] { "ChangedMind", "Mistake", "SoldOut" })
              && !OrderCancelRules.IsAllowedForCharged("ManagerOverride") && !OrderCancelRules.IsAllowedForCharged(null)
              && !OrderCancelRules.IsAllowedForCharged("Hack"),
            "S3-05 charged: a charged cancellation takes exactly one of three reasons");
        check(OrderCancelRules.RejectionMessage(51142, "x") == OrderCancelRules.ChargedReasonRequired
              && OrderCancelRules.RejectionMessage(51144, "x") == OrderCancelRules.ChargedConfirmRequired
              && OrderCancelRules.RejectionMessage(51143, "served") == "served",
            "S3-05 charged: refusals (reason, confirmation, served line) are explained in plain words");

        // Hoá đơn: dòng huỷ có tính tiền giữ trên hoá đơn, nhãn riêng, tạm tính không giảm.
        var now = new DateTime(2026, 10, 15, 19, 0, 0);
        var cooking = new ProvisionalBillLine(1, "A01", "Lẩu thái", "nồi", 250000m, 1, 250000m, "Preparing", false, now, null, null);
        var charged = cooking with { Status = "Cancelled", ChargeWhenCancelled = true, CancelReason = "ChangedMind", CancelledAt = now, CancelledByName = "Quản lý A" };
        var served = cooking with { Id = 2, Status = "Served" };
        var before = new ProvisionalBillViewModel(1, "A01", 2, "Open", now, new[] { cooking, served });
        var after = new ProvisionalBillViewModel(1, "A01", 2, "Open", now, new[] { charged, served });
        check(after.Subtotal == before.Subtotal && after.Lines.Count == 2 && charged.IsCharged && charged.IsChargedCancellation
              && charged.StatusLabel == "Đã huỷ có tính tiền" && OrderCancelRules.ChargedLabel == "Đã huỷ có tính tiền",
            "S3-05 charged: the line stays on the bill labelled \"Đã huỷ có tính tiền\" and the subtotal does not drop");
        check(served.ManagerLockedReason!.StartsWith("Món đã mang ra cho khách") && cooking.ManagerLockedReason is null
              && served.WaiterLockedReason!.StartsWith("Món đã được mang ra"),
            "S3-05 charged: a served line shows why it cannot be cancelled");
        var result = new OrderCancelResult("Cancelled", 1, "Lẩu thái", 1, "nồi", 250000m, true, "ChangedMind", null, "Quản lý A", 600000m);
        check(result.Message.StartsWith("Đã huỷ có tính tiền: 1 nồi Lẩu thái (Khách đổi ý)") && result.Message.Contains($"vẫn tính tiền {Vnd.Format(250000m)}")
              && result.Message.Contains("tạm tính không giảm") && result.Message.Contains("dừng món"),
            "S3-05 charged: the success message says the line stays charged and the kitchen is told to stop");

        // Bếp: thẻ "DỪNG MÓN".
        var stopCooking = new KitchenStopNotice(1, "A01", "Lẩu thái", 1, "nồi", "Preparing", "ChangedMind", "khách vội", now, "Quản lý A", 2);
        var stopReady = stopCooking with { OrderItemId = 2, FromStatus = "Ready", CancelReason = "SoldOut", CancellationNote = null, CancelledAt = now.AddMinutes(1) };
        check(stopCooking.Instruction == "Dừng chế biến món này." && stopReady.Instruction == "Không mang món này ra bàn."
              && stopCooking.Summary == "Bàn A01 · Đợt 2 · 1 nồi Lẩu thái (đang nấu) — Khách đổi ý · khách vội"
              && stopReady.Summary.Contains("(đã làm xong, chưa mang ra) — Hết nguyên liệu"),
            "S3-05 charged: the kitchen stop notice says what to stop, where and why");
        var screen = new KitchenScreenViewModel(Array.Empty<KitchenOrderItem>(), true, new[] { stopCooking, stopReady });
        check(screen.Stops.Select(s => s.OrderItemId).SequenceEqual(new long[] { 2, 1 }) && new KitchenScreenViewModel(Array.Empty<KitchenOrderItem>(), true).Stops.Count == 0,
            "S3-05 charged: the newest stop notice is on top; no notices by default");

        // Báo cáo cuối ca phân biệt huỷ có / không tính tiền.
        var entry = new ShiftCancellationEntry("Cancelled", DateTime.UtcNow, 1, "A01", 1, 1, "Lẩu thái", 1, "nồi", 250000m, "Ready", true,
            "SoldOut", null, "Quản lý A", "Manager", null);
        check(entry.KindLabel == "Đã huỷ có tính tiền" && entry.ChargeKind == "charged" && entry.ChargeLabel.StartsWith("Vẫn tính tiền")
              && entry.ChargeLabel.EndsWith("(đã làm xong)") && (entry with { ChargeWhenCancelled = false, ItemStatus = "Pending" }).KindLabel == "Đã hủy",
            "S3-05 charged: the end-of-shift report labels a charged cancellation apart from a free one");

        // Quyền: chỉ Bếp xác nhận dừng món.
        var ack = ApiAccessMatrix.All.Single(e => e.Key == "Kitchen.StopAck POST");
        check(ApiAccessMatrix.IsAllowed(ack, "Kitchen") && new[] { "Manager", "Waiter", "Cashier" }.All(role => !ApiAccessMatrix.IsAllowed(ack, role)),
            "S3-05 charged: only the kitchen acknowledges a stop notice");

        var root = DatabaseTool.Root;
        string Source(params string[] parts) => File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
        var bill = Source("src", "RestaurantManagement.Web", "Views", "ProvisionalBill", "Index.cshtml");
        check(bill.Contains("name=\"confirmCharged\" value=\"true\" id=\"cancel-confirm-charged\" disabled")
              && bill.Contains("data-reason-charged=\"@(OrderCancelRules.IsAllowedForCharged(option.Code) ? \"true\" : \"false\")\"")
              && bill.Contains("OrderCancelRules.ManagerCanCancel(line.Status)") && bill.Contains("data-charged-cancelled=\"@line.Id\""),
            "S3-05 charged: the bill has the manager's charged cancel action, three reasons, the confirmation and the label");
        var kitchen = Source("src", "RestaurantManagement.Web", "Views", "Kitchen", "Index.cshtml");
        check(kitchen.IndexOf("id=\"kitchen-stop-notices\"", StringComparison.Ordinal) > kitchen.IndexOf("<div id=\"kitchen-batches\">", StringComparison.Ordinal)
              && kitchen.Contains("asp-action=\"StopAck\""),
            "S3-05 charged: the stop notices are refreshed with the kitchen list (every 3 seconds)");
        var controller = Source("src", "RestaurantManagement.Web", "Controllers", "Orders", "ProvisionalBillController.cs");
        check(controller.Contains("await Refuse(51142, OrderCancelRules.ChargedReasonRequired)") && controller.Contains("await Refuse(51144, OrderCancelRules.ChargedConfirmRequired)"),
            "S3-05 charged: the web refuses (and logs) a charged cancellation without a valid reason or confirmation");

        var migration = Source("database", "migrations", "058_S305ChargedCancellation.sql");
        check(migration.Contains("THROW 51143") && migration.Contains("THROW 51142") && migration.Contains("THROW 51144")
              && migration.Contains("@ConfirmCharged bit = NULL") && migration.Contains("EXEC dbo.usp_LockOperations;"),
            "S3-05 charged SQL: served lines, the manager-only reason and a missing confirmation are refused under the shared lock");
        check(migration.IndexOf("THROW 51034", StringComparison.Ordinal) < migration.IndexOf("THROW 51144", StringComparison.Ordinal)
              && migration.Contains("ChargeWhenCancelled = CASE WHEN @status = 'Pending' THEN 0 ELSE 1 END")
              && migration.Contains("SET @outcome = 'AlreadyCancelled';"),
            "S3-05 charged SQL: the latest status is checked first, the line stays charged, and a repeat records nothing");
        check(migration.Contains("CREATE OR ALTER VIEW dbo.vw_KitchenStopNotices") && migration.Contains("EXEC dbo.usp_RequirePermission @ActorUserId, 'Kitchen.Manage';")
              && migration.Contains("THROW 51145"),
            "S3-05 charged SQL: stop notices are listed for the kitchen and acknowledged only by the kitchen");
    }
}
