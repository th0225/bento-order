using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace bento_order.Models;

public class Order
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int UserId { get; set; }

    [Required]
    public DateTime OrderDate { get; set; }

    public BentoItem? BentoItem { get; set; } = new();

    public BentoItem? AdditionalBentoItem { get; set; } = new();

    // 這筆訂單的餐點數量（0~2），統計與「是否已訂」都以此為準
    [NotMapped]
    public int MealCount =>
        (string.IsNullOrEmpty(BentoItem?.Name) ? 0 : 1) +
        (string.IsNullOrEmpty(AdditionalBentoItem?.Name) ? 0 : 1);

    [NotMapped]
    public bool HasMeal => MealCount > 0;
}