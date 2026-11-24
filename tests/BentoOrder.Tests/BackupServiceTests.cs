using System.IO.Compression;
using bento_order.Data;
using bento_order.Models;
using bento_order.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace BentoOrder.Tests;

// 使用真正的 SQLite 檔案（不是記憶體資料庫），測試 VACUUM INTO 與壓縮
public class BackupServiceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("bento-backup-").FullName;
    private readonly AppPaths _paths;
    private readonly BackupService _backup;

    public BackupServiceTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataDirectory"] = Path.Combine(_root, "data")
            })
            .Build();
        _paths = new AppPaths(config, new FakeEnv(_root));
        _backup = new BackupService(_paths, NullLogger<BackupService>.Instance);

        using var db = CreateContext();
        db.Database.Migrate();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_paths.ConnectionString).Options);

    [Fact]
    public async Task 備份包含資料庫最新資料與菜單()
    {
        // 寫入資料並保持連線開著（資料可能還在 WAL 檔，尚未寫回主檔）
        using var db = CreateContext();
        db.Users.Add(new User { Username = "backup-test", PasswordHash = "x" });
        await db.SaveChangesAsync();

        Directory.CreateDirectory(_paths.MenuDirectory);
        await File.WriteAllBytesAsync(
            _paths.MenuFilePath(new DateTime(2026, 12, 1)), [0xFF, 0xD8, 7]);

        var result = await _backup.RunAsync();

        var zipPath = _backup.GetPath(result.Name);
        Assert.NotNull(zipPath);
        using var zip = ZipFile.OpenRead(zipPath);
        Assert.NotNull(zip.GetEntry("menus/2026-12.jpg"));

        // 解壓出 app.db，確認資料在裡面
        var restored = Path.Combine(_root, "restored.db");
        zip.GetEntry("app.db")!.ExtractToFile(restored);
        await using var connection = new SqliteConnection($"Data Source={restored}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Users WHERE Username = 'backup-test'";
        Assert.Equal(1L, await command.ExecuteScalarAsync());

        Assert.Null(_backup.LastError);
        Assert.Single(_backup.List());
        // 不留暫存檔
        Assert.Single(Directory.GetFiles(_paths.BackupDirectory));
    }

    [Fact]
    public async Task 超過保留天數的備份會被刪除()
    {
        Directory.CreateDirectory(_paths.BackupDirectory);
        var old = Path.Combine(_paths.BackupDirectory,
            $"backup-{AppTime.Now.AddDays(-(BackupService.RetentionDays + 1)):yyyyMMdd-HHmmss}.zip");
        var recent = Path.Combine(_paths.BackupDirectory,
            $"backup-{AppTime.Now.AddDays(-1):yyyyMMdd-HHmmss}.zip");
        var other = Path.Combine(_paths.BackupDirectory, "notes.txt");
        File.WriteAllText(old, "");
        File.WriteAllText(recent, "");
        File.WriteAllText(other, "");

        await _backup.RunAsync();

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
        Assert.True(File.Exists(other));
        Assert.Equal(2, _backup.List().Count);
    }

    [Theory]
    [InlineData("../data/app.db")]
    [InlineData("..%2Fapp.db")]
    [InlineData("app.db")]
    [InlineData("backup-20260101-000000.db")]
    [InlineData("backup-not-a-date.zip")]
    public void 下載_只接受備份檔名(string name)
    {
        Assert.Null(_backup.GetPath(name));
    }

    private sealed class FakeEnv(string root) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = Path.Combine(root, "wwwroot");
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "test";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Test";
    }
}
