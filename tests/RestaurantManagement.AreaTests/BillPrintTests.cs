using RestaurantManagement.DbTool;

/// <summary>
/// S4-01 Task 3 (không cần database): in hoá đơn tạm tính khổ 80mm qua hộp thoại in của trình duyệt.
/// Kiểm tra mã nguồn: nút "In", bản in dựng từ cùng dữ liệu với màn hình, bố cục 80mm (cột cố định, xuống dòng, không ngắt trang),
/// ẩn điều hướng / nút khi in, JS chỉ gọi window.print(). Bố cục trên trình duyệt thật: tools/tests/bill-print-layout.check.cjs;
/// JS: tools/tests/bill-print.test.cjs; SQL Server + web thật: verify-bill-print (hoặc verify).
/// </summary>
internal static class BillPrintTests
{
    internal static void Run(Action<bool, string> check)
    {
        var root = DatabaseTool.Root;
        string Source(params string[] parts) => File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray())).Replace("\r\n", "\n");
        var preview = Source("src", "RestaurantManagement.Web", "Views", "ProvisionalBill", "Preview.cshtml");
        var receipt = Source("src", "RestaurantManagement.Web", "Views", "ProvisionalBill", "_PrintReceipt.cshtml");
        var line = Source("src", "RestaurantManagement.Web", "Views", "ProvisionalBill", "_PrintLine.cshtml");
        var css = Source("src", "RestaurantManagement.Web", "wwwroot", "css", "bill-print.css");
        var js = Source("src", "RestaurantManagement.Web", "wwwroot", "js", "bill-print.js");
        var layoutCheck = Source("tools", "tests", "bill-print-layout.check.cjs");

        // Nút "In" + bản in trên màn hình tạm tính.
        check(preview.Contains("<button type=\"button\" class=\"btn btn-primary btn-sm\" data-preview-print>In</button>")
              && preview.Contains("~/css/bill-print.css") && preview.Contains("~/js/bill-print.js")
              && preview.Contains("<partial name=\"_PrintReceipt\" model=\"printModel\" />") && preview.Contains("var printModel = Model with { Header = header };"),
            "S4-01 print: the preview has an \"In\" button and the 80mm print layout");

        // Bản in khớp màn hình: cùng model, cùng phần đầu (thời điểm in, người in), cùng các dòng, nhóm và tổng.
        check(new[] { "header.Notice", "header.RestaurantName", "header.Tables", "header.ServiceStartedAtText", "header.PrintedAtText", "header.PrintedBy",
                      "Model.Lines", "Model.ChargedCancelledLines", "BillRules.Format(Model.Total)", "BillRules.EmptyMessage",
                      "BillRules.ChargedCancellationLabel.ToUpperInvariant()", "data-print-total=\"@Model.Total.ToString(\"0\")\"" }.All(receipt.Contains)
              && new[] { "@Model.ItemName", "BillRules.Format(Model.UnitPrice)", "BillRules.Format(Model.Amount)", "@Model.Quantity", "Model.Requests.Summary",
                         "BillRules.ChargedCancellationLabel", "data-print-line=\"@Model.Id\"", "data-amount=\"@Model.Amount.ToString(\"0\")\"" }.All(line.Contains),
            "S4-01 print: the printout is built from the same data as the preview (header, lines, notes, charged group, total)");

        // Bố cục 80mm: cột SL / Thành tiền cố định, tên + ghi chú xuống dòng, số tiền không xuống dòng.
        check(css.Contains("width: 80mm;") && css.Contains("padding: 3mm 4mm 6mm;") && css.Contains(".bp-items { width: 100%; table-layout: fixed;")
              && css.Contains(".bp-col-qty { width: 8mm; }") && css.Contains(".bp-col-amount { width: 22mm; }")
              && css.Contains(".bp-name { padding-right: 1.5mm !important; overflow-wrap: anywhere;")
              && css.Contains(".bp-amount { text-align: right; white-space: nowrap;") && css.Contains(".bp-note { display: block;"),
            "S4-01 print: 80mm layout with fixed quantity / amount columns and wrapping dish names and notes");

        // Khi in: chỉ còn bản in (ẩn điều hướng, nút), không lề, không ngắt giữa một dòng; khổ trang theo nội dung (JS).
        check(css.Contains("@media print") && css.Contains("body > :not(.bill-print) { display: none !important; }") && css.Contains("@page { margin: 0; }")
              && css.Contains("break-inside: avoid;") && css.Contains(".bill-print { display: none; }")
              && js.Contains("doc.body.appendChild(receipt)") && js.Contains("`@page { size: 80mm ${pageHeightMm(px)}mm; margin: 0; }`")
              && js.Contains("root.addEventListener('beforeprint', fit);"),
            "S4-01 print: when printing only the receipt is shown, on one page as long as its content");

        // In chỉ là thao tác của trình duyệt: không gửi gì lên máy chủ → huỷ / in xong không đổi bàn, vẫn gọi thêm món được.
        check(js.Contains("root.print();") && !js.Contains("fetch(") && !js.Contains("XMLHttpRequest") && !js.Contains(".submit(")
              && !receipt.Contains("<form") && !line.Contains("<form") && !receipt.Contains("<button"),
            "S4-01 print: printing only opens the browser print dialog and sends nothing to the server");

        // Kiểm tra trên trình duyệt thật dùng đúng cấu trúc của hai partial.
        check(new[] { "data-print-receipt", "bp-notice", "bp-restaurant", "bp-meta", "bp-items", "bp-col-qty", "bp-col-amount", "data-print-group=\"items\"",
                      "data-print-group=\"charged-cancelled\"", "bp-group-head", "data-print-group-label", "bp-totals", "bp-total", "bp-foot" }
                  .All(token => receipt.Contains(token) && layoutCheck.Contains(token))
              && new[] { "bp-line", "data-print-line", "bp-name", "bp-dish", "bp-price", "bp-charged-label", "bp-note", "bp-qty", "bp-amount" }
                  .All(token => line.Contains(token) && layoutCheck.Contains(token)),
            "S4-01 print: the browser layout check uses the same structure as the print partials");
    }
}
