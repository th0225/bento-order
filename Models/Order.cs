using System.ComponentModel.DataAnnotations;

namespace bento_order.Models;

public class Order
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int UserId { get; set; }

    [Required]
    public int BentoItemId { get; set; }

    [Required]
    public DateOnly OrderDate { get; set; }
}