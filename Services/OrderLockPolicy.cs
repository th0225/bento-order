using bento_order.Models;

namespace bento_order.Services;

// 一般使用者的訂餐鎖定規則：UI（CalendarCell）與伺服器（UpsertOrderAsync）共用，
// 修改規則只改這裡。管理員不受這些限制。
public static class OrderLockPolicy
{
    // 每日截止時間（當天 09:00 後不可修改）
    public const int CutoffHour = 9;

    // lockRecord：該日期在 LockedDates 的紀錄，沒有則為 null
    //   IsLocked = true  → 管理員強制鎖定（例如國定假日）
    //   IsLocked = false → 管理員開放週末（補班日）；不會解除「過去日期」與「截止時間」
    public static bool IsLocked(DateTime date, DateTime now, LockedDate? lockRecord) =>
        GetLockReason(date, now, lockRecord) != null;

    // 鎖定的原因（顯示給使用者）；沒有鎖定時回傳 null
    public static string? GetLockReason(DateTime date, DateTime now,
        LockedDate? lockRecord)
    {
        var day = date.Date;

        if (day < now.Date)
        {
            return "已過日期";
        }

        if (day == now.Date && now.Hour >= CutoffHour)
        {
            return $"已超過 {CutoffHour:00}:00 截止時間";
        }

        if (lockRecord != null)
        {
            if (!lockRecord.IsLocked)
            {
                return null;
            }

            return string.IsNullOrEmpty(lockRecord.Note)
                ? "管理員已鎖定" : lockRecord.Note;
        }

        return day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
            ? "週末不供餐" : null;
    }
}
