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

            await ValidateMealNamesAsync(db, order);

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

    // 批次訂餐的結果：套用天數、因截止/鎖定略過、因已有訂單略過
    public record BatchOrderResult(int Applied, int SkippedLocked,
        int SkippedExisting);

    // 批次訂餐：把餐點套用到某月份中指定星期幾的所有日子。
    // 一律依 OrderLockPolicy 略過已截止或鎖定的日子（管理員也一樣，
    // 避免誤改過去的訂單；管理員可個別修改）。
    // overwrite = false 時，已經有訂餐的日子不變。
    public async Task<BatchOrderResult> BatchUpsertOrdersAsync(int userId,
        int year, int month, IReadOnlyCollection<DayOfWeek> daysOfWeek,
        string mealName, string addMealName, bool overwrite)
    {
        mealName = mealName?.Trim() ?? string.Empty;
        addMealName = addMealName?.Trim() ?? string.Empty;

        if (mealName.Length == 0)
        {
            throw new InvalidOperationException("請選擇餐點 1。");
        }

        var start = new DateTime(year, month, 1);
        var end = start.AddMonths(1);

        using var db = _dbFactory.CreateDbContext();

        if (!await db.Users.AnyAsync(u => u.Id == userId))
        {
            throw new InvalidOperationException("使用者不存在。");
        }

        var names = new[] { mealName, addMealName }
            .Where(n => n.Length > 0).Distinct().ToList();
        var validCount = await db.MealTypes.CountAsync(m => names.Contains(m.Name));
        if (validCount != names.Count)
        {
            throw new InvalidOperationException("選擇的餐點不存在，請重新整理頁面。");
        }

        var locks = await db.LockedDates
            .Where(d => d.Date >= start && d.Date < end)
            .ToListAsync();
        var orders = await db.Orders
            .Where(o => o.UserId == userId &&
                o.OrderDate >= start && o.OrderDate < end)
            .ToListAsync();

        var now = AppTime.Now;
        int applied = 0, skippedLocked = 0, skippedExisting = 0;

        for (var date = start; date < end; date = date.AddDays(1))
        {
            if (!daysOfWeek.Contains(date.DayOfWeek))
            {
                continue;
            }

            var lockRecord = locks.FirstOrDefault(d => d.Date == date);
            if (OrderLockPolicy.IsLocked(date, now, lockRecord))
            {
                skippedLocked++;
                continue;
            }

            var existing = orders.FirstOrDefault(o => o.OrderDate == date);
            if (existing != null && existing.HasMeal && !overwrite)
            {
                skippedExisting++;
                continue;
            }

            if (existing == null)
            {
                db.Orders.Add(new Order
                {
                    UserId = userId,
                    OrderDate = date,
                    BentoItem = new() { Name = mealName },
                    AdditionalBentoItem = new() { Name = addMealName }
                });
            }
            else
            {
                existing.BentoItem ??= new();
                existing.BentoItem.Name = mealName;
                existing.AdditionalBentoItem ??= new();
                existing.AdditionalBentoItem.Name = addMealName;
            }

            applied++;
        }

        await db.SaveChangesAsync();
        return new BatchOrderResult(applied, skippedLocked, skippedExisting);
    }

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


    // 餐點名稱必須是空白、現有的餐點種類，或這筆訂單原本的值
    // （原本的餐點種類被刪除後，未修改的欄位仍可保留）
    private static async Task ValidateMealNamesAsync(AppDbContext db,
        Order order)
    {
        var names = new[]
        {
            order.BentoItem?.Name ?? string.Empty,
            order.AdditionalBentoItem?.Name ?? string.Empty
        }.Where(n => n.Length > 0).Distinct().ToList();

        if (names.Count == 0)
        {
            return;
        }

        var validNames = await db.MealTypes
            .Where(m => names.Contains(m.Name))
            .Select(m => m.Name)
            .ToListAsync();

        var existing = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o =>
            o.UserId == order.UserId && o.OrderDate == order.OrderDate);
        if (existing != null)
        {
            validNames.Add(existing.BentoItem?.Name ?? string.Empty);
            validNames.Add(existing.AdditionalBentoItem?.Name ?? string.Empty);
        }

        var invalid = names.FirstOrDefault(n => !validNames.Contains(n));
        if (invalid != null)
        {
            throw new InvalidOperationException($"餐點「{invalid}」不存在。");
        }
    }


    // --- 餐點種類相關 ---
    public const int MaxMealTypeNameLength = 20;

    // 依顯示順序取得所有餐點種類
    public async Task<List<MealType>> GetMealTypesAsync()
    {
        using var db = _dbFactory.CreateDbContext();
        return await db.MealTypes
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.Id)
            .ToListAsync();
    }

    // 新增餐點種類（排在最後），成功回傳 null，失敗回傳錯誤訊息
    public async Task<string?> AddMealTypeAsync(string name)
    {
        name = name?.Trim() ?? string.Empty;
        var error = ValidateMealTypeName(name);
        if (error != null)
        {
            return error;
        }

        using var db = _dbFactory.CreateDbContext();
        if (await db.MealTypes.AnyAsync(m => m.Name == name))
        {
            return $"「{name}」已存在";
        }

        var maxSort = await db.MealTypes
            .MaxAsync(m => (int?)m.SortOrder) ?? 0;
        db.MealTypes.Add(new MealType { Name = name, SortOrder = maxSort + 1 });
        await db.SaveChangesAsync();
        return null;
    }

    // 修改餐點名稱，並一併更新所有訂單（含歷史）中的名稱，讓統計維持一致
    public async Task<string?> RenameMealTypeAsync(int id, string newName)
    {
        newName = newName?.Trim() ?? string.Empty;
        var error = ValidateMealTypeName(newName);
        if (error != null)
        {
            return error;
        }

        using var db = _dbFactory.CreateDbContext();
        var mealType = await db.MealTypes.FindAsync(id);
        if (mealType == null)
        {
            return "找不到這個餐點種類";
        }

        var oldName = mealType.Name;
        if (oldName == newName)
        {
            return null;
        }

        if (await db.MealTypes.AnyAsync(m => m.Name == newName))
        {
            return $"「{newName}」已存在";
        }

        await using var transaction = await db.Database.BeginTransactionAsync();

        mealType.Name = newName;
        await db.SaveChangesAsync();

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"Orders\" SET \"BentoName\" = {newName} WHERE \"BentoName\" = {oldName}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"Orders\" SET \"AddBentoName\" = {newName} WHERE \"AddBentoName\" = {oldName}");

        await transaction.CommitAsync();
        return null;
    }

    // 刪除餐點種類。今天以後還有人訂這個餐點時不可刪除；
    // 過去的訂單保留原本的名稱
    public async Task<string?> DeleteMealTypeAsync(int id)
    {
        using var db = _dbFactory.CreateDbContext();
        var mealType = await db.MealTypes.FindAsync(id);
        if (mealType == null)
        {
            return "找不到這個餐點種類";
        }

        var name = mealType.Name;
        var today = AppTime.Today;
        var futureCount = await db.Orders.CountAsync(o =>
            o.OrderDate >= today &&
            (o.BentoItem!.Name == name || o.AdditionalBentoItem!.Name == name));

        if (futureCount > 0)
        {
            return $"今天以後還有 {futureCount} 筆訂單選了「{name}」，請先請使用者改選其他餐點";
        }

        db.MealTypes.Remove(mealType);
        await db.SaveChangesAsync();
        return null;
    }

    // 調整順序：direction = -1 往前、+1 往後
    public async Task MoveMealTypeAsync(int id, int direction)
    {
        using var db = _dbFactory.CreateDbContext();
        var list = await db.MealTypes
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.Id)
            .ToListAsync();

        var index = list.FindIndex(m => m.Id == id);
        var target = index + Math.Sign(direction);
        if (index < 0 || target < 0 || target >= list.Count)
        {
            return;
        }

        (list[index], list[target]) = (list[target], list[index]);

        // 重新編號，順便修正舊資料中重複或跳號的 SortOrder
        for (var i = 0; i < list.Count; i++)
        {
            list[i].SortOrder = i + 1;
        }

        await db.SaveChangesAsync();
    }

    private static string? ValidateMealTypeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "請輸入餐點名稱";
        }

        if (name.Length > MaxMealTypeNameLength)
        {
            return $"餐點名稱最多 {MaxMealTypeNameLength} 個字";
        }

        return null;
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
