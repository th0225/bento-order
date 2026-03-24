using bento_order.Models;
using bento_order.Services;
using Line.Messaging;

public class BentoReportService
{
    private readonly BentoDbService _bentoDbService;

    public BentoReportService(BentoDbService bentoDbService)
    {
        _bentoDbService = bentoDbService;
    }

    public async Task ProcessAndSendReportAsync()
    {
        var today = DateTime.Today;
        DayOfWeek day = today.DayOfWeek;

        var orderStats = await _bentoDbService.GetDailyStatsAsync(today);

        // 週末沒人訂餐則不發送訊息
        if (day == DayOfWeek.Saturday || day == DayOfWeek.Sunday)
        {
            if (!orderStats.Any())
            {
                return;
            }
        }

        string messageContent = string.Empty;
        if (orderStats.Any())
        {
            messageContent = string.Join(", ", orderStats.Select(
                s => $"{s.MealName}x{s.Count}"
            ));
        }
        else
        {
            messageContent = "今日無人訂餐";
        }

        int total = orderStats.Sum(s => s.Count);
        var finalMessage = $"新技術午餐統計 ({today:MM/dd}): \n" +
                           $"{messageContent}\n" +
                           $"----------------\n" +
                           $"總計: {total} 份";

        List<SystemConfig> configs = await _bentoDbService.GetConfigAsync();
        string lineId = configs.FirstOrDefault(
            c => c.Key == "LineId"
        )?.Value ?? string.Empty;
        string lineAccessToken = configs.FirstOrDefault(
            c => c.Key == "LineAccessToken"
        )?.Value ?? string.Empty;

        var client = new LineMessagingClient(lineAccessToken);
        await client.PushMessageAsync(lineId, finalMessage);
    }
}