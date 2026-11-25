using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;

namespace bento_order.Services;

// 已開著的分頁（Blazor circuit）不會再經過 cookie 驗證，
// 所以每隔一段時間重新檢查：使用者被刪除、或改密碼/重設密碼後（安全戳記不同），
// 這個分頁就會變成未登入，AuthorizeRouteView 會導回登入頁。
public class RevalidatingAuthStateProvider
    : RevalidatingServerAuthenticationStateProvider
{
    public const string SecurityStampClaim = "SecurityStamp";

    private readonly IServiceScopeFactory _scopeFactory;

    public RevalidatingAuthStateProvider(ILoggerFactory loggerFactory,
        IServiceScopeFactory scopeFactory) : base(loggerFactory)
    {
        _scopeFactory = scopeFactory;
    }

    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(5);

    protected override async Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState,
        CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbService = scope.ServiceProvider.GetRequiredService<BentoDbService>();
        return await IsValidAsync(authenticationState.User, dbService);
    }

    // cookie 驗證（Program.cs）與 circuit 重新檢查共用：使用者存在且安全戳記相同
    public static async Task<bool> IsValidAsync(ClaimsPrincipal? principal,
        BentoDbService dbService)
    {
        var userId = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userId, out var id))
        {
            return false;
        }

        var currentStamp = await dbService.GetSecurityStampAsync(id);
        // 升級前發出的 cookie 沒有戳記，視為空字串（與尚未改過密碼的帳號相符）
        var cookieStamp = principal!.FindFirstValue(SecurityStampClaim)
            ?? string.Empty;
        return currentStamp != null && currentStamp == cookieStamp;
    }
}
