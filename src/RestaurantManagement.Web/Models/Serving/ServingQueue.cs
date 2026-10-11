using RestaurantManagement.Web.Models.Reservations;

namespace RestaurantManagement.Web.Models.Serving;

/// <summary>
/// Một dòng đọc từ dbo.OrderItems cho danh sách chờ mang ra. <paramref name="ReadyAtUtc"/> là thời điểm Bếp bấm "Đã xong"
/// (usp_TransitionOrderItem ghi ReadyAt = SYSUTCDATETIME()).
/// </summary>
public sealed record ReadyDishRow(long Id, string TableCode, string ItemName, string Unit, int Quantity, string? Notes,
    string Status, DateTime ReadyAtUtc, DateTime? ServedAtUtc);

/// <summary>Một món trên danh sách chờ mang ra, kèm thời gian đã chờ tính tới <see cref="ServingQueueSnapshot.NowUtc"/>.</summary>
public sealed record ServingQueueItem(long Id, string TableCode, string ItemName, string Unit, int Quantity, string? Notes,
    DateTime ReadyAtUtc, long WaitMs, bool IsOverdue)
{
    /// <summary>Giờ hoàn thành theo giờ Việt Nam (UTC+7).</summary>
    public DateTime ReadyAtVietnam => VietnamTime.FromUtc(ReadyAtUtc);

    /// <summary>Thời gian đã chờ dạng "m:ss" (hoặc "h:mm:ss" khi quá 1 giờ).</summary>
    public string WaitLabel => ServingQueue.FormatWait(WaitMs);
}

/// <summary>Danh sách chờ tại một thời điểm (đồng hồ của database, cùng nguồn với ReadyAt).</summary>
public sealed record ServingQueueSnapshot(DateTime NowUtc, IReadOnlyList<ServingQueueItem> Items)
{
    public int OverdueCount => Items.Count(i => i.IsOverdue);
}

/// <summary>
/// Danh sách món đã xong, chờ Phục vụ mang ra (màn hình /Serving).
/// - Thời gian chờ tính từ lúc món được ghi nhận hoàn thành (ReadyAt) tới hiện tại.
/// - Món chưa phục vụ chờ QUÁ 5 phút được đánh dấu nổi bật (cùng ngưỡng với vw_KitchenQueue.ServingOverdue trong 004_ViewsAndGuards.sql).
/// - Luôn sắp theo thời điểm hoàn thành (món xong trước đứng trước); trùng thời điểm thì theo Id. Cảnh báo không làm đổi thứ tự.
/// - Món đã phục vụ (Served) hoặc không còn ở trạng thái Ready không xuất hiện, nên không bao giờ bị cảnh báo.
/// Trang tự cập nhật đồng hồ mỗi giây và tải lại danh sách mỗi <see cref="RefreshIntervalMs"/> (wwwroot/js/serving-queue.js).
/// </summary>
public static class ServingQueue
{
    public const int OverdueMinutes = 5;
    public static readonly TimeSpan OverdueAfter = TimeSpan.FromMinutes(OverdueMinutes);
    public static long OverdueAfterMs => (long)OverdueAfter.TotalMilliseconds;

    /// <summary>
    /// S3-08 Task 1: món vừa hoàn thành phải hiện (kèm thông báo mã bàn, tên món) trên màn hình Phục vụ trong tối đa 5 giây.
    /// Trang tải lại mỗi 3 giây, còn dư 2 giây cho mạng và database.
    /// </summary>
    public const int NotifyWithinMs = 5000;

    /// <summary>Chu kỳ trang tải lại danh sách từ máy chủ (món mới xong / món đã được người khác mang ra).</summary>
    public const int RefreshIntervalMs = 3000;

    /// <summary>Thời gian chờ từ lúc hoàn thành; không âm (đồng hồ lệch vài mili giây vẫn là 0).</summary>
    public static TimeSpan WaitTime(DateTime readyAtUtc, DateTime nowUtc) =>
        nowUtc > readyAtUtc ? nowUtc - readyAtUtc : TimeSpan.Zero;

    /// <summary>Cảnh báo khi đã chờ vượt quá 5 phút (đúng 5:00 chưa cảnh báo).</summary>
    public static bool IsOverdue(TimeSpan wait) => wait > OverdueAfter;

    public static IReadOnlyList<ServingQueueItem> Build(IEnumerable<ReadyDishRow> rows, DateTime nowUtc) =>
        rows.Where(r => r.Status == "Ready" && r.ServedAtUtc is null)
            .OrderBy(r => r.ReadyAtUtc).ThenBy(r => r.Id)
            .Select(r =>
            {
                var wait = WaitTime(r.ReadyAtUtc, nowUtc);
                return new ServingQueueItem(r.Id, r.TableCode, r.ItemName, r.Unit, r.Quantity, r.Notes,
                    r.ReadyAtUtc, (long)wait.TotalMilliseconds, IsOverdue(wait));
            })
            .ToArray();

    public static ServingQueueSnapshot Snapshot(IEnumerable<ReadyDishRow> rows, DateTime nowUtc) => new(nowUtc, Build(rows, nowUtc));

    public static string FormatWait(long waitMs)
    {
        var total = Math.Max(0, waitMs) / 1000;
        var hours = total / 3600;
        var minutes = total % 3600 / 60;
        var seconds = total % 60;
        return hours > 0 ? $"{hours}:{minutes:00}:{seconds:00}" : $"{minutes}:{seconds:00}";
    }

    /// <summary>
    /// S3-08 Task 1: kết quả bấm "Đã mang ra". Thủ tục usp_TransitionOrderItem khoá độc quyền (sp_getapplock) trước khi đọc trạng thái,
    /// nên hai nhân viên bấm cùng lúc thì chỉ một người chuyển được món sang Served (<paramref name="changed"/> = true);
    /// người còn lại nhận <see cref="ServeOutcome.AlreadyServed"/> kèm tên người đã mang ra và giờ mang ra.
    /// </summary>
    public static ServeResult ServeOutcomeFor(bool changed, int actorUserId, string tableCode, string itemName,
        int? servedById, string? servedByName, DateTime? servedAtUtc)
    {
        var dish = $"Bàn {tableCode} · {itemName}";
        if (changed)
            return new(ServeOutcome.Served, $"Đã mang ra: {dish}.");
        var when = servedAtUtc is { } at ? $" lúc {VietnamTime.FromUtc(at):HH:mm}" : "";
        var message = servedById == actorUserId
            ? $"Bạn đã xác nhận mang ra {dish}{when} rồi."
            : $"{dish} đã được {(string.IsNullOrWhiteSpace(servedByName) ? "nhân viên khác" : servedByName)} mang ra{when}. Không cần mang lại.";
        return new(ServeOutcome.AlreadyServed, message, servedByName, servedAtUtc);
    }

    /// <summary>Dữ liệu JSON cho GET /Serving/Queue (trang gọi lại định kỳ).</summary>
    public static object ToJson(ServingQueueSnapshot snapshot) => new
    {
        nowUtc = DateTime.SpecifyKind(snapshot.NowUtc, DateTimeKind.Utc),
        overdueAfterMs = OverdueAfterMs,
        overdueCount = snapshot.OverdueCount,
        items = snapshot.Items.Select(i => new
        {
            id = i.Id,
            tableCode = i.TableCode,
            itemName = i.ItemName,
            unit = i.Unit,
            quantity = i.Quantity,
            notes = i.Notes,
            readyAtUtc = DateTime.SpecifyKind(i.ReadyAtUtc, DateTimeKind.Utc),
            readyAt = i.ReadyAtVietnam.ToString("HH:mm"),
            waitMs = i.WaitMs,
            overdue = i.IsOverdue
        })
    };
}

/// <summary>S3-08 Task 1: kết quả xác nhận mang món ra.</summary>
public enum ServeOutcome { Served, AlreadyServed }

public sealed record ServeResult(ServeOutcome Outcome, string Message, string? ServedByName = null, DateTime? ServedAtUtc = null)
{
    public bool Succeeded => Outcome == ServeOutcome.Served;
}
