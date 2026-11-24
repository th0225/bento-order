namespace bento_order.Models;

// 餐點選項（飯量：普/多/少/無），README 規劃中的飯量功能會用到，目前沒有畫面使用。
// 餐點種類已改存資料庫（MealTypes 資料表），不在這裡。
public interface IMealProvider
{
    List<string> GetMealOptions();
}

public class MealProvider : IMealProvider
{
    private readonly List<string> _mealOptions = [
        "普", "多", "少", "無"
    ];

    public List<string> GetMealOptions() => _mealOptions;
}
