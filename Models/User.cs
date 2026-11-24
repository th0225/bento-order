using System.ComponentModel.DataAnnotations;

namespace bento_order.Models;

public class User
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    public string RealName { get; set; } = string.Empty;

    public string Role { get; set; } = Roles.User;

    // 下次登入必須先修改密碼（目前只用於預設的 admin 帳號）
    public bool MustChangePassword { get; set; }
}