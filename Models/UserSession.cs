namespace bento_order.Models;

// 目前登入者（由登入 cookie 的 claims 建立）
public class UserSession
{
    public int Id { get; set; }
    // 帳號
    public string Username { get; set; } = string.Empty;
    // 姓名
    public string Realname { get; set; } = string.Empty;
    // 權限
    public string Role { get; set; } = string.Empty;
}
