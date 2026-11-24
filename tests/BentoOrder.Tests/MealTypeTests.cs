using bento_order.Models;
using bento_order.Services;

namespace BentoOrder.Tests;

public class MealTypeTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly BentoDbService _service;

    public MealTypeTests()
    {
        _service = _db.CreateService();
    }

    public void Dispose() => _db.Dispose();

    private async Task<List<string>> NamesAsync() =>
        (await _service.GetMealTypesAsync()).Select(m => m.Name).ToList();

    private async Task<int> IdOfAsync(string name) =>
        (await _service.GetMealTypesAsync()).Single(m => m.Name == name).Id;

    private async Task<int> AddUserAsync()
    {
        var user = new User { Username = "u1", PasswordHash = "secret123" };
        await _service.AddUserAsync(user);
        return user.Id;
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
    public async Task Migration_帶入原本的四種餐點()
    {
        Assert.Equal(["A餐", "B餐", "素食", "合菜"], await NamesAsync());
    }

    [Fact]
    public async Task 新增_排在最後_名稱不可重複或空白()
    {
        Assert.Null(await _service.AddMealTypeAsync(" 咖哩飯 "));
        Assert.Equal("咖哩飯", (await NamesAsync()).Last());

        Assert.NotNull(await _service.AddMealTypeAsync("咖哩飯"));
        Assert.NotNull(await _service.AddMealTypeAsync("  "));
        Assert.NotNull(await _service.AddMealTypeAsync(new string('x', 21)));
    }

    [Fact]
    public async Task 改名_一併更新所有訂單()
    {
        var userId = await AddUserAsync();
        var past = AppTime.Today.AddDays(-3);
        var future = AppTime.Today.AddDays(3);
        await _service.UpsertOrderAsync(NewOrder(userId, past, "A餐", "B餐"), true);
        await _service.UpsertOrderAsync(NewOrder(userId, future, "B餐", "A餐"), true);

        Assert.Null(await _service.RenameMealTypeAsync(
            await IdOfAsync("A餐"), "雞腿飯"));

        var pastOrder = await _service.GetOrderByUserAsync(userId, past);
        var futureOrder = await _service.GetOrderByUserAsync(userId, future);
        Assert.Equal("雞腿飯", pastOrder!.BentoItem!.Name);
        Assert.Equal("B餐", pastOrder.AdditionalBentoItem!.Name);
        Assert.Equal("雞腿飯", futureOrder!.AdditionalBentoItem!.Name);
        Assert.Contains("雞腿飯", await NamesAsync());
        Assert.DoesNotContain("A餐", await NamesAsync());
    }

    [Fact]
    public async Task 改名_不可與其他餐點重複()
    {
        Assert.NotNull(await _service.RenameMealTypeAsync(
            await IdOfAsync("A餐"), "B餐"));
    }

    [Fact]
    public async Task 刪除_今天以後有人訂則拒絕_只有過去訂單則可刪()
    {
        var userId = await AddUserAsync();
        var future = AppTime.Today.AddDays(3);
        await _service.UpsertOrderAsync(NewOrder(userId, future, "", "素食"), true);

        var error = await _service.DeleteMealTypeAsync(await IdOfAsync("素食"));
        Assert.NotNull(error);
        Assert.Contains("1 筆", error);

        // 改成只有過去訂單用到
        await _service.UpsertOrderAsync(NewOrder(userId, future, ""), true);
        await _service.UpsertOrderAsync(
            NewOrder(userId, AppTime.Today.AddDays(-1), "素食"), true);

        Assert.Null(await _service.DeleteMealTypeAsync(await IdOfAsync("素食")));
        Assert.DoesNotContain("素食", await NamesAsync());
        var pastOrder = await _service.GetOrderByUserAsync(userId,
            AppTime.Today.AddDays(-1));
        Assert.Equal("素食", pastOrder!.BentoItem!.Name);
    }

    [Fact]
    public async Task 調整順序()
    {
        await _service.MoveMealTypeAsync(await IdOfAsync("合菜"), -1);
        Assert.Equal(["A餐", "B餐", "合菜", "素食"], await NamesAsync());

        // 第一個不能再往前
        await _service.MoveMealTypeAsync(await IdOfAsync("A餐"), -1);
        Assert.Equal(["A餐", "B餐", "合菜", "素食"], await NamesAsync());
    }

    [Fact]
    public async Task 訂單_不存在的餐點_拒絕_原本的舊餐點可保留()
    {
        var userId = await AddUserAsync();
        var date = AppTime.Today.AddDays(-2);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpsertOrderAsync(NewOrder(userId, date, "不存在的餐"), true));

        // 訂單用了某餐點，之後該餐點被刪除：只改另一個欄位時仍可儲存
        await _service.UpsertOrderAsync(NewOrder(userId, date, "合菜"), true);
        Assert.Null(await _service.DeleteMealTypeAsync(await IdOfAsync("合菜")));
        await _service.UpsertOrderAsync(NewOrder(userId, date, "合菜", "A餐"), true);

        var order = await _service.GetOrderByUserAsync(userId, date);
        Assert.Equal("合菜", order!.BentoItem!.Name);
        Assert.Equal("A餐", order.AdditionalBentoItem!.Name);
    }
}
