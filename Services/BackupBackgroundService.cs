namespace bento_order.Services;

// 每天 03:00（台灣時間）自動備份；失敗時發 Telegram 通知
public class BackupBackgroundService : BackgroundService
{
    private const int BackupHour = 3;

    private readonly IServiceProvider _serviceProvider;
    private readonly BackupService _backupService;
    private readonly ILogger<BackupBackgroundService> _logger;

    public BackupBackgroundService(IServiceProvider serviceProvider,
        BackupService backupService, ILogger<BackupBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _backupService = backupService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = AppTime.Now;
            var targetTime = now.Date.AddHours(BackupHour);
            if (now >= targetTime)
            {
                targetTime = targetTime.AddDays(1);
            }

            await Task.Delay(targetTime - now, stoppingToken);

            try
            {
                await _backupService.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                await NotifyFailureAsync(ex, stoppingToken);
            }
        }
    }

    private async Task NotifyFailureAsync(Exception error,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var notifier = scope.ServiceProvider
                .GetRequiredService<TelegramNotifier>();

            if (!await notifier.IsConfiguredAsync())
            {
                _logger.LogWarning("備份失敗，但尚未設定 Telegram，無法通知");
                return;
            }

            await notifier.SendAsync(
                $"⚠️ 便當訂餐系統：資料庫備份失敗（{AppTime.Now:yyyy/MM/dd HH:mm}）\n" +
                $"{error.Message}",
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "備份失敗通知發送失敗");
        }
    }
}
