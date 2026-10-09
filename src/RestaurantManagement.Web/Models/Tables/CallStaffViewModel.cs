namespace RestaurantManagement.Web.Models.Tables;

/// <summary>
/// S3-01 Task 3: hướng dẫn gọi phục vụ khi khách không mở được phiên gọi món (mã QR đã thay, bàn đang dọn, ...).
/// Hiển thị mã bàn (nếu biết) để khách đọc cho nhân viên và nút gọi điện cho nhà hàng (nếu đã cấu hình số).
/// </summary>
public sealed record CallStaffViewModel(string? TableCode, string? RestaurantPhone)
{
    public const string ViewDataKey = "CallStaff";

    /// <summary>Đường dẫn gọi điện: chỉ giữ chữ số (RestaurantSettings.Phone là 10 chữ số).</summary>
    public string? PhoneHref => string.IsNullOrWhiteSpace(RestaurantPhone) || !RestaurantPhone.All(char.IsAsciiDigit)
        ? null
        : "tel:" + RestaurantPhone;
}
