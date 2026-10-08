namespace RestaurantManagement.Web.Services;

/// <summary>
/// S2-09 Task 2: chạy nền trong web, định kỳ gửi lại email đặt bàn đã thất bại khi tới giờ thử lại.
/// Quy tắc (tối đa 3 lần gửi lại, cách nhau 5 phút) nằm trong database nên nhiều web chạy cùng lúc cũng không gửi trùng.
/// Cấu hình: Email:RetryPollSeconds (mặc định 30 giây; 0 = tắt).
/// </summary>
public sealed class BookingEmailRetryWorker(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<BookingEmailRetryWorker> logger) : BackgroundService
{
    public const int DefaultPollSeconds = 30;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = configuration.GetValue("Email:RetryPollSeconds", DefaultPollSeconds);
        if (seconds <= 0 || string.IsNullOrWhiteSpace(configuration.GetConnectionString("DefaultConnection")))
        {
            logger.LogInformation("Tự động gửi lại email đặt bàn đang tắt (Email:RetryPollSeconds = {Seconds}).", seconds);
            return;
        }

        var interval = TimeSpan.FromSeconds(seconds);
        logger.LogInformation("Kiểm tra email đặt bàn cần gửi lại mỗi {Seconds} giây.", seconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<BookingEmailDispatcher>().RetryDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Không kiểm tra được email cần gửi lại. Kiểm tra kết nối database và migration 030.");
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
