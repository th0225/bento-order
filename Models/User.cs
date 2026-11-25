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

    // 安全戳記：登入時放進 cookie；改密碼或重設密碼時更新，讓其他裝置的登入失效。
    // 空字串 = 尚未變更過（相容既有的登入 cookie）
    public string SecurityStamp { get; set; } = string.Empty;

    // 連續登入失敗次數（成功登入或鎖定後歸零）
    public int FailedLoginCount { get; set; }

    // 帳號鎖定到這個時間（台灣時間），null = 沒有鎖定
    public DateTime? LockoutEnd { get; set; }
}