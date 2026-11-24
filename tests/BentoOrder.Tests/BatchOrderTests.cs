using bento_order.Models;
using bento_order.Services;

namespace BentoOrder.Tests;

public class BatchOrderTests : IDisposable
{
    private static readonly DayOfWeek[] Weekdays =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
        DayOfWeek.Thursday, DayOfWeek.Friday
    ];

    private readonly TestDb _db = new();
    private readonly BentoDbService _service;
    // 下個月：每一天都還沒截止
    private readonly DateTime _month;

    public BatchOrderTests()
    {
        _service = _db.CreateService();
        var next = AppTime.Today.AddMonths(1);
        _month = new DateTime(next.Year, next.Month, 1);
    }

    public void Dispose() => _db.Dispose();

    private async Task<int> AddUserAsync()
    {
        var user = new User { Username = "u1", PasswordHash = "secret123" };
        await _service.AddUserAsync(user);
        return user.Id;
    }

    private IEnumerable<DateTime> DaysOfMonth() =>
        Enumerable.Range(0, DateTime.DaysInMonth(_month.Year, _month.Month))
            .Select(i => _month.AddDays(i));

    private Task<BentoDbService.BatchOrderResult> BatchAsync(int userId,
        DayOfWeek[] days, string meal, string addMeal = "",
        bool overwrite = false) =>
        _service.BatchUpsertOrdersAsync(userId, _month.Year, _month.Month,
            days, meal, addMeal, overwrite);

    [Fact]
    public async Task 套用到所有平日_週末略過()
    {
        var userId = await AddUserAsync();

        var result = await BatchAsync(userId,
            [.. Weekdays, DayOfWeek.Saturday, DayOfWeek.Sunday], "A餐", "B餐");

        var weekdayCount = DaysOfMonth().Count(d => Weekdays.Contains(d.DayOfWeek));
        var weekendCount = DaysOfMonth().Count() - weekdayCount;
        Assert.Equal(weekdayCount, result.Applied);
        Assert.Equal(weekendCount, result.SkippedLocked);

        var orders = await _service.GetOrdersByMonthUserAsync(
            _month.Year, _month.Month, userId);
        Assert.Equal(weekdayCount, orders.Count);
        Assert.All(orders, o =>
        {
            Assert.Equal("A餐", o.BentoItem!.Name);
            Assert.Equal("B餐", o.AdditionalBentoItem!.Name);
        });
    }

    [Fact]
    public async Task 只套用指定的星期幾()
    {
        var userId = await AddUserAsync();

        var result = await BatchAsync(userId,
            [DayOfWeek.Monday, DayOfWeek.Wednesday], "素食");

        var expected = DaysOfMonth().Count(d =>
            d.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Wednesday);
        Assert.Equal(expected, result.Applied);
        var orders = await _service.GetOrdersByMonthUserAsync(
            _month.Year, _month.Month, userId);
        Assert.All(orders, o => Assert.Contains(o.OrderDate.DayOfWeek,
            new[] { DayOfWeek.Monday, DayOfWeek.Wednesday }));
    }

    [Fact]
    public async Task 不覆蓋已訂的日子_除非勾選覆蓋()
    {
        var userId = await AddUserAsync();
        var firstWeekday = DaysOfMonth().First(d => Weekdays.Contains(d.DayOfWeek));
        await _service.UpsertOrderAsync(new Order
        {
            UserId = userId,
            OrderDate = firstWeekday,
            BentoItem = new() { Name = "合菜" },
            AdditionalBentoItem = new()
        }, isAdmin: false);

        var result = await BatchAsync(userId, Weekdays, "A餐");
        Assert.Equal(1, result.SkippedExisting);
        Assert.Equal("合菜",
            (await _service.GetOrderByUserAsync(userId, firstWeekday))!.BentoItem!.Name);

        result = await BatchAsync(userId, Weekdays, "A餐", overwrite: true);
        Assert.Equal(0, result.SkippedExisting);
        Assert.Equal("A餐",
            (await _service.GetOrderByUserAsync(userId, firstWeekday))!.BentoItem!.Name);
    }

    [Fact]
    public async Task 鎖定日略過_開放的週末補班日會套用()
    {
        var userId = await AddUserAsync();
        var firstWeekday = DaysOfMonth().First(d => Weekdays.Contains(d.DayOfWeek));
        var firstSaturday = DaysOfMonth().First(d => d.DayOfWeek == DayOfWeek.Saturday);
        await _service.SaveLockedDateAsync(new LockedDate { Date = firstWeekday, IsLocked = true });
        await _service.SaveLockedDateAsync(new LockedDate { Date = firstSaturday, IsLocked = false });

        await BatchAsync(userId, [.. Weekdays, DayOfWeek.Saturday], "A餐");

        Assert.Null(await _service.GetOrderByUserAsync(userId, firstWeekday));
        Assert.NotNull(await _service.GetOrderByUserAsync(userId, firstSaturday));
    }

    [Fact]
    public async Task 過去日期不會被套用()
    {
        var userId = await AddUserAsync();
        var today = AppTime.Today;

        await _service.BatchUpsertOrdersAsync(userId, today.Year, today.Month,
            [.. Weekdays], "A餐", "", overwrite: true);

        var orders = await _service.GetOrdersByMonthUserAsync(
            today.Year, today.Month, userId);
        Assert.All(orders, o => Assert.True(o.OrderDate >= today));
    }

    [Fact]
    public async Task 餐點不存在或未選_拒絕()
    {
        var userId = await AddUserAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BatchAsync(userId, Weekdays, "不存在的餐"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BatchAsync(userId, Weekdays, ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BatchAsync(userId, Weekdays, "A餐", "不存在的餐"));
    }
}
