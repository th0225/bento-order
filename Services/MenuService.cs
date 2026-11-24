namespace bento_order.Services;

// 每月菜單圖片（已在瀏覽器端壓縮成 JPEG），檔案位置見 AppPaths。
// 註冊為 singleton：只處理檔案，沒有 circuit 狀態。
public class MenuService
{
    // 上傳頁預設的月份：這天（含）以後預設上傳下個月的菜單；
    // 舊版單一菜單也用這個規則判斷屬於哪個月
    public const int NextMonthFromDay = 20;

    private readonly AppPaths _appPaths;
    private readonly ILogger<MenuService> _logger;

    public MenuService(AppPaths appPaths, ILogger<MenuService> logger)
    {
        _appPaths = appPaths;
        _logger = logger;
    }

    // 某天預設對應的菜單月份（該月 1 日）
    public static DateTime DefaultMenuMonth(DateTime date)
    {
        var month = new DateTime(date.Year, date.Month, 1);
        return date.Day >= NextMonthFromDay ? month.AddMonths(1) : month;
    }

    // 菜單圖片的網址（對應 Program.cs 的 /menu-image/{month} 端點）
    public static string ImageUrl(DateTime month) =>
        $"/menu-image/{month:yyyy-MM}";

    public bool Exists(DateTime month) =>
        File.Exists(_appPaths.MenuFilePath(month));

    // 有上傳菜單的月份（新到舊）
    public List<DateTime> GetMonthsWithMenu()
    {
        if (!Directory.Exists(_appPaths.MenuDirectory))
        {
            return [];
        }

        return Directory.GetFiles(_appPaths.MenuDirectory, "*.jpg")
            .Select(Path.GetFileNameWithoutExtension)
            .Select(name => DateTime.TryParseExact(name, "yyyy-MM", null,
                System.Globalization.DateTimeStyles.None, out var month)
                    ? month : (DateTime?)null)
            .OfType<DateTime>()
            .OrderByDescending(m => m)
            .ToList();
    }

    public async Task UploadAsync(DateTime month, Stream fileStream)
    {
        var path = _appPaths.MenuFilePath(month);
        Directory.CreateDirectory(_appPaths.MenuDirectory);

        // 先寫到暫存檔再取代，避免寫到一半時有人讀到不完整的圖
        var tempPath = path + ".tmp";
        await using (var fs = new FileStream(tempPath, FileMode.Create))
        {
            await fileStream.CopyToAsync(fs);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    public void Delete(DateTime month)
    {
        var path = _appPaths.MenuFilePath(month);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    // 舊版只有一張菜單：啟動時依上傳日期搬到對應月份（20 日以後算下個月）。
    // 該月份已有菜單時不動，保留舊檔讓管理員自行處理
    public void MigrateLegacyMenu()
    {
        var legacy = _appPaths.LegacyMenuFilePath;
        if (!File.Exists(legacy))
        {
            return;
        }

        try
        {
            var month = DefaultMenuMonth(File.GetLastWriteTime(legacy));
            var target = _appPaths.MenuFilePath(month);
            if (File.Exists(target))
            {
                return;
            }

            Directory.CreateDirectory(_appPaths.MenuDirectory);
            File.Move(legacy, target);
            _logger.LogInformation("舊菜單 {Legacy} 已搬到 {Month:yyyy-MM}", legacy,
                month);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "搬移舊菜單失敗");
        }
    }
}
