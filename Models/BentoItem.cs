namespace bento_order.Models;

public class BentoItem
{
    // 餐點種類(A, B, 素)
    public string Name { get; set; } = string.Empty;
    // 選項(正常, 多, 少)
    public string Options { get; set; } = string.Empty;
}