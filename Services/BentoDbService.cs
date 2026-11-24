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
        if (user != null && user.Role != "Admin")
        {
            // 一併刪除今天以後的訂單，避免離職者的訂單繼續計入統計；
            // 過去的訂單保留，以免已結算的歷史數量改變
            var futureOrders = await db.Orders
                .Where(o => o.UserId == userId &&
                    o.OrderDate >= DateTime.Today)
                .ToListAsync();
            db.Orders.RemoveRange(futureOrders);

            db.Users.Remove(user);
            await db.SaveChangesAsync();
        }
    }


    // --- 餐點訂購相關 ---
    public async Task UpsertOrderAsync(Order order, bool isAdmin)
    {
        var now = DateTime.Now;
        var orderDate = order.OrderDate.Date;
        order.OrderDate = orderDate;
        using var db = _dbFactory.CreateDbContext();

        if (!await db.Users.AnyAsync(u => u.Id == order.UserId))
        {
            throw new InvalidOperationException("使用者不存在。");
        }

        if (!isAdmin)
        {
            var lockRecord = await db.LockedDates.FirstOrDefaultAsync(d =>
                d.Date >= orderDate && d.Date < orderDate.AddDays(1)
            );

            if (OrderLockPolicy.IsLocked(orderDate, now, lockRecord))
            {
                throw new UnauthorizedAccessException("此日期已截止或鎖定，無法修改訂單。");
            }
        }

        // 同時比對日期和使用者確認資料是否存在
        var existingOrder = await db.Orders
            .FirstOrDefaultAsync(o => o.OrderDate == orderDate
                && o.UserId == order.UserId);

        if (existingOrder == null)
        {
            // 新增資料
            db.Orders.Add(order);
        }
        else
        {
            existingOrder.BentoItem ??= new();
            existingOrder.BentoItem.Name =
                order.BentoItem?.Name ?? string.Empty;
            existingOrder.BentoItem.Option =
                order.BentoItem?.Option ?? string.Empty;

            existingOrder.AdditionalBentoItem ??= new();
            existingOrder.AdditionalBentoItem.Name =
                order.AdditionalBentoItem?.Name ?? string.Empty;
            existingOrder.AdditionalBentoItem.Option =
                order.AdditionalBentoItem?.Option ?? string.Empty;

            db.Orders.Update(existingOrder);

            // 更新資料
            // db.Entry(existingOrder).CurrentValues.SetValues(order);

            // if (order.BentoItem != null)
            // {
            //     db.Entry(existingOrder.BentoItem!)
            //         .CurrentValues.SetValues(order.BentoItem);
            // }

            // if (order.AdditionalBentoItem != null)
            // {
            //     db.Entry(existingOrder.AdditionalBentoItem!)
            //         .CurrentValues.SetValues(order.AdditionalBentoItem);
            // }
        }

        // 儲存
        await db.SaveChangesAsync();
    }

    public async Task<Order?> GetOrderByUserAsync(int UserId, DateTime date)
    {
        using var db = _dbFactory.CreateDbContext();
        var order = await db.Orders
            .FirstOrDefaultAsync(o => o.UserId == UserId &&
                o.OrderDate.Year == date.Year &&
                o.OrderDate.Month == date.Month &&
                o.OrderDate.Day == date.Day);
        return order;
    }

    public async Task<List<Order>> GetOrdersByMonthUserAsync(
        int year, int month, int userId)
    {
        using var db = _dbFactory.CreateDbContext();
        return await db.Orders
            .Where(o => o.OrderDate.Year == year &&
                o.OrderDate.Month == month &&
                o.UserId == userId).ToListAsync();
    }

    // 取得每日各項餐點統計(A餐x1, B餐x2,...)
    public async Task<List<OrderCount>> GetDailyStatsAsync(DateTime date)
    {
        using var db = _dbFactory.CreateDbContext();

        var orders = await db.Orders
            .Where(o => o.OrderDate.Date == date.Date)
            .ToListAsync();

        var stats = orders
            .SelectMany(o => new[] { o.BentoItem, o.AdditionalBentoItem })
            .Where(i => i != null && !string.IsNullOrEmpty(i.Name))
            .Select(i => i!.Name)
            .GroupBy(name => name)
            .Select(g => new OrderCount
            {
                MealName = g.Key,
                Count = g.Count()
            })
            .OrderBy(x => x.MealName)
            .ToList();

        return stats;
    }

    public async Task<int> GetPeriodTotalCountAsync(
        DateTime start, DateTime end)
    {
        using var db = _dbFactory.CreateDbContext();

        var orders = await db.Orders
            .Where(o => o.OrderDate >= start.Date &&
                o.OrderDate.Date <= end.Date)
            .ToListAsync();
        
        int total = orders.Sum(o =>
            (o.BentoItem != null && !string.IsNullOrEmpty(o.BentoItem.Name) ?
                1 : 0) +
            (o.AdditionalBentoItem != null &&
                !string.IsNullOrEmpty(o.AdditionalBentoItem.Name) ?
                1 : 0)
        );

        return total;
    }

    public async Task<List<UserOrderDisplay>> GetDailyDetailAsync(
        DateTime date)
    {
        using var db = _dbFactory.CreateDbContext();

        // 取得所有使用者
        var users = await db.Users
            .Where(u => u.Role != "Admin")
            .ToListAsync();

        // 取得當天的所有訂單
        var orders = await db.Orders
            .Where(o => o.OrderDate.Date == date.Date)
            .ToListAsync();

        var result = users.Select(u => {
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
                HasOrdered = (order != null) &&
                    !(order?.BentoItem?.Name == string.Empty &&
                        order?.AdditionalBentoItem?.Name == string.Empty)
            };
        }).ToList();

        return result;
    }


    // --- 系統設定相關 ---
    public async Task SaveConfigAsync(SystemConfig config)
    {
        using var db = _dbFactory.CreateDbContext();

        var _config = db.SystemConfig.FirstOrDefault(
            c => c.Key == config.Key);
        
        if (_config == null)
        {
            db.SystemConfig.Add(config);
        }
        else
        {
            _config.Value = config.Value;
        }

        await db.SaveChangesAsync();
    }

    public async Task<List<SystemConfig>> GetConfigAsync()
    {
        using var db = _dbFactory.CreateDbContext();

        return await db.SystemConfig.ToListAsync();
    }


    // --- 日期鎖定設定相關 ---
    public async Task SaveLockedDateAsync(LockedDate lockedDate)
    {
        using var db = _dbFactory.CreateDbContext();

        var _lockedDate = db.LockedDates.FirstOrDefault(
            c => c.Date == lockedDate.Date);
        
        if (_lockedDate == null)
        {
            db.LockedDates.Add(lockedDate);
        }
        else
        {
            _lockedDate.IsLocked = lockedDate.IsLocked;
        }

        await db.SaveChangesAsync();
    }

    public async Task<List<LockedDate>> GetLockedDatesAsync()
    {
        using var db = _dbFactory.CreateDbContext();

        return await db.LockedDates.ToListAsync();
    }
}
