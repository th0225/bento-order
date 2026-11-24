namespace bento_order.Services;

// 系統的「現在時間」一律以台灣時間計算，不受伺服器時區設定影響。
// 截止時間、排程、「今天」的判斷都要用這裡，不要直接用 DateTime.Now / Today。
public static class AppTime
{
    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei");

    public static DateTime Now =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone);

    public static DateTime Today => Now.Date;
}
