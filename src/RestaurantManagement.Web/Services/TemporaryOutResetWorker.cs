using Microsoft.Data.SqlClient;

namespace RestaurantManagement.Web.Services;

/// <summary>
/// S2-08 Task 3: mốc 00:00 theo múi giờ Asia/Ho_Chi_Minh (UTC+7, không đổi giờ mùa hè) để tự đặt lại "Tạm hết".
/// Tách riêng để kiểm thử không cần chờ tới nửa đêm.
/// </summary>
public static class TemporaryOutResetSchedule
{
    public const string TimeZoneId = "Asia/Ho_Chi_Minh";
    public const string JobName = "TemporaryOutReset";

    public static TimeZoneInfo Zone { get; } = FindZone();

    /// <summary>00:00 (giờ Việt Nam) của ngày đang diễn ra tại thời điểm <paramref name="nowUtc"/>, trả về giờ UTC.</summary>
    public static DateTime CurrentMidnightUtc(DateTime nowUtc)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), Zone);
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local.Date, DateTimeKind.Unspecified), Zone);
    }

    /// <summary>00:00 (giờ Việt Nam) kế tiếp, sau <paramref name="nowUtc"/>, trả về giờ UTC.</summary>
    public static DateTime NextMidnightUtc(DateTime nowUtc)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), Zone);
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local.Date.AddDays(1), DateTimeKind.Unspecified), Zone);
    }

    private static TimeZoneInfo FindZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time"); }
    }
}

/// <summary>
/// S2-08 Task 3: tác vụ chạy nền, mỗi ngày lúc 00:00 (Asia/Ho_Chi_Minh) đưa toàn bộ món đang "Tạm hết" về "Còn món"
/// qua dbo.usp_ResetTemporarilyOutMenuItems (migration 040). Khi web khởi động sau nửa đêm mà mốc hôm nay chưa chạy,
/// tác vụ chạy bù ngay — thủ tục chỉ đặt lại món báo tạm hết trước 00:00, mỗi mốc chỉ chạy một lần.
/// Danh sách món trong ngày, thực đơn công khai và màn hình gọi món tự cập nhật trong tối đa 5 giây (menu-availability.js).
/// </summary>
public sealed class TemporaryOutResetWorker : BackgroundService
{
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly Func<DateTime, CancellationToken, Task<int>> _reset;

    public TemporaryOutResetWorker(IConfiguration configuration, ILogger<TemporaryOutResetWorker> logger)
        : this(logger, TimeProvider.System, (scheduledUtc, ct) => RunAsync(ConnectionStringFrom(configuration), scheduledUtc, logger, ct)) { }

    private TemporaryOutResetWorker(ILogger logger, TimeProvider time, Func<DateTime, CancellationToken, Task<int>> reset)
    {
        _logger = logger;
        _time = time;
        _reset = reset;
    }

    /// <summary>
    /// S2-08 Task 4: tạo tác vụ với đồng hồ và thao tác đặt lại tuỳ chọn — dùng để kiểm thử tác vụ chạy qua mốc 00:00
    /// mà không phải chờ tới nửa đêm.
    /// </summary>
    public static TemporaryOutResetWorker Create(TimeProvider time, Func<DateTime, CancellationToken, Task<int>> reset, ILogger? logger = null)
        => new(logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, time, reset);

    private static string ConnectionStringFrom(IConfiguration configuration) => Environment.GetEnvironmentVariable("RM_CONNECTION_STRING")
        ?? configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Set RM_CONNECTION_STRING to connect the daily menu reset worker to SQL Server.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var logger = _logger;
        while (!stoppingToken.IsCancellationRequested)
        {
            var scheduledUtc = TemporaryOutResetSchedule.CurrentMidnightUtc(_time.GetUtcNow().UtcDateTime);
            try
            {
                await _reset(scheduledUtc, stoppingToken);
                // Chờ tới 00:00 kế tiếp. Nếu thức sớm vài mili giây, vòng lặp tính lại mốc (thủ tục SQL bỏ qua mốc đã chạy).
                var now = _time.GetUtcNow().UtcDateTime;
                var wait = TemporaryOutResetSchedule.NextMidnightUtc(now) - now;
                await Task.Delay(wait > TimeSpan.Zero ? wait : TimeSpan.Zero, _time, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not reset temporarily unavailable menu items for {BusinessDate} ({Zone}). Retrying in 1 minute.",
                    TimeZoneInfo.ConvertTimeFromUtc(scheduledUtc, TemporaryOutResetSchedule.Zone).Date, TemporaryOutResetSchedule.TimeZoneId);
                try { await Task.Delay(TimeSpan.FromMinutes(1), _time, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    /// <summary>Đặt lại mọi món tạm hết trước mốc <paramref name="scheduledUtc"/> (00:00 giờ Việt Nam). Trả về số món được đặt lại.</summary>
    public static async Task<int> RunAsync(string connectionString, DateTime scheduledUtc, ILogger? logger = null, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("dbo.usp_ResetTemporarilyOutMenuItems", connection)
        {
            CommandType = System.Data.CommandType.StoredProcedure,
            CommandTimeout = 60
        };
        command.Parameters.Add("@ScheduledFor", System.Data.SqlDbType.DateTime2).Value = scheduledUtc;
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        logger?.LogInformation("Reset {Count} temporarily unavailable menu items for {BusinessDate} ({Zone}).",
            count, TimeZoneInfo.ConvertTimeFromUtc(scheduledUtc, TemporaryOutResetSchedule.Zone).Date, TemporaryOutResetSchedule.TimeZoneId);
        return count;
    }
}
