using bento_order.Models;
using bento_order.Services;
using Microsoft.EntityFrameworkCore;

namespace BentoOrder.Tests;

public class BentoDbServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly BentoDbService _service;

    public BentoDbServiceTests()
    {
        _service = _db.CreateService();
    }

    public void Dispose() => _db.Dispose();

    // 找一個「一般使用者現在可以訂」的日期：至少兩天後的平日
    private static DateTime NextOpenWeekday()
    {
        var date = AppTime.Today.AddDays(2);
        while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            date = date.AddDays(1);
        }
        return date;
    }

    private async Task<User> AddUserAsync(string username)
    {
        var user = new User
        {
            Username = username,
            PasswordHash = "secret123",
            RealName = username
        };
        Assert.True(await _service.AddUserAsync(user));
        return user;
    }

    private static Order NewOrder(int userId, DateTime date, string meal,
        string addMeal = "") => new()
    {
        UserId = userId,
        OrderDate = date,
        BentoItem = new() { Name = meal },
        AdditionalBentoItem = new() { Name = addMeal }
    };

    [Fact]
    public async Task Upsert_同一天重複儲存_只有一筆並更新內容()
    {
        var user = await AddUserAsync("u1");
        var date = NextOpenWeekday();

        await _service.UpsertOrderAsync(NewOrder(user.Id, date, "A餐"), false);
        await _service.UpsertOrderAsync(
            NewOrder(user.Id, date.AddHours(10), "B餐", "素食"), false);

        using var db = _db.CreateDbContext();
        var orders = await db.Orders.Where(o => o.UserId == user.Id)
            .ToListAsync();
        var order = Assert.Single(orders);
        Assert.Equal(date, order.OrderDate);
        Assert.Equal("B餐", order.BentoItem!.Name);
        Assert.Equal("素食", order.AdditionalBentoItem!.Name);
    }

    [Fact]
    public async Task Upsert_一般使用者改過去日期_拒絕_管理員可以()
    {
        var user = await AddUserAsync("u1");
        var yesterday = AppTime.Today.AddDays(-1);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.UpsertOrderAsync(NewOrder(user.Id, yesterday, "A餐"),
                isAdmin: false));

        await _service.UpsertOrderAsync(NewOrder(user.Id, yesterday, "A餐"),
            isAdmin: true);
        Assert.NotNull(await _service.GetOrderByUserAsync(user.Id, yesterday));
    }

    [Fact]
    public async Task Upsert_不存在的使用者_拒絕()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpsertOrderAsync(
                NewOrder(999, NextOpenWeekday(), "A餐"), isAdmin: true));
    }

    [Fact]
    public async Task 資料庫_同一人同一天_不能有兩筆訂單()
    {
        var user = await AddUserAsync("u1");
        var date = NextOpenWeekday();

        using var db = _db.CreateDbContext();
        db.Orders.Add(NewOrder(user.Id, date, "A餐"));
        db.Orders.Add(NewOrder(user.Id, date, "B餐"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task 刪除使用者_刪除今天以後的訂單_保留過去訂單()
    {
        var user = await AddUserAsync("u1");
        var past = AppTime.Today.AddDays(-3);
        var future = NextOpenWeekday();

        await _service.UpsertOrderAsync(NewOrder(user.Id, past, "A餐"), true);
        await _service.UpsertOrderAsync(NewOrder(user.Id, future, "A餐"), true);

        await _service.DeleteUserAsync(user.Id);

        Assert.Null(await _service.GetUserAsync(user.Id));
        Assert.NotNull(await _service.GetOrderByUserAsync(user.Id, past));
        Assert.Null(await _service.GetOrderByUserAsync(user.Id, future));
    }

    [Fact]
    public async Task 統計_每日數量與期間總數()
    {
        var u1 = await AddUserAsync("u1");
        var u2 = await AddUserAsync("u2");
        var date = NextOpenWeekday();

        await _service.UpsertOrderAsync(NewOrder(u1.Id, date, "A餐", "B餐"), true);
        await _service.UpsertOrderAsync(NewOrder(u2.Id, date, "A餐"), true);
        await _service.UpsertOrderAsync(
            NewOrder(u2.Id, date.AddDays(1), "素食"), true);

        var stats = await _service.GetDailyStatsAsync(date);
        Assert.Equal(2, stats.Single(s => s.MealName == "A餐").Count);
        Assert.Equal(1, stats.Single(s => s.MealName == "B餐").Count);

        Assert.Equal(3, await _service.GetPeriodTotalCountAsync(date, date));
        Assert.Equal(4, await _service.GetPeriodTotalCountAsync(
            date, date.AddDays(1)));
    }

    [Fact]
    public async Task 每日明細_清空餐點視為未訂()
    {
        var user = await AddUserAsync("u1");
        var date = NextOpenWeekday();

        await _service.UpsertOrderAsync(NewOrder(user.Id, date, "A餐"), true);
        Assert.True((await _service.GetDailyDetailAsync(date))
            .Single(d => d.UserId == user.Id).HasOrdered);

        await _service.UpsertOrderAsync(NewOrder(user.Id, date, ""), true);
        Assert.False((await _service.GetDailyDetailAsync(date))
            .Single(d => d.UserId == user.Id).HasOrdered);
    }

    [Fact]
    public async Task 預設管理員_必須改密碼_改完後解除()
    {
        var admin = await _service.LoginAsync("admin", "admin");
        Assert.NotNull(admin);
        Assert.True(admin.MustChangePassword);

        Assert.Equal("目前密碼錯誤",
            await _service.ChangePasswordAsync(admin.Id, "wrong", "newpass1"));
        Assert.Null(
            await _service.ChangePasswordAsync(admin.Id, "admin", "newpass1"));

        Assert.Null(await _service.LoginAsync("admin", "admin"));
        var relogin = await _service.LoginAsync("admin", "newpass1");
        Assert.NotNull(relogin);
        Assert.False(relogin.MustChangePassword);
    }

    [Fact]
    public async Task 新增使用者與重設密碼_不強制改密碼()
    {
        var user = await AddUserAsync("u1");
        Assert.False((await _service.GetUserAsync(user.Id))!.MustChangePassword);

        Assert.Null(await _service.ResetPasswordAsync(user.Id, "reset123"));
        var login = await _service.LoginAsync("u1", "reset123");
        Assert.NotNull(login);
        Assert.False(login.MustChangePassword);
    }

    [Fact]
    public async Task 鎖定日期與設定_重複儲存只會更新()
    {
        var date = NextOpenWeekday();
        await _service.SaveLockedDateAsync(new LockedDate { Date = date, IsLocked = true });
        await _service.SaveLockedDateAsync(new LockedDate { Date = date, IsLocked = false });

        var locks = await _service.GetLockedDatesAsync(date.Year, date.Month);
        Assert.False(Assert.Single(locks, d => d.Date == date).IsLocked);

        await _service.SaveConfigAsync(new SystemConfig { Key = ConfigKeys.LineId, Value = "a" });
        await _service.SaveConfigAsync(new SystemConfig { Key = ConfigKeys.LineId, Value = "b" });
        Assert.Equal("b", await _service.GetConfigValueAsync(ConfigKeys.LineId));
    }
}
