namespace bento_order.Models;

// 管理員對單一日期的鎖定設定（規則見 Services/OrderLockPolicy.cs）
public class LockedDate
{
    public int Id { get; set; }
    // 日期（午夜），唯一
    public DateTime Date { get; set; }
    public bool IsLocked { get; set; } = false;
}
