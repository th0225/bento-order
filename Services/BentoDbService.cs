using bento_order.Data;
using bento_order.Models;
using Microsoft.EntityFrameworkCore;

namespace bento_order.Services;

public class BentoDbService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public BentoDbService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    // --- 使用者相關 ---
    // 使用者登錄
    public async Task<User?> LoginAsync(string username, string password)
    {
        using var db = _dbFactory.CreateDbContext();
        return await db.Users.FirstOrDefaultAsync(
            u => u.Username == username && u.PasswordHash == password
        );
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

        db.Users.Add(user);
        await db.SaveChangesAsync();
        
        return true;
    }

    // 刪除使用者
    public async Task DeleteUserAsync(int userId)
    {
        using var db = _dbFactory.CreateDbContext();
        var user = await db.Users.FindAsync(userId);
        if (user != null && user.Role != "admin")
        {
            db.Users.Remove(user);
            await db.SaveChangesAsync();
        }
    }

    // --- 餐點訂購相關 ---
    public async Task UpsertOrderAsync(Order order, bool isAdmin)
    {
        var now = DateTime.Now;

        if (!isAdmin)
        {
            if (order.OrderDate.Date < now.Date ||
                (order.OrderDate.Date == now.Date && now.Hour >= 9))
            {
                throw new UnauthorizedAccessException("您沒有權限在截止後修改訂單。");
            }
        }

        using var db = _dbFactory.CreateDbContext();

        // 同時比對日期和使用者確認資料是否存在
        var existingOrder = await db.Orders
            .FirstOrDefaultAsync(o => o.OrderDate == order.OrderDate
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

            // Console.WriteLine("++++++++++++++++");
            // Console.WriteLine(order.BentoItem.Name);
            // Console.WriteLine(order.BentoItem.Option);
            // Console.WriteLine(order.AdditionalBentoItem.Name);
            // Console.WriteLine(order.AdditionalBentoItem.Option);
            // Console.WriteLine("------------------");

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
        var order = db.Orders
            .FirstOrDefault(o => o.UserId == UserId &&
                o.OrderDate.Month == date.Month && o.OrderDate.Day == date.Day);
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

    public async Task<List<OrderCount>> GetDailyStatsAsync(DateTime date)
    {
        using var db = _dbFactory.CreateDbContext();

        var orders = await db.Orders
            .Where(o => o.OrderDate.Date == date.Date)
            .ToListAsync();

        var stats = orders
            .SelectMany(o => new[] { o.BentoItem, o.AdditionalBentoItem })
            .Where(i => i != null && !string.IsNullOrEmpty(i.Name))
            .GroupBy(i => new {i.Name, i.Option })
            .Select(g => new OrderCount
            {
                MealName = g.Key.Name,
                Option = g.Key.Option,
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
            db.SystemConfig.Add(new SystemConfig
            {
                Key = config!.Key,
                Value = config!.Value
            });
        }
        else
        {
            _config = config;
        }

        await db.SaveChangesAsync();
    }

    public async Task<List<SystemConfig>> GetConfigAsync()
    {
        using var db = _dbFactory.CreateDbContext();

        return await db.SystemConfig.ToListAsync();
    }
}