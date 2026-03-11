using System.ComponentModel.DataAnnotations;

namespace bento_order.Models;

public class Order
{
    [Key]
    public int Id { get; set; }

    public int UserId { get; set; }

    public int BentoItemId { get; set; }

    [Required]
    public string RiceOption { get; set; } = "Normal";

    public DateOnly OrderDate { get; set; }

    public User? User { get; set; }
    public BentoItem? BentoItem { get; set; }
}