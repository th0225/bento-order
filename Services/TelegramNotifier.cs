using bento_order.Models;

namespace bento_order.Services;

// 透過 Telegram Bot 發送系統通知（例如備份失敗）。
// Bot Token 與 Chat ID 在 /admin/system-config 設定（SystemConfig）。
public class TelegramNotifier
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BentoDbService _bentoDbService;

    public TelegramNotifier(IHttpClientFactory httpClientFactory,
        BentoDbService bentoDbService)
    {
        _httpClientFactory = httpClientFactory;
        _bentoDbService = bentoDbService;
    }

    public async Task<bool> IsConfiguredAsync() =>
        !string.IsNullOrWhiteSpace(await _bentoDbService.GetConfigValueAsync(
            ConfigKeys.TelegramBotToken)) &&
        !string.IsNullOrWhiteSpace(await _bentoDbService.GetConfigValueAsync(
            ConfigKeys.TelegramChatId));

    // 發送訊息；未設定或發送失敗時丟出例外
    public async Task SendAsync(string text,
        CancellationToken cancellationToken = default)
    {
        var token = await _bentoDbService.GetConfigValueAsync(
            ConfigKeys.TelegramBotToken);
        var chatId = await _bentoDbService.GetConfigValueAsync(
            ConfigKeys.TelegramChatId);

        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(chatId))
        {
            throw new InvalidOperationException("尚未設定 Telegram Bot Token 或 Chat ID");
        }

        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(20);

        using var response = await client.PostAsync(
            $"https://api.telegram.org/bot{token}/sendMessage",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["chat_id"] = chatId,
                ["text"] = text
            }),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // Telegram 會回傳 JSON 說明（例如 chat not found、Unauthorized）
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Telegram 發送失敗（{(int)response.StatusCode}）：{body}");
        }
    }
}
