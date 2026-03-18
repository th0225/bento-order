using System.ComponentModel.DataAnnotations;
namespace bento_order.Models;

public class SystemConfig
{
    [Key]
    public int Id { get; set; }

    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}