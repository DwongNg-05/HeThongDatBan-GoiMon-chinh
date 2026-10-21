using RestaurantManagement.DbTool;
using RestaurantManagement.Web.Models.Orders;

/// <summary>
/// S4-01 Task 1 (không cần database): màn hình "Tạm tính" của bàn đang phục vụ — phép tính, nhóm món huỷ, định dạng VND,
/// quy tắc chỉ đọc, quyền và các nút trên giao diện. Phần chạy thật trên SQL Server + web: verify-bill-preview (hoặc verify).
/// </summary>
internal static class BillPreviewTests
{
    internal static void Run(Action<bool, string> check)
    {
        var t0 = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
        BillSourceItem Item(long id, string name, decimal price, int quantity, string status = "Pending", bool charged = false,
            string? notes = null, string? quick = null, int minute = 0) =>
            new(id, name, "phần", price, quantity, status, charged, notes, quick, t0.AddMinutes(minute));

        // 1. Nhiều món, số lượng khác nhau: thành tiền từng dòng và tổng cộng.
        var many = TableBillPreviewCalculator.Build("A01", "A01", 10, "Open", "Serving", new[]
        {
            Item(1, "Phở bò", 65_000m, 2), Item(2, "Trà đá", 5_000m, 7, "Served"), Item(3, "Lẩu thái", 299_000m, 1, "Preparing"),
            Item(4, "Nem rán", 45_000m, 3, "Ready")
        });
        check(many.Lines.Select(l => l.Amount).SequenceEqual(new[] { 130_000m, 35_000m, 299_000m, 135_000m }) && many.Total == 599_000m
              && many.ItemsSubtotal == 599_000m && many.ChargedCancelledLines.Count == 0 && !many.IsEmpty,
            "S4-01 preview: line amounts are unit price × quantity and the total adds them up");
        check(many.Lines.Select(l => l.Quantity).SequenceEqual(new[] { 2, 7, 1, 3 }) && many.Lines.Select(l => l.UnitPrice).SequenceEqual(new[] { 65_000m, 5_000m, 299_000m, 45_000m }),
            "S4-01 preview: each line keeps its own quantity and unit price");

        // 2. Ghi chú: lựa chọn nhanh + ghi chú tự do, đúng như bếp đã nhận; dòng không ghi chú thì không có.
        var noted = TableBillPreviewCalculator.Build("A01", "A01", 10, "Open", "Serving", new[]
        {
            Item(1, "Phở bò", 65_000m, 1, notes: "  thêm chanh, không giá  ", quick: "Ít cay, Không hành"), Item(2, "Phở bò", 65_000m, 1)
        });
        check(noted.Lines[0].Note == "Ít cay · Không hành | Ghi chú: thêm chanh, không giá" && noted.Lines[1].Note is null
              && noted.Lines.Count == 2,
            "S4-01 preview: a line with a note shows exactly that note; two orders of the same dish with different notes stay separate");
        check(TableBillPreviewResponse.From(noted).Lines[0].Note == noted.Lines[0].Note && TableBillPreviewResponse.From(noted).Lines[1].Note is null,
            "S4-01 preview API: the note is returned with its line");

        // 3–4. Huỷ trước khi chế biến: không xuất hiện. Huỷ sau khi chế biến: nhóm riêng có nhãn, tính theo quy tắc đã chốt.
        var mixed = TableBillPreviewCalculator.Build("A01", "A01", 10, "Open", "Serving", new[]
        {
            Item(1, "Phở bò", 65_000m, 2), Item(2, "Gỏi cuốn", 40_000m, 2, "Cancelled", charged: false),
            Item(3, "Cá kho", 150_000m, 1, "Cancelled", charged: true, notes: "khách vội"), Item(4, "Trà đá", 5_000m, 3, "Served")
        });
        check(mixed.Lines.All(l => l.Id != 2) && mixed.ChargedCancelledLines.All(l => l.Id != 2),
            "S4-01 preview: a dish cancelled before the kitchen started does not appear");
        check(mixed.Lines.Select(l => l.Id).SequenceEqual(new long[] { 1, 4 }) && mixed.ChargedCancelledLines.Select(l => l.Id).SequenceEqual(new long[] { 3 })
              && mixed.ChargedCancelledLines[0].StatusLabel == "Đã huỷ – có tính tiền" && BillRules.ChargedCancellationLabel == "Đã huỷ – có tính tiền"
              && mixed.ChargedCancelledLines[0].Note == "Ghi chú: khách vội",
            "S4-01 preview: a dish cancelled after cooking sits in its own group labelled \"Đã huỷ – có tính tiền\"");
        check(BillRules.ChargedCancellationCountsInTotal && mixed.ItemsSubtotal == 145_000m && mixed.ChargedCancelledSubtotal == 150_000m
              && mixed.Total == 295_000m,
            "S4-01 preview: the charged cancellation is added to the total (agreed rule)");
        var json = TableBillPreviewResponse.From(mixed);
        check(json.ChargedCancelled.Label == "Đã huỷ – có tính tiền" && json.ChargedCancelled.Lines.Count == 1 && json.ChargedCancelled.SubtotalText == "150.000 đ"
              && json.Lines.Count == 2 && json.Total == 295_000m && json.TotalText == "295.000 đ" && json.ChargedCancellationCountsInTotal
              && !json.IsEmpty && json.EmptyMessage is null && json.Lines[0].AmountText == "130.000 đ" && json.Lines[0].UnitPriceText == "65.000 đ",
            "S4-01 preview API: lines, the charged-cancelled group and the total are returned with VND text");

        // Cùng quy tắc với hoá đơn tạm tính S3-04/S3-05 và dbo.vw_SessionTotals: tổng cộng khớp con số ở các màn hình khác.
        var legacy = new ProvisionalBillViewModel(10, "A01", 2, "Open", t0, new[]
        {
            new ProvisionalBillLine(1, "A01", "Phở bò", "tô", 65_000m, 2, 130_000m, "Pending", false, t0, null, null),
            new ProvisionalBillLine(2, "A01", "Gỏi cuốn", "đĩa", 40_000m, 2, 80_000m, "Cancelled", false, t0, null, null),
            new ProvisionalBillLine(3, "A01", "Cá kho", "nồi", 150_000m, 1, 150_000m, "Cancelled", true, t0, null, null),
            new ProvisionalBillLine(4, "A01", "Trà đá", "ly", 5_000m, 3, 15_000m, "Served", false, t0, null, null)
        });
        check(legacy.Subtotal == mixed.Total, "S4-01 preview: the total matches the existing provisional bill and session totals");

        // 5. Bàn chưa có món / mọi món đã huỷ trước khi chế biến: thông báo trống.
        var empty = TableBillPreviewCalculator.Build("B02", "", 11, "Open", "Serving", Array.Empty<BillSourceItem>());
        var allFree = TableBillPreviewCalculator.Build("B02", "B02", 11, "Open", "Serving", new[] { Item(1, "Phở bò", 65_000m, 1, "Cancelled") });
        check(empty.IsEmpty && empty.Total == 0m && empty.Tables == "B02" && allFree.IsEmpty && BillRules.EmptyMessage == "Bàn chưa có món nào."
              && TableBillPreviewResponse.From(empty).EmptyMessage == "Bàn chưa có món nào." && TableBillPreviewResponse.From(empty).TotalText == "0 đ",
            "S4-01 preview: a table with no dishes shows the empty message and a zero total");

        // 6. Định dạng VND và làm tròn.
        check(BillRules.Format(0m) == "0 đ" && BillRules.Format(999m) == "999 đ" && BillRules.Format(1_000m) == "1.000 đ"
              && BillRules.Format(1_250_000m) == "1.250.000 đ" && BillRules.Format(1_234_567_890m) == "1.234.567.890 đ"
              && BillRules.Format(99_999_999_999m) == "99.999.999.999 đ",
            "S4-01 VND: thousands separated by dots, followed by \"đ\" (large amounts included)");
        check(BillRules.Round(12_345.5m) == 12_346m && BillRules.Round(12_345.49m) == 12_345m && BillRules.Format(12_345.5m) == "12.346 đ"
              && BillRules.LineAmount(33_333.5m, 3) == 100_001m && BillRules.LineAmount(65_000m, 99) == 6_435_000m,
            "S4-01 VND: amounts are rounded to the whole đồng, half up");
        var big = TableBillPreviewCalculator.Build("VIP1", "VIP1", 12, "Open", "Serving", new[]
        {
            Item(1, "Bào ngư", 9_990_000m, 99), Item(2, "Rượu vang", 25_000_000m, 12)
        });
        check(big.Total == 1_289_010_000m && BillRules.Format(big.Total) == "1.289.010.000 đ" && TableBillPreviewResponse.From(big).Lines[0].AmountText == "989.010.000 đ",
            "S4-01 VND: a large bill is totalled and formatted correctly");

        // Thứ tự: theo lúc gọi món, cùng lúc thì theo mã dòng (món gọi thêm nằm cuối).
        var order = TableBillPreviewCalculator.Build("A01", "A01", 10, "Open", "Serving", new[]
        {
            Item(9, "Gọi thêm", 10_000m, 1, minute: 30), Item(5, "Đợt 1 b", 10_000m, 1), Item(4, "Đợt 1 a", 10_000m, 1)
        });
        check(order.Lines.Select(l => l.Id).SequenceEqual(new long[] { 4, 5, 9 }), "S4-01 preview: lines are listed in ordering order; dishes added later come last");
        check(TableBillPreviewResult.NotServing("C03").Message.Contains("không có khách") && TableBillPreviewResult.UnknownTable("ZZ").Outcome == TableBillPreviewOutcome.UnknownTable,
            "S4-01 preview: a table without a serving session has no preview and says why");

        // 7. Chỉ đọc: truy vấn không ghi gì; giao dịch luôn rollback.
        var root = DatabaseTool.Root;
        string Source(params string[] parts) => File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
        var store = Source("src", "RestaurantManagement.Web", "Services", "Orders", "TableBillPreviewStore.cs");
        var sql = (RestaurantManagement.Web.Services.TableBillPreviewStore.SessionQuery + RestaurantManagement.Web.Services.TableBillPreviewStore.ItemsQuery).ToUpperInvariant();
        check(new[] { "INSERT", "UPDATE", "DELETE", "MERGE", "EXEC", "TRUNCATE", "INTO ", "SP_" }.All(word => !sql.Contains(word)),
            "S4-01 read-only: the preview queries only SELECT (no insert, update, delete, merge or procedure call)");
        check(store.Contains("await transaction.RollbackAsync(") && store.Contains("ApplicationIntent = ApplicationIntent.ReadOnly")
              && !store.Contains("CommitAsync") && !store.Contains("ExecuteNonQuery"),
            "S4-01 read-only: the preview runs in a transaction that is always rolled back");

        // Quyền: Quản lý, Phục vụ; Bếp, Thu ngân, khách không.
        foreach (var key in new[] { "ProvisionalBill.Preview GET", "ProvisionalBillApi.Get GET" })
        {
            var api = ApiAccessMatrix.All.Single(e => e.Key == key);
            check(ApiAccessMatrix.IsAllowed(api, "Manager") && ApiAccessMatrix.IsAllowed(api, "Waiter")
                  && !ApiAccessMatrix.IsAllowed(api, "Kitchen") && !ApiAccessMatrix.IsAllowed(api, "Cashier") && !ApiAccessMatrix.IsAllowed(api, null),
                $"S4-01 permissions: {key} is for the manager and the waiter only");
        }

        // Giao diện: lối vào "Xem / in tạm tính" trên chi tiết bàn, nhóm có nhãn,
        // dòng tổng cộng, nút "Quay lại gọi món". Cả bàn dữ liệu demo đang phục vụ
        // cũng phải xem/in được; đây chỉ là thao tác đọc và không đổi trạng thái bàn.
        var map = Source("src", "RestaurantManagement.Web", "Views", "Home", "Index.cshtml");
        var mapJs = Source("src", "RestaurantManagement.Web", "wwwroot", "js", "table-map.js");
        check(map.Contains("id=\"details-preview-link\"") && map.Contains("Xem / in tạm tính</a>") && map.Contains("~/ProvisionalBill/Preview/")
              && mapJs.Contains("const hasPreview = serving && details.hasActiveSession;"),
            "S4-01 UI: the table details show the provisional-bill action for every serving table with an active session");
        var view = Source("src", "RestaurantManagement.Web", "Views", "ProvisionalBill", "Preview.cshtml");
        check(view.Contains("data-preview-back>← Quay lại gọi món</a>") && view.Contains("\"?table=\"") && mapJs.Contains("get('table')")
              && view.Contains("data-preview-group=\"charged-cancelled\"") && view.Contains("@BillRules.ChargedCancellationLabel")
              && view.Contains(">Tổng cộng</th>") && view.Contains("@BillRules.EmptyMessage") && !view.Contains("<form"),
            "S4-01 UI: the preview has the item table, the labelled group, the total, the empty message and a way back to the table details");
        var line = Source("src", "RestaurantManagement.Web", "Views", "ProvisionalBill", "_PreviewLine.cshtml");
        check(line.Contains("@BillRules.Format(line.UnitPrice)") && line.Contains("@BillRules.Format(line.Amount)") && !line.Contains("Vnd.Format")
              && !view.Contains("Vnd.Format"),
            "S4-01 UI: every amount on the preview uses the agreed VND format");
    }
}
