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

    public async Task<List<User>> GetUsersAsync()
    {
        using var db = _dbFactory.CreateDbContext();
        return await db.Users.ToListAsync();
    }

    // --- 餐點訂購相關 ---
    public async Task<(bool success, string message)> PlaceOrderAsync(
        Order order)
    {
        using var db = _dbFactory.CreateDbContext();

        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return (true, "訂購成功! ");
    }

    public async Task<List<Order>> GetOrdersByMonthAsync(int year, int month)
    {
        using var db = _dbFactory.CreateDbContext();
        return await db.Orders
            .Where(o => o.OrderDate.Year == year &&
                o.OrderDate.Month == month)
            .Include(o => o.BentoItem)
            .ToListAsync();
    }
}