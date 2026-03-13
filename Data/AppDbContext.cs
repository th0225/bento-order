using Microsoft.EntityFrameworkCore;
using bento_order.Models;

namespace bento_order.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : 
        base(options) {}

    // 註冊資料表
    public DbSet<User> Users => Set<User>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<MonthlyOrder> MonthlyMenus => Set<MonthlyOrder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Username為唯一索引
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Username)
            .IsUnique();

        // 建立管理員帳號
        modelBuilder.Entity<User>().HasData(
            new User {
                Id = 1, 
                Username = "admin",
                PasswordHash = "admin",
                Role = "Admin"
            }
        );
    }
}