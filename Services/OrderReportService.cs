public class OrderReportService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;

    public OrderReportService(IServiceProvider serviceProvider)
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
                now.Year, now.Month, now.Day, 9, 5, 0
            );

            if (now > targetTime)
            {
                targetTime = targetTime.AddDays(1);
            }

            var delay = targetTime - now;
            Console.WriteLine($"[Bento] 下次發送時間：{targetTime}，等待 {delay.TotalHours:F2} 小時");
            await Task.Delay(delay, stoppingToken);

            using (var scope = _serviceProvider.CreateScope())
            {
                var bentoService =
                scope.ServiceProvider.GetRequiredService<BentoReportService>();
                await bentoService.ProcessAndSendReportAsync();
            };
        }
    }
}