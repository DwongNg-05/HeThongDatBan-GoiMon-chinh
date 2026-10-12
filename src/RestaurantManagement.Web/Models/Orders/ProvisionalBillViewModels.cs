namespace RestaurantManagement.Web.Models.Orders;

/// <summary>
/// S3-04 Task 4: một dòng trên hoá đơn tạm tính = đúng một dòng dbo.OrderItems (một dòng bếp).
/// Hai dòng cùng món nhưng khác yêu cầu KHÔNG được gộp; mỗi dòng mang nguyên lựa chọn nhanh + ghi chú đã gửi bếp.
/// </summary>
public sealed record ProvisionalBillLine(long Id, string TableCode, string ItemName, string Unit, decimal UnitPrice, int Quantity,
    decimal LineTotal, string Status, bool ChargeWhenCancelled, DateTime SubmittedAt, string? Notes, string? QuickNotes)
{
    /// <summary>Đọc giống hệt màn hình bếp (KitchenOrderItem.Requests).</summary>
    public OrderItemRequests Requests => OrderItemRequests.From(QuickNotes, Notes);

    /// <summary>Có tính tiền: món chưa huỷ, hoặc huỷ sau khi bếp đã làm (giống dbo.vw_SessionTotals).</summary>
    public bool IsCharged => Status != "Cancelled" || ChargeWhenCancelled;

    public bool IsCancelled => Status == "Cancelled";

    public string StatusLabel => Status switch
    {
        "Pending" => "Chờ bếp nhận",
        "Preparing" => "Đang nấu",
        "Ready" => "Xong, chờ mang ra",
        "Served" => "Đã mang ra",
        "Cancelled" => ChargeWhenCancelled ? "Đã huỷ (vẫn tính tiền)" : "Đã huỷ",
        _ => Status
    };
}

/// <summary>
/// Hoá đơn tạm tính của một phiên thanh toán (phiên gốc + các phiên gộp vào, BillingSessionId).
/// Đọc lại từ database mỗi lần mở, nên mở lại bao nhiêu lần cũng ra cùng nội dung ghi chú đã lưu.
/// </summary>
public sealed record ProvisionalBillViewModel(long SessionId, string Tables, int GuestCount, string Status, DateTime OpenedAt,
    IReadOnlyList<ProvisionalBillLine> Lines)
{
    public decimal Subtotal => Lines.Where(l => l.IsCharged).Sum(l => l.LineTotal);
    public int ChargedLineCount => Lines.Count(l => l.IsCharged);
    public int RequestLineCount => Lines.Count(l => l.Requests.HasAny);
    public bool HasSeveralTables => Lines.Select(l => l.TableCode).Distinct().Count() > 1;

    public string StatusLabel => Status switch
    {
        "Open" => "Đang phục vụ",
        "AwaitingPayment" => "Đã yêu cầu thanh toán",
        "Closed" => "Đã thanh toán",
        _ => Status
    };
}

/// <summary>Không tìm thấy hoá đơn tạm tính (mã phiên sai, hoặc bàn không có khách).</summary>
public sealed record ProvisionalBillMissingViewModel(string Message);
