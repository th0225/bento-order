namespace bento_order.Models;

// 使用者角色（存在 Users.Role 與登入 cookie 的 Role claim）
public static class Roles
{
    public const string Admin = "Admin";
    public const string User = "User";
}

// SystemConfig 資料表的 Key
public static class ConfigKeys
{
    // LINE Messaging API 的 Channel Access Token
    public const string LineAccessToken = "LineAccessToken";
    // 每日訂餐統計推播的群組/聊天室 ID
    public const string LineId = "LineId";
    // 每月訂餐提醒推播的群組/聊天室 ID
    public const string LineOrderId = "LineOrderId";
    // 每日訂餐統計訊息的標題
    public const string LineReportTitle = "LineReportTitle";
    // Telegram Bot Token（系統通知，例如備份失敗）
    public const string TelegramBotToken = "TelegramBotToken";
    // Telegram 接收通知的 Chat ID（個人或群組）
    public const string TelegramChatId = "TelegramChatId";
}
