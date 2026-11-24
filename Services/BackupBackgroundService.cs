namespace bento_order.Services;

// 每天 03:00（台灣時間）自動備份。
// 失敗時寫入 log，並由 /admin/backups 顯示最近一次的結果（BackupService.LastError）
public class BackupBackgroundService : BackgroundService
{
    private const int BackupHour = 3;

    private readonly BackupService _backupService;

    public BackupBackgroundService(BackupService backupService)
    {
        _backupService = backupService;
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
            catch (Exception)
            {
                // BackupService 已記錄 log 與 LastError，隔天會再試一次
            }
        }
    }
}
