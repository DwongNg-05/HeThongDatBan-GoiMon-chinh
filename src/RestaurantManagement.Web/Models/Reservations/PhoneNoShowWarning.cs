using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Security;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Models.Reservations;

/// <summary>
/// S3-09 Task 3: số lần khách không tới của một số điện thoại (đã chuẩn hoá) trong cửa sổ 90 ngày, dùng để cảnh báo
/// nhân viên khi tạo đặt bàn. Quy tắc đếm nằm trong database (dbo.fn_PhoneNoShowCount, migration 055).
/// </summary>
public sealed record PhoneNoShowSummary(string Phone, int NoShowCount, DateTime? LastNoShowAtUtc)
{
    public bool IsWarning => PhoneNoShowRules.IsWarning(NoShowCount);
    public string? Message => IsWarning ? PhoneNoShowRules.WarningMessage(NoShowCount, LastNoShowAtUtc) : null;
}

public static class PhoneNoShowRules
{
    /// <summary>Cảnh báo khi có ít nhất 3 lần không tới (cần PO chốt).</summary>
    public const int WarningThreshold = 3;

    /// <summary>Cửa sổ trượt 90 ngày tính ngược từ lúc kiểm tra, mốc biên được tính (cần PO chốt).</summary>
    public const int WindowDays = 90;

    public static bool IsWarning(int noShowCount) => noShowCount >= WarningThreshold;

    public static string WarningMessage(int noShowCount, DateTime? lastNoShowAtUtc)
    {
        // Dùng dấu gạch chéo literal để thông điệp nhất quán, không phụ thuộc DateSeparator
        // của máy chủ chạy ứng dụng (một số cấu hình vi-VN trả về dấu gạch ngang).
        var last = lastNoShowAtUtc is { } at ? $" (gần nhất ngày {VietnamTime.FromUtc(at):dd'/'MM'/'yyyy})" : "";
        return $"Số điện thoại này có {noShowCount} lần khách không tới trong {WindowDays} ngày gần nhất{last}. " +
               "Hãy trao đổi lại với khách trước khi tạo đặt bàn.";
    }

    /// <summary>
    /// Có phải hỏi lại nhân viên trước khi tạo đặt bàn không: số điện thoại đạt ngưỡng và nhân viên chưa xác nhận
    /// đúng số lần hiện tại. Nếu lịch sử tăng thêm sau khi nhân viên đã xác nhận (một lượt khác vừa bị đánh dấu không tới),
    /// <paramref name="acknowledgedCount"/> nhỏ hơn số hiện tại nên vẫn phải xác nhận lại.
    /// </summary>
    public static bool NeedsConfirmation(PhoneNoShowSummary summary, bool confirmed, int acknowledgedCount) =>
        summary.IsWarning && (!confirmed || acknowledgedCount < summary.NoShowCount);

    public const string ConfirmationRequired =
        "Số điện thoại này đã nhiều lần không tới. Hãy đánh dấu ô xác nhận đã trao đổi với khách rồi bấm tạo lại.";

    public const string ConfirmationLabel = "Tôi đã trao đổi với khách và vẫn tạo đặt bàn";
}

public sealed class PhoneNoShowStore(string connectionString)
{
    /// <summary>
    /// Đếm lần không tới của số điện thoại <paramref name="phone"/> (nhập theo bất kỳ cách tương đương nào, ví dụ
    /// "+84 901 234 567"). Trả null nếu không phải số điện thoại hợp lệ. <paramref name="utcNow"/> null = giờ máy chủ database.
    /// </summary>
    public async Task<PhoneNoShowSummary?> CountAsync(string? phone, CancellationToken cancellationToken, DateTime? utcNow = null)
    {
        var normalized = ReservationPhoneNormalizer.Normalize(phone);
        if (normalized is null) return null;
        await using var connection = new SqlConnection(connectionString);
        await using var command = new SqlCommand(
            "SELECT NoShowCount, LastNoShowAt FROM dbo.fn_PhoneNoShowCount(@phone, COALESCE(@now, SYSUTCDATETIME()));", connection);
        command.Parameters.Add("@phone", SqlDbType.VarChar, 10).Value = normalized;
        command.Parameters.Add("@now", SqlDbType.DateTime2).Value = utcNow is null ? DBNull.Value : utcNow.Value;
        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return new(normalized, 0, null);
        return new(normalized, reader.GetInt32(0),
            reader.IsDBNull(1) ? null : DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc));
    }
}

/// <summary>S3-09 Task 3: form tạo đặt bàn có cảnh báo số điện thoại nhiều lần không tới.</summary>
public interface IPhoneNoShowForm
{
    string Phone { get; set; }
    /// <summary>Nhân viên đã đánh dấu "Tôi đã trao đổi với khách và vẫn tạo đặt bàn".</summary>
    bool NoShowWarningConfirmed { get; set; }
    /// <summary>Số lần không tới mà nhân viên đã thấy khi xác nhận (lịch sử tăng sau đó thì phải xác nhận lại).</summary>
    int NoShowCountAcknowledged { get; set; }
    /// <summary>Cảnh báo hiện tại để form hiển thị lại (không nhận từ trình duyệt).</summary>
    PhoneNoShowSummary? PhoneWarning { get; set; }
}

/// <summary>
/// S3-09 Task 3: kiểm tra phía máy chủ khi gửi form tạo đặt bàn (Reservations/Create khi nhân viên đặt hộ,
/// TableReservations/Create của Quản lý). Dùng chung để hai form theo đúng một quy tắc.
/// </summary>
public static class PhoneNoShowGuard
{
    /// <summary>Đổi số điện thoại về dạng chuẩn nếu hợp lệ; bỏ lỗi định dạng của cách nhập tương đương.</summary>
    public static void NormalizePhone(ModelStateDictionary modelState, IPhoneNoShowForm form)
    {
        if (ReservationPhoneNormalizer.Normalize(form.Phone) is not { } normalized) return;
        form.Phone = normalized;
        modelState.Remove(nameof(IPhoneNoShowForm.Phone));
    }

    /// <summary>
    /// Chỉ khi Quản lý / Phục vụ tạo đặt bàn (khách tự đặt không đăng nhập: không tra cứu, không lộ lịch sử của số điện thoại).
    /// Đếm lại số lần không tới trong 90 ngày NGAY LÚC GỬI — lịch sử có thể vừa tăng trong lúc nhân viên điền form.
    /// Đạt ngưỡng mà nhân viên chưa xác nhận đúng số lần hiện tại: gắn cảnh báo + lỗi vào form và trả true (hiện lại form).
    /// Không đọc được lịch sử (database lỗi, thiếu migration 055) thì không chặn việc đặt bàn.
    /// </summary>
    public static async Task<bool> RequireConfirmationAsync(ClaimsPrincipal user, ModelStateDictionary modelState, IPhoneNoShowForm form,
        string connectionString, CancellationToken cancellationToken)
    {
        if (!user.IsInRole(AppRoles.Manager) && !user.IsInRole(AppRoles.Waiter)) return false;
        PhoneNoShowSummary? summary;
        try
        {
            summary = await new PhoneNoShowStore(connectionString).CountAsync(form.Phone, cancellationToken);
        }
        catch (SqlException)
        {
            return false;
        }
        form.PhoneWarning = summary is { IsWarning: true } ? summary : null;
        if (summary is null || !PhoneNoShowRules.NeedsConfirmation(summary, form.NoShowWarningConfirmed, form.NoShowCountAcknowledged))
            return false;
        // Hiện số lần mới nhất; bỏ giá trị cũ trong ModelState để form lấy giá trị mới và nhân viên phải đánh dấu lại.
        modelState.Remove(nameof(IPhoneNoShowForm.NoShowCountAcknowledged));
        modelState.Remove(nameof(IPhoneNoShowForm.NoShowWarningConfirmed));
        form.NoShowCountAcknowledged = summary.NoShowCount;
        form.NoShowWarningConfirmed = false;
        modelState.AddModelError(nameof(IPhoneNoShowForm.NoShowWarningConfirmed), PhoneNoShowRules.ConfirmationRequired);
        return true;
    }
}
