namespace bento_order.Models;

public interface IMealProvider
{
    List<string> GetMealTypes();
    List<string> GetMealOptions();
}

public class MealProvider : IMealProvider
{
    private readonly List<string> _mealTypes = [
        "A餐", "B餐", "素食", "合菜"
    ];

    private readonly List<string> _mealOptions = [
        "普", "多", "少", "無"
    ];

    public List<string> GetMealTypes() => _mealTypes;
    public List<string> GetMealOptions() => _mealOptions;
}