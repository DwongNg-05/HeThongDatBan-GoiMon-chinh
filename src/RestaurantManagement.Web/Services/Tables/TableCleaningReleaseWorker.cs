using Microsoft.Data.SqlClient;

namespace RestaurantManagement.Web.Services;

/// <summary>
/// Sau khi thanh toán (dbo.usp_Checkout), bàn chuyển sang "Đang dọn". Tác vụ nền này trả bàn về trạng thái sẵn sàng
/// chậm nhất <see cref="MaxCleaningMinutes"/> phút sau đó, kể cả khi nhân viên quên bấm "Dọn xong":
///   * Bàn được ghi lại là Available. Nếu bàn có lượt đặt đang giữ (Đã xác nhận, hoặc Chờ xác nhận trong 30 phút tới),
///     sơ đồ bàn và màn hình "Khu vực &amp; bàn" tự hiển thị "Đã đặt trước" (cùng quy tắc với TableMapStore.TablesSql),
///     ngược lại hiển thị "Trống".
///   * Không ghi thẳng 'Reserved' vào database để bàn không bị kẹt ở "Đã đặt trước" khi lượt đặt bị huỷ hoặc hết giờ.
///   * Chỉ đổi bàn vẫn còn 'Cleaning': nhân viên đã đổi tay trước đó thì giữ nguyên.
/// Trigger tr_DiningTables_StatusChanged ghi sự kiện, TableStatusOutboxWorker đẩy thay đổi tới mọi sơ đồ đang mở.
/// </summary>
public sealed class TableCleaningReleaseWorker(
    IConfiguration configuration,
    ILogger<TableCleaningReleaseWorker> logger) : BackgroundService
{
    /// <summary>Thời gian dọn bàn tối đa (phút) trước khi bàn tự trở lại sẵn sàng.</summary>
    public const int MaxCleaningMinutes = 15;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(1);

    public const string ReleaseSql = """
        SET NOCOUNT ON;
        DECLARE @changed TABLE (Code varchar(20));
        UPDATE t
        SET Status = 'Available',
            StatusChangedAt = SYSUTCDATETIME()
        OUTPUT inserted.Code INTO @changed
        FROM dbo.DiningTables t
        WHERE t.Status = 'Cleaning'
          AND t.StatusChangedAt <= DATEADD(minute, -@MaxCleaningMinutes, SYSUTCDATETIME());
        SELECT COUNT(*) FROM @changed;
        """;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            logger.LogInformation("Auto-release of cleaning tables is disabled because no default connection string is configured.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = PollInterval;
            try
            {
                var released = await ReleaseAsync(connectionString, MaxCleaningMinutes, stoppingToken);
                if (released > 0)
                    logger.LogInformation("Released {Count} table(s) after {Minutes} minutes of cleaning.", released, MaxCleaningMinutes);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not release cleaning tables. Retrying in {Retry}.", RetryInterval);
                delay = RetryInterval;
            }

            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Trả các bàn đã "Đang dọn" quá <paramref name="maxCleaningMinutes"/> phút về sẵn sàng. Trả về số bàn được đổi.</summary>
    public static async Task<int> ReleaseAsync(string connectionString, int maxCleaningMinutes = MaxCleaningMinutes,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(ReleaseSql, connection) { CommandTimeout = 30 };
        command.Parameters.Add("@MaxCleaningMinutes", System.Data.SqlDbType.Int).Value = maxCleaningMinutes;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }
}
