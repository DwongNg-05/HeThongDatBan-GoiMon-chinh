using System.Data;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models.Cashier;
using RestaurantManagement.Web.Models.Reservations;

namespace RestaurantManagement.Web.Models.Orders;

/// <summary>S3-05 Task 1: một lý do huỷ món (mã lưu trong database + nhãn hiển thị).</summary>
public sealed record OrderCancelReason(string Code, string Label);

/// <summary>
/// S3-05 Task 1: quy tắc huỷ dòng món trên hoá đơn tạm tính (docs/S3-05-Task1.md).
/// Huỷ nguyên dòng; Phục vụ chỉ huỷ món còn chờ bếp với ba lý do; huỷ trước khi bếp làm không tính tiền.
/// Database (dbo.usp_CancelOrderItem, migration 056) kiểm tra lại toàn bộ khi xác nhận.
/// </summary>
public static class OrderCancelRules
{
    public const string ChangedMind = "ChangedMind";
    public const string Mistake = "Mistake";
    public const string SoldOut = "SoldOut";
    public const string ManagerOverride = "ManagerOverride";

    /// <summary>Ba lý do Phục vụ được chọn; Quản lý có thêm "Quản lý yêu cầu".</summary>
    public static readonly IReadOnlyList<OrderCancelReason> StaffReasons = new[]
    {
        new OrderCancelReason(ChangedMind, "Khách đổi ý"),
        new OrderCancelReason(Mistake, "Gọi nhầm món"),
        new OrderCancelReason(SoldOut, "Hết nguyên liệu")
    };

    private static readonly OrderCancelReason ManagerReason = new(ManagerOverride, "Quản lý yêu cầu");

    public static IReadOnlyList<OrderCancelReason> ReasonsFor(bool isManager) =>
        isManager ? StaffReasons.Append(ManagerReason).ToArray() : StaffReasons;

    public static string? Label(string? code) => code is null
        ? null
        : StaffReasons.Append(ManagerReason).FirstOrDefault(reason => reason.Code == code)?.Label ?? code;

    public static bool IsAllowed(string? reason, bool isManager) =>
        reason is not null && ReasonsFor(isManager).Any(option => option.Code == reason);

    /// <summary>Trạng thái của một dòng món chưa huỷ (giá trị expectedStatus mà hộp xác nhận gửi lên).</summary>
    public static bool IsKnownStatus(string? status) => status is "Pending" or "Preparing" or "Ready" or "Served";

    /// <summary>Phục vụ chỉ huỷ được món còn chờ bếp.</summary>
    public static bool WaiterCanCancel(string status) => status == "Pending";

    /// <summary>
    /// S3-05 Task 3: Quản lý huỷ được món còn chờ bếp (không tính tiền) và món đang nấu / đã xong chờ mang ra (có tính tiền).
    /// Món đã mang ra cho khách không huỷ được nữa (database: 51143).
    /// </summary>
    public static bool ManagerCanCancel(string status) => status is "Pending" or "Preparing" or "Ready";

    /// <summary>S3-05 Task 3: huỷ ở trạng thái này là huỷ có tính tiền (bếp đã bắt đầu, món chưa mang ra).</summary>
    public static bool IsChargedStatus(string? status) => status is "Preparing" or "Ready";

    /// <summary>S3-05 Task 3: huỷ có tính tiền chỉ dùng ba lý do (không có "Quản lý yêu cầu").</summary>
    public static IReadOnlyList<OrderCancelReason> ChargedReasons => StaffReasons;

    public static bool IsAllowedForCharged(string? reason) => reason is not null && ChargedReasons.Any(option => option.Code == reason);

    /// <summary>Nhãn của dòng món đã huỷ có tính tiền trên hoá đơn, màn hình gọi món và báo cáo cuối ca.</summary>
    public const string ChargedLabel = "Đã huỷ có tính tiền";

    /// <summary>Lý do nút huỷ của Quản lý bị vô hiệu; null khi Quản lý vẫn huỷ được.</summary>
    public static string? ManagerLockedReason(string status) => status == "Served"
        ? "Món đã mang ra cho khách — không hủy được nữa; nếu cần, hãy giảm giá khi thanh toán."
        : null;

    /// <summary>Lý do nút huỷ của Phục vụ bị vô hiệu; null khi Phục vụ vẫn huỷ được.</summary>
    public static string? LockedReason(string status) => status switch
    {
        "Preparing" => "Bếp đang chế biến món này — chỉ Quản lý được hủy và món vẫn tính tiền.",
        "Ready" => "Bếp đã làm xong món này — chỉ Quản lý được hủy và món vẫn tính tiền.",
        "Served" => "Món đã được mang ra cho khách — không hủy được nữa.",
        _ => null
    };

    public const string MissingReason = "Hãy chọn lý do hủy món: khách đổi ý, gọi nhầm hoặc hết nguyên liệu.";
    public const string ManagerOnlyReason = "Lý do \"Quản lý yêu cầu\" chỉ dành cho Quản lý.";
    public const string ChargedReasonRequired = "Huỷ có tính tiền cần một trong ba lý do: khách đổi ý, gọi nhầm hoặc hết nguyên liệu.";
    public const string ChargedConfirmRequired = "Bếp đã bắt đầu món này: hãy đánh dấu xác nhận huỷ có tính tiền trước khi huỷ.";
    public const string InvalidRequest = "Yêu cầu hủy món không hợp lệ. Vui lòng tải lại hoá đơn và thử lại.";

    /// <summary>Lỗi do dbo.usp_CancelOrderItem trả về → thông báo cho nhân viên.</summary>
    public static string RejectionMessage(int number, string databaseMessage) => number switch
    {
        51000 => "Hệ thống đang bận. Vui lòng bấm lại sau ít giây.",
        51001 => "Tài khoản không có quyền hủy món.",
        51032 => MissingReason,
        51036 => ManagerOnlyReason,
        51142 => ChargedReasonRequired,
        51144 => ChargedConfirmRequired,
        // 51031 (không còn huỷ được), 51033 (không thuộc hoá đơn), 51034 (bếp vừa đổi trạng thái), 51035 (bếp đã bắt đầu):
        // thông báo tiếng Việt viết sẵn trong database.
        _ => databaseMessage
    };
}

/// <summary>
/// S3-05 Task 1: kết quả huỷ một dòng món. AlreadyCancelled: lần gửi lặp hoặc người khác đã huỷ trước — không ghi thêm,
/// không trừ tiền thêm. BillSubtotal: tạm tính mới của cả hoá đơn (phiên gốc + phiên gộp vào).
/// </summary>
public sealed record OrderCancelResult(string Outcome, long OrderItemId, string ItemName, int Quantity, string Unit, decimal LineTotal,
    bool Charged, string? CancelReason, DateTime? CancelledAtUtc, string? CancelledByName, decimal BillSubtotal)
{
    public bool AlreadyCancelled => Outcome == "AlreadyCancelled";

    public string Message
    {
        get
        {
            var line = $"{Quantity} {Unit} {ItemName}";
            var subtotal = $"Tạm tính hiện tại: {Vnd.Format(BillSubtotal)}.";
            if (AlreadyCancelled)
            {
                var by = CancelledByName is null ? "" : $" bởi {CancelledByName}";
                var at = CancelledAtUtc is { } time ? $" lúc {VietnamTime.FromUtc(time):HH:mm}" : "";
                return $"{line} đã được hủy trước đó{by}{at}; không ghi thêm và không trừ tiền thêm. {subtotal}";
            }
            var reason = OrderCancelRules.Label(CancelReason);
            return Charged
                ? $"{OrderCancelRules.ChargedLabel}: {line} ({reason}). Bếp đã bắt đầu xử lý nên món vẫn tính tiền {Vnd.Format(LineTotal)}, tạm tính không giảm; màn hình bếp hiện thông báo dừng món. {subtotal}"
                : $"Đã hủy {line} ({reason}): trừ {Vnd.Format(LineTotal)} khỏi tạm tính và gỡ khỏi màn hình bếp. {subtotal}";
        }
    }
}

public sealed class OrderCancelStore(string connectionString)
{
    /// <summary>Huỷ dòng món qua dbo.usp_CancelOrderItem (một giao dịch có khoá; kiểm tra quyền, hoá đơn, lý do, trạng thái mới nhất).</summary>
    /// <exception cref="SqlException">Bị từ chối (mã 51000–51499, xem OrderCancelRules.RejectionMessage).</exception>
    public async Task<OrderCancelResult> CancelAsync(long orderItemId, long sessionId, string reason, string? note, string? expectedStatus,
        int actorUserId, CancellationToken cancellationToken, bool confirmCharged = false)
    {
        await using var connection = new SqlConnection(connectionString);
        await using var command = new SqlCommand("dbo.usp_CancelOrderItem", connection)
            { CommandType = CommandType.StoredProcedure, CommandTimeout = 30 };
        command.Parameters.Add("@OrderItemId", SqlDbType.BigInt).Value = orderItemId;
        command.Parameters.Add("@Reason", SqlDbType.VarChar, 30).Value = reason;
        command.Parameters.Add("@ActorUserId", SqlDbType.Int).Value = actorUserId;
        command.Parameters.Add("@Note", SqlDbType.NVarChar, 500).Value = (object?)NormalizeNote(note) ?? DBNull.Value;
        command.Parameters.Add("@SessionId", SqlDbType.BigInt).Value = sessionId;
        command.Parameters.Add("@ExpectedStatus", SqlDbType.VarChar, 20).Value = (object?)expectedStatus ?? DBNull.Value;
        // S3-05 Task 3: Quản lý đã đánh dấu "xác nhận huỷ có tính tiền" (bắt buộc khi bếp đã bắt đầu).
        command.Parameters.Add("@ConfirmCharged", SqlDbType.Bit).Value = confirmCharged;
        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("usp_CancelOrderItem không trả kết quả.");
        return new OrderCancelResult(reader.GetString(0), reader.GetInt64(1), reader.GetString(2), reader.GetInt32(3),
            reader.GetString(4), reader.GetDecimal(5), reader.GetBoolean(6), reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : DateTime.SpecifyKind(reader.GetDateTime(8), DateTimeKind.Utc),
            reader.IsDBNull(9) ? null : reader.GetString(9), reader.GetDecimal(10));
    }

    /// <summary>Ghi chú tuỳ chọn: bỏ khoảng trắng thừa, rỗng thành null, tối đa 500 ký tự.</summary>
    public static string? NormalizeNote(string? note)
    {
        var text = note?.Trim();
        return string.IsNullOrEmpty(text) ? null : text.Length > 500 ? text[..500] : text;
    }
}
