using bento_order.Models;
using Line.Messaging;

namespace bento_order.Services;

// 組合訂餐統計與提醒訊息，透過 LINE Messaging API 推播
public class BentoReportService
{
    // 沒有設定標題時使用的預設值
    public const string DefaultReportTitle = "午餐統計";

    private readonly BentoDbService _bentoDbService;

    public BentoReportService(BentoDbService bentoDbService)
    {
        _bentoDbService = bentoDbService;
    }

    // 推播今日訂餐統計（每天 09:05，也可手動觸發）
    public async Task ProcessAndSendReportAsync()
    {
        var today = AppTime.Today;
        var orderStats = await _bentoDbService.GetDailyStatsAsync(today);

        // 週末沒人訂餐則不發送訊息
        if ((today.DayOfWeek == DayOfWeek.Saturday ||
            today.DayOfWeek == DayOfWeek.Sunday) && orderStats.Count == 0)
        {
            return;
        }

        var messageContent = orderStats.Count > 0
            ? string.Join(", ",
                orderStats.Select(s => $"{s.MealName}x{s.Count}"))
            : "今日無人訂餐";

        var title = await _bentoDbService.GetConfigValueAsync(
            ConfigKeys.LineReportTitle);
        if (string.IsNullOrWhiteSpace(title))
        {
            title = DefaultReportTitle;
        }

        var total = orderStats.Sum(s => s.Count);
        var finalMessage = $"{title} ({today:MM/dd}): \n" +
                           $"{messageContent}\n" +
                           $"----------------\n" +
                           $"總計: {total} 份";

        await PushAsync(ConfigKeys.LineId, finalMessage);
    }

    // 推播「完成下個月訂餐」提醒（每月 28 日，也可手動觸發）
    public Task SendNotifyOrderMessageAsync() =>
        PushAsync(ConfigKeys.LineOrderId, "菜單已上傳，請儘快完成下個月訂餐");

    private async Task PushAsync(string targetIdKey, string message)
    {
        var token = await _bentoDbService.GetConfigValueAsync(
            ConfigKeys.LineAccessToken);
        var targetId = await _bentoDbService.GetConfigValueAsync(targetIdKey);

        if (string.IsNullOrWhiteSpace(token) ||
            string.IsNullOrWhiteSpace(targetId))
        {
            throw new InvalidOperationException(
                "尚未設定 LINE Access Token 或群組 ID");
        }

        var client = new LineMessagingClient(token);
        await client.PushMessageAsync(targetId, message);
    }
}
