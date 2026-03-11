using System.ComponentModel.DataAnnotations;

namespace bento_order.Models;

public class BentoItem
{
    [Key]
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}