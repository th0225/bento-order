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
    public DbSet<SystemConfig> SystemConfig => Set<SystemConfig>();
    public DbSet<LockedDate> LockedDates => Set<LockedDate>();
    public DbSet<MealType> MealTypes => Set<MealType>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 每人每天只能有一筆訂單（也用於加速查詢）
        modelBuilder.Entity<Order>()
            .HasIndex(o => new { o.UserId, o.OrderDate })
            .IsUnique();

        // 設定鍵、鎖定日期不可重複
        modelBuilder.Entity<SystemConfig>()
            .HasIndex(c => c.Key)
            .IsUnique();
        modelBuilder.Entity<LockedDate>()
            .HasIndex(d => d.Date)
            .IsUnique();

        // 餐點種類名稱不可重複
        modelBuilder.Entity<MealType>()
            .HasIndex(m => m.Name)
            .IsUnique();

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
                Role = Roles.Admin,
                // 預設密碼只能使用一次，登入後必須修改
                MustChangePassword = true
            }
        );
    }
}