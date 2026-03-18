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
    public DbSet<SystemConfig> SystemConfig => Set<SystemConfig>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 建立user id + 日期的複合索引，加速查詢
        modelBuilder.Entity<Order>()
            .HasIndex(o => new { o.UserId, o.OrderDate });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.OwnsOne(o => o.BentoItem, b =>
            {
                b.Property(p => p.Name).HasColumnName("BentoName");
                b.Property(p => p.Option).HasColumnName("BentoOption");
            });

            entity.OwnsOne(o => o.AdditionalBentoItem, a =>
            {
                a.Property(p => p.Name).HasColumnName("AddBentoName");
                a.Property(p => p.Option).HasColumnName("AddBentoOption");
            });
        });

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