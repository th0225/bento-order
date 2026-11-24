using System.Globalization;
using System.IO.Compression;
using Microsoft.Data.Sqlite;

namespace bento_order.Services;

// 資料備份：資料庫（以 VACUUM INTO 產生一致的快照，網站運作中也安全）與每月菜單，
// 壓縮成 backups/backup-yyyyMMdd-HHmmss.zip，保留 RetentionDays 天。
// 每天由 BackupBackgroundService 自動執行，也可在 /admin/backups 手動執行與下載。
//
// 還原：停止服務 → 解壓縮 → 用 app.db 取代原資料庫、刪除 app.db-wal / app.db-shm、
// 把 menus/ 放回原位 → 啟動服務（詳見 docs/backup-restore.md）。
public class BackupService
{
    public const int RetentionDays = 30;
    private const string FilePrefix = "backup-";
    private const string TimestampFormat = "yyyyMMdd-HHmmss";

    public record BackupFile(string Name, DateTime CreatedAt, long Size);

    private readonly AppPaths _appPaths;
    private readonly ILogger<BackupService> _logger;
    // 避免自動與手動備份同時執行
    private readonly SemaphoreSlim _lock = new(1, 1);

    public BackupService(AppPaths appPaths, ILogger<BackupService> logger)
    {
        _appPaths = appPaths;
        _logger = logger;
    }

    // 最近一次備份的結果（重新啟動後清空，畫面以備份檔列表為準）
    public DateTime? LastRunAt { get; private set; }
    public string? LastError { get; private set; }

    // 執行一次備份並清除過期備份，回傳新的備份檔
    public async Task<BackupFile> RunAsync(
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var backup = await CreateBackupAsync(cancellationToken);
            DeleteExpired();

            LastRunAt = AppTime.Now;
            LastError = null;
            _logger.LogInformation("備份完成：{Name}（{Size} bytes）", backup.Name,
                backup.Size);
            return backup;
        }
        catch (Exception ex)
        {
            LastRunAt = AppTime.Now;
            LastError = ex.Message;
            _logger.LogError(ex, "備份失敗");
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    // 現有的備份（新到舊）
    public List<BackupFile> List()
    {
        if (!Directory.Exists(_appPaths.BackupDirectory))
        {
            return [];
        }

        return Directory.GetFiles(_appPaths.BackupDirectory, FilePrefix + "*.zip")
            .Select(path => TryParse(path, out var created)
                ? new BackupFile(Path.GetFileName(path), created,
                    new FileInfo(path).Length)
                : null)
            .OfType<BackupFile>()
            .OrderByDescending(b => b.CreatedAt)
            .ToList();
    }

    // 取得備份檔的完整路徑；檔名格式不符（例如含路徑）時回傳 null
    public string? GetPath(string name)
    {
        if (Path.GetFileName(name) != name ||
            !TryParse(name, out _) ||
            !name.EndsWith(".zip", StringComparison.Ordinal))
        {
            return null;
        }

        var path = Path.Combine(_appPaths.BackupDirectory, name);
        return File.Exists(path) ? path : null;
    }

    private async Task<BackupFile> CreateBackupAsync(
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_appPaths.BackupDirectory);

        var now = AppTime.Now;
        var name = $"{FilePrefix}{now.ToString(TimestampFormat, CultureInfo.InvariantCulture)}.zip";
        var zipPath = Path.Combine(_appPaths.BackupDirectory, name);
        var tempDb = Path.Combine(_appPaths.BackupDirectory, $"{name}.db.tmp");
        var tempZip = zipPath + ".tmp";

        try
        {
            // VACUUM INTO：在交易中產生完整且一致的資料庫副本（含 WAL 中的資料）
            await using (var connection = new SqliteConnection(
                _appPaths.ConnectionString))
            {
                await connection.OpenAsync(cancellationToken);
                await using var command = connection.CreateCommand();
                command.CommandText = "VACUUM INTO $path";
                command.Parameters.AddWithValue("$path", tempDb);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            // 壓縮：app.db + menus/
            using (var zip = ZipFile.Open(tempZip, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(tempDb, "app.db",
                    CompressionLevel.Optimal);

                if (Directory.Exists(_appPaths.MenuDirectory))
                {
                    foreach (var file in Directory.GetFiles(
                        _appPaths.MenuDirectory, "*.jpg"))
                    {
                        zip.CreateEntryFromFile(file,
                            $"menus/{Path.GetFileName(file)}",
                            CompressionLevel.Fastest);
                    }
                }
            }

            File.Move(tempZip, zipPath, overwrite: true);
            return new BackupFile(name, now, new FileInfo(zipPath).Length);
        }
        finally
        {
            File.Delete(tempDb);
            File.Delete(tempZip);
        }
    }

    // 刪除超過保留天數的備份
    private void DeleteExpired()
    {
        var cutoff = AppTime.Now.AddDays(-RetentionDays);
        foreach (var backup in List().Where(b => b.CreatedAt < cutoff))
        {
            File.Delete(Path.Combine(_appPaths.BackupDirectory, backup.Name));
            _logger.LogInformation("刪除過期備份：{Name}", backup.Name);
        }
    }

    // 從檔名 backup-yyyyMMdd-HHmmss.zip 取得建立時間
    private static bool TryParse(string path, out DateTime createdAt)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        createdAt = default;
        return name.StartsWith(FilePrefix, StringComparison.Ordinal) &&
            DateTime.TryParseExact(name[FilePrefix.Length..], TimestampFormat,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out createdAt);
    }
}
