namespace RestaurantManagement.Web.Services;

/// <summary>
/// S4-08 Task 1: tác vụ chạy nền trong web, định kỳ kiểm tra các lượt đặt bàn đã xác nhận còn đúng 2 tiếng tới giờ hẹn
/// và gửi email nhắc (<see cref="BookingEmailDispatcher.SendDueRemindersAsync"/>).
/// Cấu hình: Email:ReminderPollSeconds (mặc định 60 giây; 0 = tắt).
/// </summary>
public sealed class BookingReminderWorker(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<BookingReminderWorker> logger) : BackgroundService
{
    public const int DefaultPollSeconds = 60;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = configuration.GetValue("Email:ReminderPollSeconds", DefaultPollSeconds);
        if (seconds <= 0 || string.IsNullOrWhiteSpace(configuration.GetConnectionString("DefaultConnection")))
        {
            logger.LogInformation("Tác vụ gửi email nhắc lịch đang tắt (Email:ReminderPollSeconds = {Seconds}).", seconds);
            return;
        }

        var interval = TimeSpan.FromSeconds(seconds);
        logger.LogInformation("Kiểm tra lượt đặt bàn cần gửi email nhắc mỗi {Seconds} giây.", seconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<BookingEmailDispatcher>().SendDueRemindersAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Không chạy được tác vụ nhắc lịch. Kiểm tra kết nối database và migration 059.");
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
