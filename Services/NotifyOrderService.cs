public class NotifyOrderService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;

    public NotifyOrderService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.Now;
            var targetTime = new DateTime(
                now.Year, now.Month, 28, 8, 50, 0
            );

            if (now > targetTime)
            {
                targetTime = targetTime.AddMonths(1);
            }

            var delay = targetTime - now;
            await Task.Delay(delay, stoppingToken);

            using (var scope = _serviceProvider.CreateScope())
            {
                try
                {
                    var bentoService = scope.ServiceProvider.GetRequiredService
                        <BentoReportService>();
                    await bentoService.SendNotifyOrderMessageAsync();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        $"[Bento] LINE 訂餐通知發送失敗：{ex.Message}"
                    );
                }
            }
        }
    }
}