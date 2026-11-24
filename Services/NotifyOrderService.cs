namespace bento_order.Services;

// 每月 28 日 08:50（台灣時間）推播「完成下個月訂餐」提醒
public class NotifyOrderService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<NotifyOrderService> _logger;

    public NotifyOrderService(IServiceProvider serviceProvider,
        ILogger<NotifyOrderService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = AppTime.Now;
            var targetTime = new DateTime(
                now.Year, now.Month, 28, 8, 50, 0
            );

            if (now >= targetTime)
            {
                targetTime = targetTime.AddMonths(1);
            }

            await Task.Delay(targetTime - now, stoppingToken);

            using var scope = _serviceProvider.CreateScope();
            try
            {
                var bentoService = scope.ServiceProvider
                    .GetRequiredService<BentoReportService>();
                await bentoService.SendNotifyOrderMessageAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LINE 訂餐提醒發送失敗");
            }
        }
    }
}
