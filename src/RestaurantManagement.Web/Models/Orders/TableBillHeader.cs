using System.Globalization;
using System.Security.Claims;
using RestaurantManagement.Web.Models.Reservations;

namespace RestaurantManagement.Web.Models.Orders;

/// <summary>"Thời điểm bắt đầu phục vụ" tính từ đâu (PO chốt).</summary>
public enum ServiceStartDefinition
{
    /// <summary>Lúc mở bàn: phiên phục vụ được mở (sớm nhất trong nhóm bàn đã gộp).</summary>
    TableOpened,

    /// <summary>Lúc gọi món đầu tiên của phiên (kể cả món sau đó bị huỷ).</summary>
    FirstOrder
}

/// <summary>
/// S4-01 Task 2: quy tắc phần đầu hoá đơn tạm tính (docs/S4-01-Task2.md). Các điểm cần PO chốt được gom về đây:
/// - Thời điểm bắt đầu phục vụ = lúc MỞ BÀN (đề xuất), đổi sang lúc gọi món đầu tiên bằng <see cref="ServiceStartsAt"/>.
/// - Tên quán lấy từ cấu hình chung dbo.RestaurantSettings.Name (hệ thống một quán, chưa có chi nhánh).
/// - Có dòng chữ "TẠM TÍNH – KHÔNG PHẢI HOÁ ĐƠN THANH TOÁN" (đề xuất: có), tắt bằng <see cref="ShowProvisionalNotice"/>.
/// - Ngày giờ theo giờ Việt Nam, dạng ngày/tháng/năm giờ:phút (15/10/2026 19:05), không phụ thuộc văn hoá của máy chủ.
/// </summary>
public static class BillHeaderRules
{
    public static readonly ServiceStartDefinition ServiceStartsAt = ServiceStartDefinition.TableOpened;

    public static readonly bool ShowProvisionalNotice = true;

    public const string ProvisionalNotice = "TẠM TÍNH – KHÔNG PHẢI HOÁ ĐƠN THANH TOÁN";

    /// <summary>Dùng khi cấu hình chưa có tên quán.</summary>
    public const string FallbackRestaurantName = "Nhà hàng";

    public const string DateTimePattern = "dd/MM/yyyy HH:mm";

    /// <summary>Giờ Việt Nam (đã đổi) → "15/10/2026 19:05".</summary>
    public static string FormatDateTime(DateTime vietnamTime) =>
        vietnamTime.ToString("dd'/'MM'/'yyyy HH':'mm", CultureInfo.InvariantCulture);

    /// <summary>Thời điểm bắt đầu phục vụ (UTC) theo định nghĩa đã chốt; null khi tính theo món đầu mà bàn chưa gọi món.</summary>
    public static DateTime? ServiceStartUtc(TableBillPreview preview) => ServiceStartsAt == ServiceStartDefinition.FirstOrder
        ? preview.FirstOrderAtUtc
        : preview.ServiceOpenedAtUtc;

    /// <summary>Tên nhân viên đang đăng nhập: họ tên (claim FullName lúc đăng nhập), không có thì tên đăng nhập.</summary>
    public static string PrinterName(ClaimsPrincipal user)
    {
        var fullName = user.FindFirstValue("FullName");
        if (!string.IsNullOrWhiteSpace(fullName)) return fullName.Trim();
        return string.IsNullOrWhiteSpace(user.Identity?.Name) ? "Không rõ" : user.Identity!.Name!.Trim();
    }

    public static string RestaurantNameOrDefault(string? configured) =>
        string.IsNullOrWhiteSpace(configured) ? FallbackRestaurantName : configured.Trim();
}

/// <summary>
/// Phần đầu hoá đơn tạm tính: tên quán, mã bàn hiện tại, thời điểm bắt đầu phục vụ, thời điểm in và người in.
/// Thời điểm in và người in được ghi nhận ở MỖI lần mở tạm tính (yêu cầu hiện tại), không lưu vào database (tạm tính chỉ đọc).
/// </summary>
public sealed record TableBillHeader(string RestaurantName, string Tables, DateTime? ServiceStartedAtUtc, DateTime PrintedAtUtc,
    string PrintedBy, string? Notice)
{
    public DateTime? ServiceStartedAt => ServiceStartedAtUtc is null ? null : VietnamTime.FromUtc(ServiceStartedAtUtc.Value);
    public DateTime PrintedAt => VietnamTime.FromUtc(PrintedAtUtc);

    public string ServiceStartedAtText => ServiceStartedAt is null ? "Chưa gọi món" : BillHeaderRules.FormatDateTime(ServiceStartedAt.Value);
    public string PrintedAtText => BillHeaderRules.FormatDateTime(PrintedAt);

    public string ServiceStartLabel => BillHeaderRules.ServiceStartsAt == ServiceStartDefinition.FirstOrder
        ? "Bắt đầu phục vụ (món đầu tiên)"
        : "Bắt đầu phục vụ";

    public static TableBillHeader Create(TableBillPreview preview, DateTime printedAtUtc, string printedBy) => new(
        BillHeaderRules.RestaurantNameOrDefault(preview.RestaurantName), preview.Tables, BillHeaderRules.ServiceStartUtc(preview),
        DateTime.SpecifyKind(printedAtUtc, DateTimeKind.Utc), printedBy,
        BillHeaderRules.ShowProvisionalNotice ? BillHeaderRules.ProvisionalNotice : null);
}

/// <summary>Phần đầu trong JSON của GET /api/tables/{code}/provisional-bill.</summary>
public sealed record TableBillHeaderResponse(string RestaurantName, string Tables, DateTime? ServiceStartedAtUtc, string ServiceStartedAtText,
    string ServiceStartDefinition, DateTime PrintedAtUtc, string PrintedAtText, string PrintedBy, string? Notice)
{
    public static TableBillHeaderResponse From(TableBillHeader header) => new(header.RestaurantName, header.Tables, header.ServiceStartedAtUtc,
        header.ServiceStartedAtText, BillHeaderRules.ServiceStartsAt.ToString(), header.PrintedAtUtc, header.PrintedAtText, header.PrintedBy,
        header.Notice);
}
