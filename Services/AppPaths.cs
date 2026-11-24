namespace bento_order.Services;

// 執行期間產生的檔案（資料庫、菜單圖片）的存放位置。
//
// 設定 DataDirectory（appsettings 或環境變數 DataDirectory）時：
//   資料庫 = {DataDirectory}/app.db，菜單 = {DataDirectory}/menus/yyyy-MM.jpg，
//   備份 = {DataDirectory}/backups/
//   建議正式機設定在發佈資料夾之外，重新部署才不會被覆蓋。
// 沒有設定時（相容既有部署）：
//   資料庫 = ConnectionStrings:DefaultConnection（相對路徑以 ContentRootPath 為準），
//   菜單 = 資料庫所在資料夾/menus/yyyy-MM.jpg，備份 = 資料庫所在資料夾/backups/
//
// 舊版只有一張菜單（LegacyMenuFilePath），由 MenuService 在啟動時搬到對應月份。
public class AppPaths
{
    public string ConnectionString { get; }
    // 每月菜單的資料夾
    public string MenuDirectory { get; }
    // 舊版單一菜單的位置
    public string LegacyMenuFilePath { get; }
    // 每日備份的資料夾（與資料庫放在一起）
    public string BackupDirectory { get; }

    public AppPaths(IConfiguration configuration, IWebHostEnvironment env)
    {
        var dataDirectory = configuration["DataDirectory"];

        if (!string.IsNullOrWhiteSpace(dataDirectory))
        {
            dataDirectory = Path.GetFullPath(dataDirectory,
                env.ContentRootPath);
            Directory.CreateDirectory(dataDirectory);

            ConnectionString =
                $"Data Source={Path.Combine(dataDirectory, "app.db")}";
            MenuDirectory = Path.Combine(dataDirectory, "menus");
            BackupDirectory = Path.Combine(dataDirectory, "backups");
            LegacyMenuFilePath = Path.Combine(dataDirectory, "menu.jpg");
            return;
        }

        var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(
            configuration.GetConnectionString("DefaultConnection"));
        builder.DataSource = Path.GetFullPath(builder.DataSource,
            env.ContentRootPath);

        // SQLite 不會自動建立資料夾，先確保資料庫檔所在的資料夾存在
        var dbDirectory = Path.GetDirectoryName(builder.DataSource)
            ?? env.ContentRootPath;
        Directory.CreateDirectory(dbDirectory);

        ConnectionString = builder.ToString();
        MenuDirectory = Path.Combine(dbDirectory, "menus");
        BackupDirectory = Path.Combine(dbDirectory, "backups");
        LegacyMenuFilePath = Path.Combine(
            env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot"),
            "menu.jpg");
    }

    // 某個月份的菜單檔案路徑
    public string MenuFilePath(DateTime month) =>
        Path.Combine(MenuDirectory, $"{month:yyyy-MM}.jpg");
}
