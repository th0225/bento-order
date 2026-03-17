namespace bento_order.Models;

public class OrderCount
{
    public string MealName { get; set; } = string.Empty;
    public string Option { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class UserOrderDisplay
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public Order OrderData { get; set; } = new();
    public bool HasOrdered { get; set; }
}