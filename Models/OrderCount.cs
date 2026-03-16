namespace bento_order.Models;

public class OrderCount
{
    public string MealName { get; set; } = string.Empty;
    public string Option { get; set; } = string.Empty;
    public int Count { get; set; }
}