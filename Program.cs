using bento_order.Components;
using bento_order.Data;
using bento_order.Models;
using bento_order.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpOverrides;
using MudBlazor.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// 讀取資料庫連線字串
var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection");

// Sqlite資料庫
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlite(connectionString));
// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
// MudBlazor Service
builder.Services.AddMudServices();
// 全域狀態
builder.Services.AddScoped<GlobalState>();
// 資料庫操作
builder.Services.AddScoped<BentoDbService>();
// 餐點統計並Line通知
builder.Services.AddScoped<BentoReportService>();
// 菜單上傳
builder.Services.AddScoped<MenuUploadService>();

// 餐點資料
builder.Services.AddSingleton<IMealProvider, MealProvider>();

builder.Services.AddHostedService<OrderReportService>();

builder.Services.AddServerSideBlazor().AddHubOptions(options =>
{
    options.MaximumReceiveMessageSize = 10 * 1024 * 1024;
});

// 註冊驗證服務 (這是你目前漏掉的部分)
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.Cookie.Name = "BentoOrderAuth";
    options.LoginPath = "/Login";
});

builder.Services.AddAuthorization();

// builder.Services.ConfigureExternalCookie(options =>
// {
//     options.Cookie.HttpOnly = true;
//     options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
// });

var app = builder.Build();

// --- 1. 基礎環境設定 ---
// 更新資料庫
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.Migrate(); 
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", 
    createScopeForStatusCodePages: true);

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

// 認證與授權
app.UseAuthentication(); // 認證：你是誰
app.UseAuthorization(); // 授權：你能做什麼

app.UseAntiforgery();

app.MapPost("/auth/login", async (
    LoginRequest request,
    BentoDbService bentoDbService,
    HttpContext httpContext) =>
{
    var user = await bentoDbService.LoginAsync(
        request.Username,
        request.Password
    );

    if (user == null)
    {
        return Results.Unauthorized();
    }

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new(ClaimTypes.Name, user.Username),
        new(ClaimTypes.Role, user.Role),
        new("RealName", user.RealName)
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

    return Results.Ok(new UserSession
    {
        Id = user.Id,
        Username = user.Username,
        Realname = user.RealName,
        Role = user.Role,
        Expiry = DateTime.Now.AddDays(7)
    });
}).AllowAnonymous();

app.MapPost("/auth/logout", async (HttpContext httpContext) =>
{
    await httpContext.SignOutAsync(
        CookieAuthenticationDefaults.AuthenticationScheme
    );

    return Results.Ok();
});

app.MapGet("/auth/session", (HttpContext httpContext) =>
{
    var principal = httpContext.User;
    if (principal.Identity?.IsAuthenticated != true)
    {
        return Results.Unauthorized();
    }

    var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!int.TryParse(userId, out var id))
    {
        return Results.Unauthorized();
    }

    return Results.Ok(new UserSession
    {
        Id = id,
        Username = principal.Identity.Name ?? string.Empty,
        Realname = principal.FindFirstValue("RealName") ?? string.Empty,
        Role = principal.FindFirstValue(ClaimTypes.Role) ?? string.Empty,
        Expiry = DateTime.Now.AddDays(7)
    });
});

// --- 4. 端點對應 ---
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public record LoginRequest(string Username, string Password);
