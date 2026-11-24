using System.Security.Claims;

namespace bento_order.Models;

// 每個 circuit（瀏覽器分頁）一份的狀態。
// 使用者身分只能由伺服器端的 AuthenticationState claims 建立（MainLayout 負責），
// 不要從 JS 或 localStorage 設定。
public class GlobalState
{
    // 是否為深色模式（README 規劃中的功能，目前沒有暗色 palette）
    public bool IsDarkMode { get; set; } = false;
    // 目前使用者
    public UserSession? CurrentUser { get; private set; }
    // 是否為管理員
    public bool IsAdmin => CurrentUser?.Role == Roles.Admin;
    public event Action? OnChange;
    // 是否已從 claims 載入使用者
    public bool IsInitialized { get; private set; }

    public void SetCurrentUserFromClaims(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            CurrentUser = null;
            IsInitialized = true;
            NotifyStateChanged();
            return;
        }

        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        CurrentUser = new UserSession
        {
            Id = int.TryParse(userId, out var id) ? id : 0,
            Username = principal.Identity.Name ?? string.Empty,
            Realname = principal.FindFirstValue("RealName") ?? string.Empty,
            Role = principal.FindFirstValue(ClaimTypes.Role) ?? string.Empty
        };

        IsInitialized = true;
        NotifyStateChanged();
    }

    // 首頁月曆目前顯示的月份（AppBar 的「菜單」按鈕依此顯示該月菜單），null = 本月
    public DateTime? CalendarMonth { get; set; }

    // 是否必須先修改密碼才能使用系統（由 MainLayout 從資料庫載入）
    public bool MustChangePassword { get; private set; }

    public void SetMustChangePassword(bool value)
    {
        MustChangePassword = value;
        NotifyStateChanged();
    }

    // 改變主題顏色時，通知所有UI元件更新
    public void SetDarkMode(bool isDark)
    {
        IsDarkMode = isDark;
        NotifyStateChanged();
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}
