namespace bento_order.Services;

// 執行期間產生的檔案（資料庫、菜單圖片）的存放位置。
//
// 設定 DataDirectory（appsettings 或環境變數 DataDirectory）時：
//   資料庫 = {DataDirectory}/app.db，菜單 = {DataDirectory}/menu.jpg
//   建議正式機設定在發佈資料夾之外，重新部署才不會被覆蓋。
// 沒有設定時維持舊行為（相容既有部署）：
//   資料庫 = ConnectionStrings:DefaultConnection（相對路徑以 ContentRootPath 為準），
//   菜單 = wwwroot/menu.jpg
public class AppPaths
{
    public const string MenuFileName = "menu.jpg";

    public string ConnectionString { get; }
    public string MenuFilePath { get; }

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
            MenuFilePath = Path.Combine(dataDirectory, MenuFileName);
            return;
        }

        var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(
            configuration.GetConnectionString("DefaultConnection"));
        builder.DataSource = Path.GetFullPath(builder.DataSource,
            env.ContentRootPath);

        // SQLite 不會自動建立資料夾，先確保資料庫檔所在的資料夾存在
        var dbDirectory = Path.GetDirectoryName(builder.DataSource);
        if (!string.IsNullOrEmpty(dbDirectory))
        {
            Directory.CreateDirectory(dbDirectory);
        }

        ConnectionString = builder.ToString();
        MenuFilePath = Path.Combine(
            env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot"),
            MenuFileName);
    }

    public bool MenuExists => File.Exists(MenuFilePath);
}
