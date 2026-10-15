using System.Security.Claims;
using RestaurantManagement.DbTool;
using RestaurantManagement.Web.Models.Orders;

/// <summary>
/// S4-01 Task 2 (không cần database): phần đầu hoá đơn tạm tính — tên quán, mã bàn, thời điểm bắt đầu phục vụ, thời điểm in,
/// người in, dòng chữ "TẠM TÍNH – KHÔNG PHẢI HOÁ ĐƠN THANH TOÁN", định dạng ngày/tháng/năm giờ:phút.
/// Phần chạy thật trên SQL Server + web: verify-bill-header (hoặc verify).
/// </summary>
internal static class BillHeaderTests
{
    internal static void Run(Action<bool, string> check)
    {
        // 12:05 UTC = 19:05 giờ Việt Nam.
        var opened = new DateTime(2026, 10, 15, 12, 5, 0, DateTimeKind.Utc);
        BillSourceItem Item(long id, int minutes, string status = "Pending") =>
            new(id, "Phở bò", "tô", 65_000m, 1, status, false, null, null, opened.AddMinutes(minutes));
        TableBillPreview Bill(string tables, string? name, params BillSourceItem[] items) =>
            TableBillPreviewCalculator.Build(tables.Split(',')[0].Trim(), tables, 10, "Open", "Serving", items) with
            {
                RestaurantName = name,
                ServiceOpenedAtUtc = opened
            };
        ClaimsPrincipal Staff(string userName, string? fullName)
        {
            var claims = new List<Claim> { new(ClaimTypes.Name, userName), new(ClaimTypes.Role, "Waiter") };
            if (fullName is not null) claims.Add(new Claim("FullName", fullName));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookies"));
        }

        // Tên quán theo cấu hình; cấu hình trống thì có tên dự phòng.
        var printedAt = new DateTime(2026, 10, 15, 13, 47, 31, DateTimeKind.Utc);
        var header = TableBillHeader.Create(Bill("A01", "  Bếp Nhà  ", Item(1, 15)), printedAt, "Nguyễn Văn A");
        check(header.RestaurantName == "Bếp Nhà" && TableBillHeader.Create(Bill("A01", "  "), printedAt, "A").RestaurantName == "Nhà hàng"
              && TableBillHeader.Create(Bill("A01", null), printedAt, "A").RestaurantName == BillHeaderRules.FallbackRestaurantName,
            "S4-01 header: the restaurant name comes from the configuration (fallback when it is empty)");

        // Mã bàn: bàn đang mở; nhóm bàn đã gộp hiện mọi mã bàn hiện tại.
        check(header.Tables == "A01" && TableBillHeader.Create(Bill("A01, A02", "Bếp Nhà"), printedAt, "A").Tables == "A01, A02",
            "S4-01 header: the table code is the open table; merged tables show every current code");

        // Bắt đầu phục vụ theo định nghĩa đã chốt (đề xuất: lúc mở bàn), dạng ngày/tháng/năm giờ:phút giờ Việt Nam.
        var withOrders = Bill("A01", "Bếp Nhà", Item(2, 40, "Cancelled"), Item(1, 15));
        check(BillHeaderRules.ServiceStartsAt == ServiceStartDefinition.TableOpened && header.ServiceStartedAtUtc == opened
              && header.ServiceStartedAtText == "15/10/2026 19:05" && header.ServiceStartLabel == "Bắt đầu phục vụ",
            "S4-01 header: the service start is when the table was opened (agreed definition), in Vietnam time");
        check(withOrders.FirstOrderAtUtc == opened.AddMinutes(15) && Bill("A01", "x").FirstOrderAtUtc is null
              && Bill("A01", "x", Item(9, 3, "Cancelled")).FirstOrderAtUtc == opened.AddMinutes(3),
            "S4-01 header: the first order time is also known (for the alternative definition), cancelled dishes included");

        // Thời điểm in: giờ của lần mở này, đổi mỗi lần mở lại.
        var later = TableBillHeader.Create(withOrders, printedAt.AddMinutes(2), "Nguyễn Văn A");
        check(header.PrintedAtText == "15/10/2026 20:47" && later.PrintedAtText == "15/10/2026 20:49" && later.PrintedAtUtc > header.PrintedAtUtc,
            "S4-01 header: the print time is the time of this opening and changes when the bill is opened again");

        // Định dạng ngày giờ cố định dd/MM/yyyy HH:mm (số 0 đứng đầu, 24 giờ, không theo văn hoá máy chủ).
        check(BillHeaderRules.FormatDateTime(new DateTime(2026, 1, 5, 7, 3, 0)) == "05/01/2026 07:03"
              && BillHeaderRules.FormatDateTime(new DateTime(2026, 12, 31, 23, 59, 59)) == "31/12/2026 23:59"
              && BillHeaderRules.DateTimePattern == "dd/MM/yyyy HH:mm",
            "S4-01 header: dates are day/month/year hour:minute");
        var oldCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("en-US");
            check(BillHeaderRules.FormatDateTime(new DateTime(2026, 1, 5, 19, 3, 0)) == "05/01/2026 19:03",
                "S4-01 header: the date format does not depend on the server culture");
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = oldCulture; }

        // Người in: nhân viên đang đăng nhập (họ tên), đổi nhân viên thì đổi tên.
        check(BillHeaderRules.PrinterName(Staff("waiter", "Nhân viên A")) == "Nhân viên A"
              && BillHeaderRules.PrinterName(Staff("manager", "Nhân viên B")) == "Nhân viên B"
              && BillHeaderRules.PrinterName(Staff("waiter2", null)) == "waiter2"
              && BillHeaderRules.PrinterName(new ClaimsPrincipal(new ClaimsIdentity())) == "Không rõ",
            "S4-01 header: the printer is the signed-in employee; another employee gives another name");

        // Dòng chữ đánh dấu tạm tính.
        check(BillHeaderRules.ShowProvisionalNotice && header.Notice == "TẠM TÍNH – KHÔNG PHẢI HOÁ ĐƠN THANH TOÁN",
            "S4-01 header: the provisional notice is shown");

        // API: phần đầu đi kèm dữ liệu tạm tính.
        var json = TableBillPreviewResponse.From(withOrders with { Header = later }).Header!;
        check(json.RestaurantName == "Bếp Nhà" && json.Tables == "A01" && json.ServiceStartedAtText == "15/10/2026 19:05"
              && json.PrintedAtText == "15/10/2026 20:49" && json.PrintedBy == "Nguyễn Văn A" && json.ServiceStartDefinition == "TableOpened"
              && json.Notice == BillHeaderRules.ProvisionalNotice && TableBillPreviewResponse.From(withOrders).Header is null,
            "S4-01 header API: the header is returned with the provisional bill data");

        // Nguồn: truy vấn đọc tên quán + lúc mở bàn; controller gắn người in theo người đang đăng nhập; phần đầu nằm trên bảng món.
        var root = DatabaseTool.Root;
        string Source(params string[] parts) => File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
        var sql = RestaurantManagement.Web.Services.TableBillPreviewStore.SessionQuery;
        check(sql.Contains("FROM dbo.RestaurantSettings rs WHERE rs.Id=1") && sql.Contains("MIN(s3.OpenedAt)")
              && sql.Contains("st2.ReleasedAt IS NULL"),
            "S4-01 header SQL: restaurant name from the settings, opening time of the bill group, only the tables held now");
        var controller = Source("src", "RestaurantManagement.Web", "Controllers", "Orders", "ProvisionalBillController.cs");
        var api = Source("src", "RestaurantManagement.Web", "Controllers", "Orders", "ProvisionalBillApiController.cs");
        check(controller.Contains("TableBillHeader.Create(result.Preview, DateTime.UtcNow, BillHeaderRules.PrinterName(User))")
              && api.Contains("TableBillHeader.Create(result.Preview, DateTime.UtcNow, BillHeaderRules.PrinterName(User))"),
            "S4-01 header: the print time and printer are taken from the current request");
        var view = Source("src", "RestaurantManagement.Web", "Views", "ProvisionalBill", "Preview.cshtml");
        var headerAt = view.IndexOf("data-preview-header", StringComparison.Ordinal);
        check(headerAt > 0 && headerAt < view.IndexOf("<table", StringComparison.Ordinal) && headerAt < view.IndexOf("data-preview-empty", StringComparison.Ordinal)
              && new[] { "data-preview-restaurant", "data-preview-tables", "data-preview-service-start", "data-preview-printed-at", "data-preview-printed-by", "data-preview-notice" }
                  .All(view.Contains),
            "S4-01 header UI: restaurant, table, service start, print time, printer and notice are shown above the item table");
    }
}
