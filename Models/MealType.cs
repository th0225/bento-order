namespace bento_order.Models;

// 餐點種類（由管理員在 /admin/meal-types 維護）。
// 訂單以名稱字串存入 Orders，改名時 BentoDbService 會一併更新既有訂單。
public class MealType
{
    public int Id { get; set; }

    // 顯示與存入訂單的名稱，唯一
    public string Name { get; set; } = string.Empty;

    // 下拉選單中的順序（小的在前）
    public int SortOrder { get; set; }
}
