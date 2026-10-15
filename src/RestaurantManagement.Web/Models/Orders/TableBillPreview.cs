using System.Globalization;

namespace RestaurantManagement.Web.Models.Orders;

/// <summary>
/// S4-01 Task 1: quy tắc tính và hiển thị tiền trên màn hình "Tạm tính" của một bàn đang phục vụ (docs/S4-01-Task1.md).
/// Hai điểm cần PO chốt được gom về đây, đổi một chỗ là đổi cả API, màn hình và kiểm thử:
/// - Món huỷ SAU khi bếp đã chế biến (ChargeWhenCancelled = 1) CÓ cộng vào tổng cộng — cùng quy tắc với dbo.vw_SessionTotals,
///   hoá đơn khi thanh toán (S3-05 Task 3) và "Tạm tính hiện tại" trên chi tiết bàn, nên mọi nơi ra cùng một con số.
/// - Tiền VND là số nguyên đồng: thành tiền = đơn giá × số lượng, làm tròn tới 1 đồng (nửa đồng làm tròn lên, AwayFromZero);
///   tổng cộng = tổng các thành tiền đã làm tròn. Không làm tròn tới nghìn đồng.
/// - Hiển thị: dấu chấm phân cách hàng nghìn, ký hiệu " đ" phía sau, ví dụ 1.250.000 đ (không phụ thuộc cấu hình văn hoá của máy chủ).
/// </summary>
public static class BillRules
{
    /// <summary>Món huỷ sau khi bếp chế biến có được cộng vào tổng cộng (đề xuất mặc định: có).</summary>
    public static readonly bool ChargedCancellationCountsInTotal = true;

    /// <summary>Nhãn của nhóm món huỷ sau khi bếp chế biến.</summary>
    public const string ChargedCancellationLabel = "Đã huỷ – có tính tiền";

    /// <summary>Ký hiệu tiền sau số (đề xuất mặc định: "đ").</summary>
    public const string CurrencySymbol = "đ";

    public const string EmptyMessage = "Bàn chưa có món nào.";

    private static readonly NumberFormatInfo Grouping = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = ",", NegativeSign = "-" };

    /// <summary>Làm tròn tới 1 đồng, nửa đồng làm tròn lên.</summary>
    public static decimal Round(decimal amount) => Math.Round(amount, 0, MidpointRounding.AwayFromZero);

    /// <summary>Thành tiền một dòng = đơn giá × số lượng, làm tròn tới đồng.</summary>
    public static decimal LineAmount(decimal unitPrice, int quantity) => Round(unitPrice * quantity);

    /// <summary>1250000 → "1.250.000 đ"; 0 → "0 đ".</summary>
    public static string Format(decimal amount) => Round(amount).ToString("#,0", Grouping) + " " + CurrencySymbol;
}

/// <summary>Một dòng dbo.OrderItems như đọc từ database (đầu vào của phép tính, không có logic).</summary>
public sealed record BillSourceItem(long Id, string ItemName, string Unit, decimal UnitPrice, int Quantity, string Status,
    bool ChargeWhenCancelled, string? Notes, string? QuickNotes, DateTime SubmittedAt);

/// <summary>Một dòng trên màn hình tạm tính. Mỗi dòng đơn là một dòng (không gộp hai lần gọi cùng món, vì ghi chú có thể khác).</summary>
public sealed record TableBillPreviewLine(long Id, string ItemName, string Unit, int Quantity, decimal UnitPrice, decimal Amount,
    string Status, OrderItemRequests Requests)
{
    /// <summary>Ghi chú gộp một dòng (lựa chọn nhanh + ghi chú tự do), null khi không có.</summary>
    public string? Note => Requests.HasAny ? Requests.Summary : null;

    public string StatusLabel => Status switch
    {
        "Pending" => "Chờ bếp nhận",
        "Preparing" => "Đang nấu",
        "Ready" => "Xong, chờ mang ra",
        "Served" => "Đã mang ra",
        "Cancelled" => BillRules.ChargedCancellationLabel,
        _ => Status
    };
}

/// <summary>Một hàng trên màn hình (số thứ tự liên tục qua cả hai nhóm + dòng món).</summary>
public sealed record TableBillPreviewRow(int Position, TableBillPreviewLine Line);

/// <summary>Dữ liệu tạm tính của một bàn đang phục vụ (phiên thanh toán của bàn, kể cả bàn đã gộp vào).</summary>
public sealed record TableBillPreview(string TableCode, string Tables, long SessionId, string SessionStatus, string TableStatus,
    IReadOnlyList<TableBillPreviewLine> Lines, IReadOnlyList<TableBillPreviewLine> ChargedCancelledLines)
{
    /// <summary>Tổng các món đang phục vụ (chưa huỷ).</summary>
    public decimal ItemsSubtotal => Lines.Sum(l => l.Amount);

    /// <summary>Tổng các món huỷ sau khi bếp chế biến.</summary>
    public decimal ChargedCancelledSubtotal => ChargedCancelledLines.Sum(l => l.Amount);

    /// <summary>Tổng cộng theo quy tắc đã chốt (BillRules.ChargedCancellationCountsInTotal).</summary>
    public decimal Total => ItemsSubtotal + (BillRules.ChargedCancellationCountsInTotal ? ChargedCancelledSubtotal : 0m);

    /// <summary>Không có dòng nào để hiển thị (bàn chưa gọi món, hoặc mọi món đã huỷ trước khi bếp chế biến).</summary>
    public bool IsEmpty => Lines.Count == 0 && ChargedCancelledLines.Count == 0;

    // ---- S4-01 Task 2: dữ liệu cho phần đầu hoá đơn (TableBillHeader) ----

    /// <summary>Tên quán theo cấu hình chung (dbo.RestaurantSettings.Name).</summary>
    public string? RestaurantName { get; init; }

    /// <summary>Lúc mở bàn (UTC): phiên mở sớm nhất trong nhóm bàn cùng hoá đơn.</summary>
    public DateTime? ServiceOpenedAtUtc { get; init; }

    /// <summary>Lúc gọi món đầu tiên (UTC), kể cả món sau đó bị huỷ; null khi chưa gọi món.</summary>
    public DateTime? FirstOrderAtUtc { get; init; }

    /// <summary>Phần đầu hoá đơn của lần mở này (thời điểm in, người in) — do controller gắn theo yêu cầu hiện tại.</summary>
    public TableBillHeader? Header { get; init; }

    public string SessionStatusLabel => SessionStatus switch
    {
        "Open" => "Đang phục vụ",
        "AwaitingPayment" => "Đã yêu cầu thanh toán",
        _ => SessionStatus
    };
}

/// <summary>Phép tính tạm tính (thuần, không đọc database — kiểm thử trực tiếp được).</summary>
public static class TableBillPreviewCalculator
{
    public static TableBillPreview Build(string tableCode, string tables, long sessionId, string sessionStatus, string tableStatus,
        IEnumerable<BillSourceItem> items)
    {
        var ordered = items.OrderBy(i => i.SubmittedAt).ThenBy(i => i.Id).ToArray();
        // Huỷ trước khi bếp chế biến (ChargeWhenCancelled = 0): loại hẳn khỏi tạm tính.
        var active = ordered.Where(i => i.Status != "Cancelled").Select(ToLine).ToArray();
        // Huỷ sau khi bếp chế biến: nhóm riêng có nhãn "Đã huỷ – có tính tiền".
        var chargedCancelled = ordered.Where(i => i.Status == "Cancelled" && i.ChargeWhenCancelled).Select(ToLine).ToArray();
        return new TableBillPreview(tableCode, string.IsNullOrWhiteSpace(tables) ? tableCode : tables, sessionId, sessionStatus, tableStatus,
            active, chargedCancelled)
        {
            FirstOrderAtUtc = ordered.Length == 0 ? null : DateTime.SpecifyKind(ordered[0].SubmittedAt, DateTimeKind.Utc)
        };
    }

    private static TableBillPreviewLine ToLine(BillSourceItem item) => new(item.Id, item.ItemName, item.Unit, item.Quantity,
        BillRules.Round(item.UnitPrice), BillRules.LineAmount(item.UnitPrice, item.Quantity), item.Status,
        OrderItemRequests.From(item.QuickNotes, item.Notes));
}

/// <summary>Kết quả tra tạm tính theo mã bàn.</summary>
public sealed record TableBillPreviewResult(TableBillPreviewOutcome Outcome, TableBillPreview? Preview, string Message)
{
    public static TableBillPreviewResult Found(TableBillPreview preview) => new(TableBillPreviewOutcome.Found, preview, "");
    public static TableBillPreviewResult UnknownTable(string code) =>
        new(TableBillPreviewOutcome.UnknownTable, null, $"Không tìm thấy bàn {code}.");
    public static TableBillPreviewResult NotServing(string code) =>
        new(TableBillPreviewOutcome.NotServing, null, $"Bàn {code} hiện không có khách đang phục vụ, chưa có tạm tính.");
}

public enum TableBillPreviewOutcome { Found, UnknownTable, NotServing }

/// <summary>JSON trả về của GET /api/tables/{code}/provisional-bill (tiền là số nguyên đồng, kèm chuỗi đã định dạng).</summary>
public sealed record TableBillPreviewResponse(string TableCode, string Tables, long SessionId, string SessionStatus, string TableStatus,
    IReadOnlyList<TableBillPreviewLineResponse> Lines, TableBillPreviewGroupResponse ChargedCancelled, decimal Total, string TotalText,
    bool IsEmpty, string? EmptyMessage, bool ChargedCancellationCountsInTotal, TableBillHeaderResponse? Header = null)
{
    public static TableBillPreviewResponse From(TableBillPreview preview) => new(
        preview.TableCode, preview.Tables, preview.SessionId, preview.SessionStatus, preview.TableStatus,
        preview.Lines.Select(TableBillPreviewLineResponse.From).ToArray(),
        new TableBillPreviewGroupResponse(BillRules.ChargedCancellationLabel,
            preview.ChargedCancelledLines.Select(TableBillPreviewLineResponse.From).ToArray(),
            preview.ChargedCancelledSubtotal, BillRules.Format(preview.ChargedCancelledSubtotal)),
        preview.Total, BillRules.Format(preview.Total), preview.IsEmpty, preview.IsEmpty ? BillRules.EmptyMessage : null,
        BillRules.ChargedCancellationCountsInTotal, preview.Header is null ? null : TableBillHeaderResponse.From(preview.Header));
}

public sealed record TableBillPreviewLineResponse(long Id, string ItemName, string Unit, int Quantity, decimal UnitPrice, string UnitPriceText,
    decimal Amount, string AmountText, string? Note, string Status, string StatusLabel)
{
    public static TableBillPreviewLineResponse From(TableBillPreviewLine line) => new(line.Id, line.ItemName, line.Unit, line.Quantity,
        line.UnitPrice, BillRules.Format(line.UnitPrice), line.Amount, BillRules.Format(line.Amount), line.Note, line.Status, line.StatusLabel);
}

public sealed record TableBillPreviewGroupResponse(string Label, IReadOnlyList<TableBillPreviewLineResponse> Lines, decimal Subtotal, string SubtotalText);
