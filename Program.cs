using bento_order.Components;
using bento_order.Data;
using bento_order.Models;
using bento_order.Services;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;

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
// 資料庫操作
builder.Services.AddScoped<BentoDbService>();
builder.Services.AddScoped<BentoReportService>();

// 餐點資料
builder.Services.AddSingleton<IMealProvider, MealProvider>();
// 全域狀態
builder.Services.AddSingleton<GlobalState>();
// Line訊息通知
builder.Services.AddSingleton<LineNotifyService>();

builder.Services.AddHostedService<OrderReportService>();

var app = builder.Build();

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

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
