using System.ComponentModel.DataAnnotations;

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
}