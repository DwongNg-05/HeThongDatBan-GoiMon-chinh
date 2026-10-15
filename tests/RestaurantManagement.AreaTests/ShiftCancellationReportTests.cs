using RestaurantManagement.DbTool;
using RestaurantManagement.Web.Models.Cashier;
using RestaurantManagement.Web.Models.Orders;
using RestaurantManagement.Web.Security;

/// <summary>
/// S3-05 Task 2 (không cần database): nhãn từng dòng nhật ký huỷ (đơn, dòng món, người thực hiện, thời điểm, lý do, cách tính tiền),
/// tổng của báo cáo, ca trống, quyền chỉ Quản lý (thanh điều hướng + API), thông báo ca trống / lỗi tải trên giao diện,
/// ghi nhận yêu cầu bị từ chối ở controller và các quy tắc trong migration 057.
/// Phần chạy thật trên SQL Server + web: verify-cancel-report (hoặc verify).
/// </summary>
internal static class ShiftCancellationReportTests
{
    internal static void Run(Action<bool, string> check)
    {
        var opened = new DateTime(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc);
        var shift = new ShiftOption(5, "Ca tối", new DateTime(2026, 10, 15), opened, null, "Open");
        check(shift.Label == "Ca tối · 15/10/2026 · mở 17:00 · đang mở"
              && (shift with { ClosedAtUtc = opened.AddHours(5), Status = "Closed" }).Label == "Ca tối · 15/10/2026 · mở 17:00, chốt 22:00",
            "S3-05 report: a shift is labelled with its name, business day and Vietnam opening/closing time");

        var free = new ShiftCancellationEntry("Cancelled", opened.AddMinutes(30), 41, "A01", 2, 900, "Phở bò", 2, "tô", 120000m, "Pending", false,
            "ChangedMind", "khách vội", "Nguyễn Văn Phục", "Waiter", null);
        var charged = free with { OccurredAtUtc = opened.AddMinutes(45), OrderItemId = 901, ItemStatus = "Preparing", ChargeWhenCancelled = true,
            LineTotal = 50000m, Reason = "ManagerOverride", Note = null, ActorName = "Trần Quản Lý", ActorRole = "Manager" };
        var rejected = new ShiftCancellationEntry("Rejected", opened.AddMinutes(50), 41, "A01", 2, 902, "Cơm rang", 1, "đĩa", 70000m, "Preparing", null,
            "Mistake", null, "Nguyễn Văn Phục", "Waiter", "Bếp đã bắt đầu chế biến món này.");
        check(free.KindLabel == "Đã hủy" && free.OccurredAtLabel == "17:30:00 15/10/2026" && free.OrderLabel == "Hoá đơn #41 · Bàn A01 · Đợt 2"
              && free.LineLabel == "2 tô Phở bò" && free.ActorLabel == "Nguyễn Văn Phục (Phục vụ)" && free.ReasonLabel == "Khách đổi ý · Ghi chú: khách vội",
            "S3-05 report: a cancellation shows the order, the line, the actor with role, the Vietnam time and the reason");
        check(free.ChargeKind == "free" && free.ChargeLabel.StartsWith("Không tính tiền (trừ ") && free.ChargeLabel.Contains(Vnd.Format(120000m))
              && charged.ChargeKind == "charged" && charged.ChargeLabel.StartsWith("Vẫn tính tiền ") && charged.ChargeLabel.Contains(Vnd.Format(50000m))
              && charged.ReasonLabel == "Quản lý yêu cầu" && charged.ActorLabel == "Trần Quản Lý (Quản lý)",
            "S3-05 report: a free cancellation (before cooking) and a charged one (after cooking started) are told apart");
        check(rejected.KindLabel == "Bị từ chối" && rejected.ChargeKind == "none" && rejected.ChargeLabel == "Không đổi (yêu cầu không được thực hiện)"
              && rejected.ReasonLabel == "Gọi nhầm món",
            "S3-05 report: a refused request is marked as refused and does not change the bill");
        var forged = new ShiftCancellationEntry("Rejected", opened, 41, null, null, 999999, null, null, null, null, null, null, null, null, "A", "Kitchen", "x");
        check(forged.LineLabel == "Dòng món #999999 (không tồn tại)" && forged.ReasonLabel == "Không chọn lý do" && forged.ActorLabel == "A (Bếp)"
              && forged.OrderLabel == "Hoá đơn #41",
            "S3-05 report: a refused request for a line that does not exist and without a reason is still readable");

        var report = new ShiftCancellationReport(shift, new[] { free, charged, rejected });
        check(report.CancelledCount == 2 && report.RejectedCount == 1 && report.FreeAmount == 120000m && report.ChargedAmount == 50000m,
            "S3-05 report: totals count successful and refused cancellations and split free from charged amounts");
        var empty = new ShiftCancellationReport(shift, Array.Empty<ShiftCancellationEntry>());
        check(empty.CancelledCount == 0 && empty.RejectedCount == 0 && empty.FreeAmount == 0 && empty.ChargedAmount == 0,
            "S3-05 report: a shift without cancellations has zero totals");

        // Quyền: chỉ Quản lý — thanh điều hướng và cả API dữ liệu.
        var page = ApiAccessMatrix.All.Single(e => e.Key == "ShiftReports.Cancellations GET");
        var data = ApiAccessMatrix.All.Single(e => e.Key == "ShiftReports.CancellationsData GET");
        check(ApiAccessMatrix.IsAllowed(page, "Manager") && ApiAccessMatrix.IsAllowed(data, "Manager")
              && new[] { "Waiter", "Kitchen", "Cashier" }.All(role => !ApiAccessMatrix.IsAllowed(page, role) && !ApiAccessMatrix.IsAllowed(data, role)),
            "S3-05 report: only the manager may open the report page or request its data directly");
        check(RoleNavigation.Items.Single(i => i.Key == "shift-cancellations") is { Path: "/Reports/Shift/Cancellations", Title: "Báo cáo huỷ món" }
              && ApiAuthorizationVerification.ExpectedNavigation.Where(n => n.Key != "Manager").All(n => !n.Value.Nav.Contains("shift-cancellations")),
            "S3-05 report: the report is on the manager's navigation bar only");

        var root = DatabaseTool.Root;
        string Source(params string[] parts) => File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
        var view = Source("src", "RestaurantManagement.Web", "Views", "ShiftReports", "Cancellations.cshtml");
        check(view.Contains("Ca này không có lần huỷ món nào.") && view.Contains("id=\"shift-cancellations-error\"") && view.Contains("Chưa có ca nào.")
              && view.Contains("@entry.ChargeLabel") && view.Contains("@entry.ActorLabel") && view.Contains("@entry.ReasonLabel")
              && view.Contains("@entry.OrderLabel") && view.Contains("@entry.LineLabel") && view.Contains("@entry.OccurredAtLabel"),
            "S3-05 report: the page shows every column, a message for an empty shift and a message when the report cannot be loaded");
        var controller = Source("src", "RestaurantManagement.Web", "Controllers", "Orders", "ShiftReportsController.cs");
        check(controller.Contains("[Authorize(Roles = AppRoles.Manager)]") && controller.Contains("Status503ServiceUnavailable"),
            "S3-05 report: the controller is manager-only and returns a load error instead of an empty report");
        var bill = Source("src", "RestaurantManagement.Web", "Controllers", "Orders", "ProvisionalBillController.cs");
        check(bill.Contains("await Refuse(51032, OrderCancelRules.MissingReason)") && bill.Contains("await Refuse(51036, OrderCancelRules.ManagerOnlyReason)")
              && bill.Contains("await Refuse(51037, OrderCancelRules.InvalidRequest)") && bill.Contains("await Refuse(exception.Number,"),
            "S3-05 report: every refused cancel request (checked on the web or by the database) is logged");

        var migration = Source("database", "migrations", "057_S305ShiftCancellationReport.sql");
        check(migration.Contains("EXEC dbo.usp_RequirePermission @ActorUserId, 'Audit.Read';") && migration.Contains("THROW 51141"),
            "S3-05 SQL: the report checks the manager-only permission Audit.Read in the database");
        check(migration.Contains("WHERE e.ToStatus = 'Cancelled' AND se.ShiftId = @ShiftId") && migration.Contains("WHERE x.ShiftId = @ShiftId")
              && migration.Contains("INSTEAD OF UPDATE, DELETE"),
            "S3-05 SQL: entries belong to the shift of the dining session; the refusal log is append-only");
    }
}
