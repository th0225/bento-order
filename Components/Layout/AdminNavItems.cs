using MudBlazor;

namespace bento_order.Components.Layout;

// 管理員選單項目：電腦版左側選單（NavMenu）與手機版上方下拉選單（MainLayout）共用
public static class AdminNavItems
{
    public record Item(string Href, string Icon, string Title);

    public static readonly IReadOnlyList<Item> All =
    [
        new("/admin/users", Icons.Material.Filled.People, "使用者管理"),
        new("/admin/stats", Icons.Material.Filled.Assessment, "餐點統計"),
        new("/admin/daily-manager", Icons.Material.Filled.CalendarMonth, "餐點管理"),
        new("/admin/meal-types", Icons.Material.Filled.Fastfood, "餐點種類"),
        new("/admin/date-manager", Icons.Material.Filled.LockClock, "日期鎖定"),
        new("/admin/menu-manager", Icons.Material.Filled.RestaurantMenu, "菜單管理"),
        new("/admin/system-config", Icons.Material.Filled.Settings, "系統設定"),
    ];
}
