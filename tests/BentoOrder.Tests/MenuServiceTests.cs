using bento_order.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace BentoOrder.Tests;

public class MenuServiceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("bento-menu-").FullName;
    private readonly AppPaths _paths;
    private readonly MenuService _menus;

    public MenuServiceTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Data Source=database/app.db"
            })
            .Build();
        _paths = new AppPaths(config, new FakeEnv(_root));
        _menus = new MenuService(_paths, NullLogger<MenuService>.Instance);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static MemoryStream Jpeg(byte b) => new([0xFF, 0xD8, b]);

    [Fact]
    public async Task 每月各自存放_互不覆蓋()
    {
        var nov = new DateTime(2026, 11, 1);
        var dec = new DateTime(2026, 12, 1);

        await _menus.UploadAsync(nov, Jpeg(1));
        await _menus.UploadAsync(dec, Jpeg(2));

        Assert.True(_menus.Exists(nov));
        Assert.True(_menus.Exists(dec));
        Assert.False(_menus.Exists(new DateTime(2027, 1, 1)));
        Assert.Equal(1, File.ReadAllBytes(_paths.MenuFilePath(nov))[2]);
        Assert.Equal(2, File.ReadAllBytes(_paths.MenuFilePath(dec))[2]);
        Assert.Equal([dec, nov], _menus.GetMonthsWithMenu());

        // 菜單與資料庫放在一起
        Assert.Equal(Path.Combine(_root, "database", "menus"), _paths.MenuDirectory);

        _menus.Delete(nov);
        Assert.False(_menus.Exists(nov));
        Assert.True(_menus.Exists(dec));
    }

    [Theory]
    [InlineData(2026, 11, 19, 2026, 11)]
    [InlineData(2026, 11, 20, 2026, 12)]
    [InlineData(2026, 12, 28, 2027, 1)]
    public void 預設月份_20日以後算下個月(int y, int m, int d, int ey, int em)
    {
        Assert.Equal(new DateTime(ey, em, 1),
            MenuService.DefaultMenuMonth(new DateTime(y, m, d)));
    }

    [Fact]
    public void 舊版單一菜單_依上傳日期搬到對應月份()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_paths.LegacyMenuFilePath)!);
        File.WriteAllBytes(_paths.LegacyMenuFilePath, [0xFF, 0xD8, 9]);
        File.SetLastWriteTime(_paths.LegacyMenuFilePath, new DateTime(2026, 11, 28, 10, 0, 0));

        _menus.MigrateLegacyMenu();

        Assert.False(File.Exists(_paths.LegacyMenuFilePath));
        Assert.True(_menus.Exists(new DateTime(2026, 12, 1)));
    }

    [Fact]
    public async Task 舊版菜單_目標月份已有菜單時不動()
    {
        var dec = new DateTime(2026, 12, 1);
        await _menus.UploadAsync(dec, Jpeg(1));
        Directory.CreateDirectory(Path.GetDirectoryName(_paths.LegacyMenuFilePath)!);
        File.WriteAllBytes(_paths.LegacyMenuFilePath, [0xFF, 0xD8, 9]);
        File.SetLastWriteTime(_paths.LegacyMenuFilePath, new DateTime(2026, 11, 28));

        _menus.MigrateLegacyMenu();

        Assert.True(File.Exists(_paths.LegacyMenuFilePath));
        Assert.Equal(1, File.ReadAllBytes(_paths.MenuFilePath(dec))[2]);
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
