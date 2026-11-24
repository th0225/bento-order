namespace bento_order.Services;

// 每天 09:05（台灣時間）推播當日訂餐統計
public class OrderReportService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OrderReportService> _logger;

    public OrderReportService(IServiceProvider serviceProvider,
        ILogger<OrderReportService> logger)
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
            var targetTime = now.Date.AddHours(9).AddMinutes(5);

            if (now >= targetTime)
            {
                targetTime = targetTime.AddDays(1);
            }

            var delay = targetTime - now;
            _logger.LogInformation("下次 LINE 統計發送時間：{Target}，等待 {Hours:F2} 小時",
                targetTime, delay.TotalHours);
            await Task.Delay(delay, stoppingToken);

            using var scope = _serviceProvider.CreateScope();
            try
            {
                var bentoService = scope.ServiceProvider
                    .GetRequiredService<BentoReportService>();
                await bentoService.ProcessAndSendReportAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LINE 統計發送失敗");
            }
        }
    }
}
