using bento_order.Data;
using bento_order.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

namespace bento_order.Services;

public class BentoDbService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly PasswordHasher<User> _passwordHasher = new();

    public BentoDbService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    // --- 使用者相關 ---
    // 使用者登錄
    public async Task<User?> LoginAsync(string username, string password)
    {
        using var db = _dbFactory.CreateDbContext();
        var user = await db.Users.FirstOrDefaultAsync(
            u => u.Username == username
        );

        if (user == null || !VerifyPassword(user, password))
        {
            return null;
        }

        // 舊的明碼密碼或需要重新雜湊時，順便更新成新的雜湊
        if (user.PasswordHash == password ||
            _passwordHasher.VerifyHashedPassword(
                user, user.PasswordHash, password) ==
                PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _passwordHasher.HashPassword(user, password);
            await db.SaveChangesAsync();
        }

        return user;
    }

    // 驗證密碼（相容早期以明碼儲存的密碼）
    private bool VerifyPassword(User user, string password)
    {
        try
        {
            var result = _passwordHasher.VerifyHashedPassword(
                user, user.PasswordHash, password);

            if (result != PasswordVerificationResult.Failed)
            {
                return true;
            }
        }
        catch (FormatException) {}

        return user.PasswordHash == password;
    }

    // 取得單一使用者
    public async Task<User?> GetUserAsync(int userId)
    {
        using var db = _dbFactory.CreateDbContext();
        return await db.Users.FindAsync(userId);
    }

    // 使用者是否仍存在（被刪除的使用者 cookie 需失效）
    public async Task<bool> UserExistsAsync(int userId)
    {
        using var db = _dbFactory.CreateDbContext();
        return await db.Users.AnyAsync(u => u.Id == userId);
    }

    // 修改自己的密碼，成功回傳 null，失敗回傳錯誤訊息
    public async Task<string?> ChangePasswordAsync(int userId,
        string currentPassword, string newPassword)
    {
        using var db = _dbFactory.CreateDbContext();
        var user = await db.Users.FindAsync(userId);

        if (user == null)
        {
            return "找不到使用者";
        }

        if (!VerifyPassword(user, currentPassword))
        {
            return "目前密碼錯誤";
        }

        if (currentPassword == newPassword)
        {
            return "新密碼不能與目前密碼相同";
        }

        var error = ValidateNewPassword(newPassword);
        if (error != null)
        {
            return error;
        }

        user.PasswordHash = _passwordHasher.HashPassword(user, newPassword);
        user.MustChangePassword = false;
        await db.SaveChangesAsync();

        return null;
    }

    // 管理員重設使用者密碼（不強制使用者修改）
    public async Task<string?> ResetPasswordAsync(int userId,
        string newPassword)
    {
        var error = ValidateNewPassword(newPassword);
        if (error != null)
        {
            return error;
        }

        using var db = _dbFactory.CreateDbContext();
        var user = await db.Users.FindAsync(userId);

        if (user == null)
        {
            return "找不到使用者";
        }

        user.PasswordHash = _passwordHasher.HashPassword(user, newPassword);
        await db.SaveChangesAsync();

        return null;
    }

    public const int MinPasswordLength = 6;

    private static string? ValidateNewPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) ||
            password.Length < MinPasswordLength)
        {
            return $"密碼至少需要 {MinPasswordLength} 個字元";
        }

        return null;
    }

    // 取得使用者資料
    public async Task<List<User>> GetUsersAsync()
    {
        using var db = _dbFactory.CreateDbContext();
        return await db.Users.ToListAsync();
    }

    // 新增使用者
    public async Task<bool> AddUserAsync(User user)
    {
        using var db = _dbFactory.CreateDbContext();
        // 檢查帳號是否重覆
        if (await db.Users.AnyAsync(u => u.Username == user.Username))
        {
            return false;
        }

        user.PasswordHash = _passwordHasher.HashPassword(
            user,
            user.PasswordHash
        );

        db.Users.Add(user);
        await db.SaveChangesAsync();
        
        return true;
    }

    // 刪除使用者
    public async Task DeleteUserAsync(int userId)
    {
        using var db = _dbFactory.CreateDbContext();
        var user = await db.Users.FindAsync(userId);
        if (user != null && user.Role != Roles.Admin)
        {
            // 一併刪除今天以後的訂單，避免離職者的訂單繼續計入統計；
            // 過去的訂單保留，以免已結算的歷史數量改變
            var futureOrders = await db.Orders
                .Where(o => o.UserId == userId &&
                    o.OrderDate >= AppTime.Today)
                .ToListAsync();
            db.Orders.RemoveRange(futureOrders);

            db.Users.Remove(user);
            await db.SaveChangesAsync();
        }
    }


    // --- 餐點訂購相關 ---
    // 新增或更新某人某天的訂單（每人每天一筆）
    public async Task UpsertOrderAsync(Order order, bool isAdmin)
    {
        var orderDate = order.OrderDate.Date;
        order.OrderDate = orderDate;

        using (var db = _dbFactory.CreateDbContext())
        {
            if (!await db.Users.AnyAsync(u => u.Id == order.UserId))
            {
                throw new InvalidOperationException("使用者不存在。");
            }

            if (!isAdmin)
            {
                var lockRecord = await db.LockedDates
                    .FirstOrDefaultAsync(d => d.Date == orderDate);

                if (OrderLockPolicy.IsLocked(orderDate, AppTime.Now,
                    lockRecord))
                {
                    throw new UnauthorizedAccessException(
                        "此日期已截止或鎖定，無法修改訂單。");
                }
            }
        }

        try
        {
            await SaveOrderAsync(order);
        }
        catch (DbUpdateException)
        {
            // 同一人同一天同時新增（例如開兩個分頁），唯一索引衝突；
            // 此時資料已存在，改用更新再試一次
            await SaveOrderAsync(order);
        }
    }

    private async Task SaveOrderAsync(Order order)
    {
        using var db = _dbFactory.CreateDbContext();

        var existingOrder = await db.Orders
            .FirstOrDefaultAsync(o => o.OrderDate == order.OrderDate
                && o.UserId == order.UserId);

        if (existingOrder == null)
        {
            db.Orders.Add(new Order
            {
                UserId = order.UserId,
                OrderDate = order.OrderDate,
                BentoItem = CopyItem(order.BentoItem),
                AdditionalBentoItem = CopyItem(order.AdditionalBentoItem)
            });
        }
        else
        {
            existingOrder.BentoItem ??= new();
            existingOrder.BentoItem.Name = order.BentoItem?.Name ?? string.Empty;
            existingOrder.BentoItem.Option =
                order.BentoItem?.Option ?? string.Empty;

            existingOrder.AdditionalBentoItem ??= new();
            existingOrder.AdditionalBentoItem.Name =
                order.AdditionalBentoItem?.Name ?? string.Empty;
            existingOrder.AdditionalBentoItem.Option =
                order.AdditionalBentoItem?.Option ?? string.Empty;
        }

        await db.SaveChangesAsync();
    }

    private static BentoItem CopyItem(BentoItem? item) => new()
    {
        Name = item?.Name ?? string.Empty,
        Option = item?.Option ?? string.Empty
    };

    public async Task<Order?> GetOrderByUserAsync(int userId, DateTime date)
    {
        using var db = _dbFactory.CreateDbContext();
        return await db.Orders.FirstOrDefaultAsync(o =>
            o.UserId == userId && o.OrderDate == date.Date);
    }

    public async Task<List<Order>> GetOrdersByMonthUserAsync(
        int year, int month, int userId)
    {
        var start = new DateTime(year, month, 1);
        var end = start.AddMonths(1);

        using var db = _dbFactory.CreateDbContext();
        return await db.Orders
            .Where(o => o.UserId == userId &&
                o.OrderDate >= start && o.OrderDate < end)
            .ToListAsync();
    }

    // 取得某一天的所有訂單（日期區間查詢，才能使用索引）
    private static Task<List<Order>> GetOrdersOfDayAsync(AppDbContext db,
        DateTime date)
    {
        var start = date.Date;
        var end = start.AddDays(1);
        return db.Orders
            .Where(o => o.OrderDate >= start && o.OrderDate < end)
            .ToListAsync();
    }

    // 取得每日各項餐點統計(A餐x1, B餐x2,...)
    public async Task<List<OrderCount>> GetDailyStatsAsync(DateTime date)
    {
        using var db = _dbFactory.CreateDbContext();
        var orders = await GetOrdersOfDayAsync(db, date);

        return orders
            .SelectMany(o => new[] { o.BentoItem, o.AdditionalBentoItem })
            .Where(i => !string.IsNullOrEmpty(i?.Name))
            .GroupBy(i => i!.Name)
            .Select(g => new OrderCount
            {
                MealName = g.Key,
                Count = g.Count()
            })
            .OrderBy(x => x.MealName)
            .ToList();
    }

    // 期間內的餐點總數（含起訖日）
    public async Task<int> GetPeriodTotalCountAsync(
        DateTime start, DateTime end)
    {
        var from = start.Date;
        var to = end.Date.AddDays(1);

        using var db = _dbFactory.CreateDbContext();
        var orders = await db.Orders
            .Where(o => o.OrderDate >= from && o.OrderDate < to)
            .ToListAsync();

        return orders.Sum(o => o.MealCount);
    }

    public async Task<List<UserOrderDisplay>> GetDailyDetailAsync(
        DateTime date)
    {
        using var db = _dbFactory.CreateDbContext();

        // 取得所有一般使用者
        var users = await db.Users
            .Where(u => u.Role != Roles.Admin)
            .ToListAsync();

        // 取得當天的所有訂單
        var orders = await GetOrdersOfDayAsync(db, date);

        return users.Select(u =>
        {
            var order = orders.FirstOrDefault(o => o.UserId == u.Id);
            return new UserOrderDisplay
            {
                UserId = u.Id,
                Username = u.RealName,
                OrderDate = date,
                OrderData = order ?? new Order
                {
                    UserId = u.Id,
                    OrderDate = date
                },
                HasOrdered = order?.HasMeal ?? false
            };
        }).ToList();
    }


    // --- 系統設定相關 ---
    public async Task SaveConfigAsync(SystemConfig config)
    {
        using var db = _dbFactory.CreateDbContext();

        var existing = await db.SystemConfig.FirstOrDefaultAsync(
            c => c.Key == config.Key);

        if (existing == null)
        {
            db.SystemConfig.Add(config);
        }
        else
        {
            existing.Value = config.Value;
        }

        await db.SaveChangesAsync();
    }

    public async Task<List<SystemConfig>> GetConfigAsync()
    {
        using var db = _dbFactory.CreateDbContext();

        return await db.SystemConfig.ToListAsync();
    }

    // 取得單一設定值，沒有設定時回傳空字串
    public async Task<string> GetConfigValueAsync(string key)
    {
        using var db = _dbFactory.CreateDbContext();

        var config = await db.SystemConfig.FirstOrDefaultAsync(
            c => c.Key == key);
        return config?.Value ?? string.Empty;
    }


    // --- 日期鎖定設定相關 ---
    public async Task SaveLockedDateAsync(LockedDate lockedDate)
    {
        lockedDate.Date = lockedDate.Date.Date;
        using var db = _dbFactory.CreateDbContext();

        var existing = await db.LockedDates.FirstOrDefaultAsync(
            c => c.Date == lockedDate.Date);

        if (existing == null)
        {
            db.LockedDates.Add(lockedDate);
        }
        else
        {
            existing.IsLocked = lockedDate.IsLocked;
        }

        await db.SaveChangesAsync();
    }

    // 取得某個月份的日期鎖定設定
    public async Task<List<LockedDate>> GetLockedDatesAsync(int year,
        int month)
    {
        var start = new DateTime(year, month, 1);
        var end = start.AddMonths(1);

        using var db = _dbFactory.CreateDbContext();
        return await db.LockedDates
            .Where(d => d.Date >= start && d.Date < end)
            .ToListAsync();
    }
}
