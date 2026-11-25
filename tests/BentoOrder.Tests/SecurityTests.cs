using System.Security.Claims;
using bento_order.Models;
using bento_order.Services;
using static bento_order.Services.BentoDbService;

namespace BentoOrder.Tests;

public class SecurityTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly BentoDbService _service;

    public SecurityTests()
    {
        _service = _db.CreateService();
    }

    public void Dispose() => _db.Dispose();

    private async Task<User> AddUserAsync(string password = "secret123")
    {
        var user = new User { Username = "u1", PasswordHash = password };
        Assert.Null(await _service.AddUserAsync(user));
        return user;
    }

    // 模擬登入 cookie 中的 claims
    private static ClaimsPrincipal Principal(int userId, string? stamp)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        if (stamp != null)
        {
            claims.Add(new(RevalidatingAuthStateProvider.SecurityStampClaim, stamp));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    [Fact]
    public async Task 連續失敗5次_帳號鎖定_正確密碼也無法登入()
    {
        await AddUserAsync();

        for (var i = 1; i < MaxFailedLogins; i++)
        {
            Assert.Equal(LoginStatus.Invalid, (await _service.LoginAsync("u1", "wrong")).Status);
        }
        Assert.Equal(LoginStatus.LockedOut, (await _service.LoginAsync("u1", "wrong")).Status);
        Assert.Equal(LoginStatus.LockedOut, (await _service.LoginAsync("u1", "secret123")).Status);
    }

    [Fact]
    public async Task 登入成功_失敗次數歸零()
    {
        await AddUserAsync();

        for (var i = 1; i < MaxFailedLogins; i++)
        {
            await _service.LoginAsync("u1", "wrong");
        }
        Assert.Equal(LoginStatus.Success, (await _service.LoginAsync("u1", "secret123")).Status);

        // 歸零後要再錯滿 5 次才會鎖定
        for (var i = 1; i < MaxFailedLogins; i++)
        {
            Assert.Equal(LoginStatus.Invalid, (await _service.LoginAsync("u1", "wrong")).Status);
        }
    }

    [Fact]
    public async Task 管理員重設密碼_解除鎖定()
    {
        var user = await AddUserAsync();
        for (var i = 0; i < MaxFailedLogins; i++)
        {
            await _service.LoginAsync("u1", "wrong");
        }

        Assert.Null(await _service.ResetPasswordAsync(user.Id, "newpass12"));

        Assert.Equal(LoginStatus.Success, (await _service.LoginAsync("u1", "newpass12")).Status);
    }

    [Fact]
    public async Task 不存在的帳號_回傳一般錯誤()
    {
        Assert.Equal(LoginStatus.Invalid, (await _service.LoginAsync("nobody", "x")).Status);
    }

    [Fact]
    public async Task 改密碼後_舊的登入失效_新登入有效()
    {
        var user = await AddUserAsync();
        var oldLogin = (await _service.LoginAsync("u1", "secret123")).User!;
        var oldCookie = Principal(user.Id, oldLogin.SecurityStamp);
        Assert.True(await RevalidatingAuthStateProvider.IsValidAsync(oldCookie, _service));

        Assert.Null(await _service.ChangePasswordAsync(user.Id, "secret123", "newpass12"));

        Assert.False(await RevalidatingAuthStateProvider.IsValidAsync(oldCookie, _service));
        var newLogin = (await _service.LoginAsync("u1", "newpass12")).User!;
        Assert.True(await RevalidatingAuthStateProvider.IsValidAsync(
            Principal(user.Id, newLogin.SecurityStamp), _service));
    }

    [Fact]
    public async Task 重設密碼後_舊的登入失效()
    {
        var user = await AddUserAsync();
        var login = (await _service.LoginAsync("u1", "secret123")).User!;
        var cookie = Principal(user.Id, login.SecurityStamp);

        await _service.ResetPasswordAsync(user.Id, "newpass12");

        Assert.False(await RevalidatingAuthStateProvider.IsValidAsync(cookie, _service));
    }

    [Fact]
    public async Task 升級前的cookie沒有戳記_尚未改過密碼的帳號仍有效()
    {
        // 預設 admin 的戳記是空字串（migration 前就存在的帳號也是）
        Assert.True(await RevalidatingAuthStateProvider.IsValidAsync(Principal(1, null), _service));

        // 改過密碼後，沒有戳記的舊 cookie 就失效
        await _service.ResetPasswordAsync(1, "newpass12");
        Assert.False(await RevalidatingAuthStateProvider.IsValidAsync(Principal(1, null), _service));
    }

    [Fact]
    public async Task 使用者被刪除_登入失效()
    {
        var user = await AddUserAsync();
        var login = (await _service.LoginAsync("u1", "secret123")).User!;
        await _service.DeleteUserAsync(user.Id);

        Assert.False(await RevalidatingAuthStateProvider.IsValidAsync(
            Principal(user.Id, login.SecurityStamp), _service));
    }

    [Fact]
    public async Task 新密碼至少8個字元()
    {
        Assert.NotNull(await _service.AddUserAsync(new User { Username = "u1", PasswordHash = "short12" }));
        var user = await AddUserAsync("longenough");
        Assert.NotNull(await _service.ResetPasswordAsync(user.Id, "short12"));
        Assert.NotNull(await _service.ChangePasswordAsync(user.Id, "longenough", "short12"));
    }

    [Fact]
    public async Task 新增使用者_帳號重複或空白_拒絕()
    {
        await AddUserAsync();
        Assert.Equal("帳號已存在", await _service.AddUserAsync(
            new User { Username = " u1 ", PasswordHash = "secret123" }));
        Assert.NotNull(await _service.AddUserAsync(
            new User { Username = "  ", PasswordHash = "secret123" }));
    }
}
