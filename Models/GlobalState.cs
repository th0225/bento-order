using System.Text.Json;
using Microsoft.JSInterop;

namespace bento_order.Models;

public class GlobalState
{
    // 是否為深色模式
    public bool IsDarkMode { get; set; } = false;
    // 目前使用者
    public UserSession? CurrentUser { get; set; }
    // 是否為管理員
    public bool IsAdmin => CurrentUser?.Role == "Admin";
    // 供管理員修改使用者餐點
    public int FakeId = 1;
    public event Action? OnChange;
    // 頁面是否已初始化
    public bool IsInitialized { get; private set; }
    
    public async Task EnsureInitialized(IJSRuntime js)
    {
        if (IsInitialized)
        {
            return;
        }

        // 讀取user
        var userJson = await js.InvokeAsync<string>(
            "localStorage.getItem", "user"
        );

        if (!string.IsNullOrEmpty(userJson))
        {
            try
            {
                var session = JsonSerializer.Deserialize<UserSession>(userJson);

                if (session != null && DateTime.Now < session.Expiry)
                {
                    CurrentUser = session;
                    IsInitialized = true;
                }
                else
                {
                    await js.InvokeVoidAsync("localStorage.removeItem", "user");
                    CurrentUser = null;
                    IsInitialized = true;
                    return;
                }
            }
            catch
            {
                IsInitialized = true;
                return;
            }
        }
        else
        {
            IsInitialized = true;
        }

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