namespace RestaurantManagement.Web.Models.Orders;

/// <summary>
/// Yêu cầu riêng của khách trên MỘT dòng đơn (dbo.OrderItems): lựa chọn nhanh (cột QuickNotes, ví dụ "Ít cay, Không hành",
/// S3-03 Task 2) và ghi chú tự do (cột Notes, tối đa 200 ký tự, S3-03 Task 1).
/// Màn hình bếp (S3-03 Task 3) và hoá đơn tạm tính (S3-04 Task 4) cùng đọc hai cột đã lưu qua lớp này,
/// nên nội dung nhân viên phục vụ đối chiếu luôn khớp từng chữ với yêu cầu bếp đã nhận.
/// </summary>
public sealed record OrderItemRequests(IReadOnlyList<string> QuickLabels, string? FreeNote)
{
    public static readonly OrderItemRequests None = new(Array.Empty<string>(), null);

    /// <summary>Tách lựa chọn nhanh đã lưu thành từng nhãn; ghi chú tự do giữ nguyên đầy đủ (không cắt), chỉ bỏ khoảng trắng hai đầu.</summary>
    public static OrderItemRequests From(string? quickNotes, string? notes)
    {
        var labels = string.IsNullOrWhiteSpace(quickNotes)
            ? Array.Empty<string>()
            : quickNotes.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var note = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        return labels.Length == 0 && note is null ? None : new OrderItemRequests(labels, note);
    }

    /// <summary>Dòng có yêu cầu riêng (lựa chọn nhanh hoặc ghi chú).</summary>
    public bool HasAny => QuickLabels.Count > 0 || FreeNote is not null;

    /// <summary>Một dòng chữ để so khớp/hiển thị gọn, ví dụ "Ít cay · Không hành | Ghi chú: thêm chanh"; chuỗi rỗng khi không có yêu cầu.</summary>
    public string Summary => string.Join(" | ", new[]
    {
        QuickLabels.Count > 0 ? string.Join(" · ", QuickLabels) : null,
        FreeNote is null ? null : "Ghi chú: " + FreeNote
    }.Where(part => part is not null));
}
