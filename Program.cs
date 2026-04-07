using bento_order.Components;
using bento_order.Data;
using bento_order.Models;
using bento_order.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpOverrides;
using MudBlazor.Services;
using Microsoft.AspNetCore.Authentication.Cookies;

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

// 處理轉發標頭
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto
});

// 動態判斷cookie安全性
app.UseCookiePolicy(new CookiePolicyOptions
{
    HttpOnly = Microsoft.AspNetCore.CookiePolicy.HttpOnlyPolicy.Always,
    Secure = CookieSecurePolicy.SameAsRequest,
    MinimumSameSitePolicy = SameSiteMode.Lax
});

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
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

// 處理 Cloudflare 轉發與 Cookie 策略
app.UseForwardedHeaders();
app.UseCookiePolicy();

// 認證與授權
app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();
app.UseStaticFiles();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
