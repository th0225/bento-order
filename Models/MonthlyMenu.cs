using System.ComponentModel.DataAnnotations;

namespace bento_order.Models;

public class MonthlyMenu
{
    [Key]
    public int Id { get; set; }

    public int Year { get; set; }
    public int Month { get; set; }

    public string ImageUrl { get; set; } = string.Empty;
}