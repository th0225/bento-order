using bento_order.Data;
using bento_order.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BentoOrder.Tests;

// 每個測試一個記憶體內的 SQLite 資料庫，並套用所有 migration（同時驗證 migration 可執行）
public sealed class TestDb : IDisposable, IDbContextFactory<AppDbContext>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public TestDb()
    {
        _connection.Open();
        using var db = CreateDbContext();
        db.Database.Migrate();
    }

    public AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options);

    public BentoDbService CreateService() => new(this);

    public void Dispose() => _connection.Dispose();
}
