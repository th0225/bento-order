namespace bento_order.Models;

public class UserSession
{
    public int Id { get; set; }
    // 帳號
    public string Username { get; set; } = string.Empty;
    // 姓名
    public string Realname { get; set; } = string.Empty;
    // 權限
    public string Role { get; set; } = string.Empty;
    // 過期時間
    public DateTime Expiry { get; set; }
}