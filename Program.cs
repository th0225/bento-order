using bento_order.Components;
using bento_order.Data;
using bento_order.Models;
using bento_order.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpOverrides;
using MudBlazor.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// 資料庫與菜單圖片的存放位置（見 Services/AppPaths.cs）
var appPaths = new AppPaths(builder.Configuration, builder.Environment);
builder.Services.AddSingleton(appPaths);

// Sqlite資料庫
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlite(appPaths.ConnectionString));

// Blazor：全站 Interactive Server。
// 菜單圖片在瀏覽器壓縮後透過 circuit 傳回，需放大單次訊息上限
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddHubOptions(options =>
    {
        options.MaximumReceiveMessageSize = 10 * 1024 * 1024;
    });
builder.Services.AddCascadingAuthenticationState();
// 定期重新檢查已開著分頁的登入狀態（使用者被刪除、改密碼後失效）
builder.Services.AddScoped<
    Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider,
    RevalidatingAuthStateProvider>();
// MudBlazor Service
builder.Services.AddMudServices();
// 全域狀態（每個 circuit 一份）
builder.Services.AddScoped<GlobalState>();
// 資料庫操作
builder.Services.AddScoped<BentoDbService>();
// 餐點統計並Line通知
builder.Services.AddScoped<BentoReportService>();
// 每月菜單圖片
builder.Services.AddSingleton<MenuService>();
// 匯入政府辦公日曆（國定假日、補班日）
builder.Services.AddHttpClient();
builder.Services.AddScoped<HolidayImportService>();
// 資料備份（每天 03:00，保留 30 天）
builder.Services.AddSingleton<BackupService>();
builder.Services.AddHostedService<BackupBackgroundService>();

// 餐點資料
builder.Services.AddSingleton<IMealProvider, MealProvider>();

// 排程：每日 09:05 統計、每月 28 日提醒
builder.Services.AddHostedService<OrderReportService>();
builder.Services.AddHostedService<NotifyOrderService>();

// Cookie 驗證
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.Cookie.Name = "BentoOrderAuth";
    options.LoginPath = "/Login";

    // 使用者被刪除、或改密碼/重設密碼後（安全戳記不同），原本的登入 cookie 立即失效
    options.Events.OnValidatePrincipal = async context =>
    {
        var dbService = context.HttpContext.RequestServices
            .GetRequiredService<BentoDbService>();

        if (!await RevalidatingAuthStateProvider.IsValidAsync(
            context.Principal, dbService))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(
                CookieAuthenticationDefaults.AuthenticationScheme);
        }
    };
});

builder.Services.AddAuthorization();

// 登入限流：同一個 IP 每 5 分鐘最多嘗試 10 次
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0
            }));
});

var app = builder.Build();

// --- 1. 基礎環境設定 ---
// 更新資料庫
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.Migrate();

    // 舊版單一菜單搬到對應月份
    scope.ServiceProvider.GetRequiredService<MenuService>().MigrateLegacyMenu();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// 頁面找不到時顯示 /not-found；/auth/* 是給 JS 呼叫的 API，保留原本的狀態碼
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/auth"),
    branch => branch.UseStatusCodePagesWithReExecute("/not-found",
        createScopeForStatusCodePages: true));

// --- 2. 請求處理與安全性 ---
// 處理轉發標頭
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto
});

app.UseHttpsRedirection();
app.UseStaticFiles();

// 設定cookie策略
app.UseCookiePolicy(new CookiePolicyOptions
{
    HttpOnly = Microsoft.AspNetCore.CookiePolicy.HttpOnlyPolicy.Always,
    Secure = CookieSecurePolicy.SameAsRequest,
    MinimumSameSitePolicy = SameSiteMode.Lax
});

// --- 3. 認證、授權與防偽 ---
app.UseRouting();
app.UseRateLimiter();

// 認證與授權
app.UseAuthentication(); // 認證：你是誰
app.UseAuthorization(); // 授權：你能做什麼

app.UseAntiforgery();

// 登入（由 App.razor 的 bentoAuth.login 呼叫）。
// 成功後前端會重新載入頁面，由伺服器依 cookie 決定身分與導向
app.MapPost("/auth/login", async (
    LoginRequest request,
    BentoDbService bentoDbService,
    HttpContext httpContext) =>
{
    var result = await bentoDbService.LoginAsync(
        request.Username,
        request.Password
    );

    if (result.Status == BentoDbService.LoginStatus.LockedOut)
    {
        // 帳號連續登入失敗太多次，暫時鎖定
        return Results.StatusCode(StatusCodes.Status423Locked);
    }

    if (result.User is not { } user)
    {
        return Results.Unauthorized();
    }

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new(ClaimTypes.Name, user.Username),
        new(ClaimTypes.Role, user.Role),
        new("RealName", user.RealName),
        new(RevalidatingAuthStateProvider.SecurityStampClaim, user.SecurityStamp)
    };

    var identity = new ClaimsIdentity(
        claims,
        CookieAuthenticationDefaults.AuthenticationScheme
    );

    await httpContext.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(identity),
        new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
        }
    );

    return Results.Ok();
}).AllowAnonymous().RequireRateLimiting("login");

app.MapPost("/auth/logout", async (HttpContext httpContext) =>
{
    await httpContext.SignOutAsync(
        CookieAuthenticationDefaults.AuthenticationScheme
    );

    return Results.Ok();
});

// 每月菜單圖片，例如 /menu-image/2026-12（位置見 AppPaths，no-cache 以免看到舊菜單）
app.MapGet("/menu-image/{month:regex(^\\d{{4}}-\\d{{2}}$)}", (string month,
    AppPaths paths, HttpContext httpContext) =>
{
    if (!DateTime.TryParseExact(month, "yyyy-MM", null,
        System.Globalization.DateTimeStyles.None, out var date))
    {
        return Results.NotFound();
    }

    var path = paths.MenuFilePath(date);
    if (!File.Exists(path))
    {
        return Results.NotFound();
    }

    httpContext.Response.Headers.CacheControl = "no-cache";
    return Results.File(path, "image/jpeg");
});

// 下載備份檔（僅管理員），檔名由 BackupService.GetPath 檢查格式，避免路徑穿越
app.MapGet("/admin/backup-file/{name}", (string name,
    BackupService backupService) =>
{
    var path = backupService.GetPath(name);
    return path == null
        ? Results.NotFound()
        : Results.File(path, "application/zip", name);
}).RequireAuthorization(policy => policy.RequireRole(Roles.Admin));

// --- 4. 端點對應 ---
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public record LoginRequest(string Username, string Password);
