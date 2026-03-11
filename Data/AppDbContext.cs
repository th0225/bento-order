using Microsoft.EntityFrameworkCore;
using bento_order.Models;

namespace bento_order.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : 
        base(options) {}

    // 註冊資料表
    public DbSet<User> Users => Set<User>();
    public DbSet<BentoItem> BentoItems => Set<BentoItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<MonthlyMenu> MonthlyMenus => Set<MonthlyMenu>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Username為唯一索引
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Username)
            .IsUnique();
        
        // 建立初始資料
        modelBuilder.Entity<BentoItem>().HasData(
            new BentoItem { Id = 1, Name = "A餐"},
            new BentoItem { Id = 2, Name = "B餐"},
            new BentoItem { Id = 3, Name = "素食"},
            new BentoItem { Id = 4, Name = "合菜"}
        );

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