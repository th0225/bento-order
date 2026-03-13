using System.ComponentModel.DataAnnotations;

namespace bento_order.Models;

public class MonthlyOrder
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int Year { get; set; }
    [Required]
    public int Month { get; set; }

    [Required]
    // 整月餐點數量統計
    public int TotalOrder { get; set; }

    // 菜單網址
    public string ImageUrl { get; set; } = string.Empty;
}