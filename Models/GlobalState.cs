namespace bento_order.Models;

public class GlobalState
{
    // 是否為深色模式
    public bool IsDarkMode { get; set; } = false;
    // 目前使用者
    public User? CurrentUser { get; set; }

    // 改變主題顏色時，通知所有UI元件更新
    public event Action? OnChange;
    public void SetDarkMode(bool isDark)
    {
        IsDarkMode = isDark;
        NotifyStateChanged();
    }
    private void NotifyStateChanged() => OnChange?.Invoke();
}