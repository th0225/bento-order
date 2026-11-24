using bento_order.Models;
using bento_order.Services;

namespace BentoOrder.Tests;

public class OrderLockPolicyTests
{
    // 2026-11-25 是星期三
    private static readonly DateTime Wednesday = new(2026, 11, 25);
    private static readonly DateTime Saturday = new(2026, 11, 28);

    [Fact]
    public void 平日未截止_開放()
    {
        var now = Wednesday.AddHours(8).AddMinutes(59);
        Assert.False(OrderLockPolicy.IsLocked(Wednesday, now, null));
    }

    [Fact]
    public void 當天九點後_鎖定()
    {
        var now = Wednesday.AddHours(9);
        Assert.True(OrderLockPolicy.IsLocked(Wednesday, now, null));
    }

    [Fact]
    public void 過去日期_鎖定_即使管理員解鎖()
    {
        var now = Wednesday.AddDays(1).AddHours(8);
        var unlocked = new LockedDate { Date = Wednesday, IsLocked = false };
        Assert.True(OrderLockPolicy.IsLocked(Wednesday, now, unlocked));
    }

    [Fact]
    public void 當天九點後_管理員解鎖也無效()
    {
        var now = Wednesday.AddHours(10);
        var unlocked = new LockedDate { Date = Wednesday, IsLocked = false };
        Assert.True(OrderLockPolicy.IsLocked(Wednesday, now, unlocked));
    }

    [Fact]
    public void 週末_預設鎖定()
    {
        var now = Wednesday.AddHours(8);
        Assert.True(OrderLockPolicy.IsLocked(Saturday, now, null));
    }

    [Fact]
    public void 週末_管理員解鎖為補班日_開放()
    {
        var now = Wednesday.AddHours(8);
        var unlocked = new LockedDate { Date = Saturday, IsLocked = false };
        Assert.False(OrderLockPolicy.IsLocked(Saturday, now, unlocked));
    }

    [Fact]
    public void 平日_管理員鎖定_鎖定()
    {
        var now = Wednesday.AddHours(8);
        var locked = new LockedDate { Date = Wednesday.AddDays(1), IsLocked = true };
        Assert.True(OrderLockPolicy.IsLocked(Wednesday.AddDays(1), now, locked));
    }
}
