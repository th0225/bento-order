# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 專案概要

公司內部的便當訂餐系統。員工在月曆上選每天的餐點，09:00 截止。管理員可以管理使用者、補登訂單、鎖定日期、上傳菜單。系統每天 09:05 把當日統計推播到 LINE 群組。

- 技術：.NET 10、Blazor Web App（全站 Interactive Server）、MudBlazor 9、EF Core 10 + SQLite。
- 沒有前端建置流程，也沒有 WebAssembly 專案。
- 介面文字、程式註解、commit 訊息都用繁體中文；回覆使用者也請用繁體中文。

## 常用指令

```bash
dotnet build          # 建置（目前 0 warning，請維持）
dotnet watch          # 開發用，http://localhost:5045，存檔後自動熱重載
dotnet list package --vulnerable --include-transitive   # 檢查套件漏洞
```

- 本機登入帳號：`admin` / `admin`，由 `AppDbContext` 的種子資料建立。第一次登入後必須修改密碼。
- 資料庫位置：`database/app.db`，相對於執行時的工作目錄，已被 .gitignore 排除。
  - 啟動時 `Program.cs` 會自動建立資料夾，並執行 `Database.Migrate()`。
  - 要重置資料，刪掉這個資料夾即可。
- `dotnet watch` 若出現 inotify 上限錯誤，可以調高 `fs.inotify.max_user_instances`，或改用 `DOTNET_USE_POLLING_FILE_WATCHER=1 dotnet watch`。
- 沒有測試專案，也沒有 linter。驗證方式是 `dotnet build` 通過，再實際跑起來操作受影響的頁面，一般使用者與管理員各試一次。
- 部署：過去透過 GitHub Actions 發佈到 GCP VM 的 systemd 服務 `bento-order.service`。這個 workflow 已在 commit a592cfa 刪除，目前的部署方式請向使用者確認。

### 修改資料庫 schema

```bash
dotnet tool install --global dotnet-ef --version "10.*"   # 第一次使用前需安裝
dotnet ef migrations add <PascalCase名稱>
```

- 修改 `Models/` 或 `Data/AppDbContext.cs` 後，一定要產生 migration，並連同 `AppDbContextModelSnapshot.cs` 一起 commit。
- 不要修改已經存在的 migration。
- migration 會在正式機啟動時自動套用，所以必須能安全處理既有資料。SQLite 的 ALTER 功能有限，EF 常會以重建資料表的方式處理。

## 架構

### 執行模式

- `App.razor` 在 `<Routes>` 與 `<HeadOutlet>` 上設定 `InteractiveServer`，所有元件都在伺服器上透過 SignalR circuit 執行。
- 每個瀏覽器分頁就是一個 circuit，各自擁有一組 scoped 服務，`GlobalState` 也是。
- 事件處理函式中沒有攔截的例外，可能終止整個 circuit，讓畫面失去回應。
- 自訂的 JS 都寫在 `App.razor` 的 inline `<script>` 裡：
  - `window.bentoAuth`：登入與登出。
  - `window.compressImage`：在瀏覽器端把菜單圖片壓縮成 JPEG。壓縮後的 bytes 會透過 circuit 傳回伺服器，所以 `MaximumReceiveMessageSize` 設為 10MB。

### 驗證與授權

1. `Login.razor` 呼叫 JS 的 `bentoAuth.login`，它會 `fetch POST /auth/login`（`Program.cs` 中的 minimal API）。
2. 伺服器用 ASP.NET Identity 的 `PasswordHasher` 驗證密碼（舊的明碼密碼會在登入時自動轉成雜湊），然後發出 cookie `BentoOrderAuth`，有效 7 天。
3. 登入後以 `forceLoad: true` 跳轉，建立新的 circuit。`MainLayout.OnInitializedAsync` 從 `AuthenticationState` 的 claims 建立 `GlobalState.CurrentUser`。
4. 頁面用 `[Authorize]` 或 `[Authorize(Roles = "Admin")]` 保護。`Routes.razor` 的 `AuthorizeRouteView` 在未授權時導回 `/`。
5. 每次 HTTP 請求時，cookie 的 `OnValidatePrincipal`（`Program.cs`）會確認使用者仍存在，被刪除的使用者會立即登出。
6. `Users.MustChangePassword = true` 時，`MainLayout` 會以 `ChangePasswordForm` 取代 `@Body`，改完密碼前看不到任何頁面。目前只有預設的 admin 帳號會被設為 true；一般使用者不強制改密碼（使用者明確要求），但可用右上角「改密碼」自行修改，管理員也可在使用者管理頁重設。改密碼與重設密碼都在 `BentoDbService`，密碼至少 `MinPasswordLength` 個字元。

- 角色只有 `"Admin"` 和 `"User"` 兩個字串。管理員登入後導向 `/admin/users`，一般使用者導向 `/home`。
- localStorage 的 `user`、`/auth/session`、`GlobalState.EnsureInitialized` 都是早期的快取機制，不是身分來源。

### 資料存取

只有 `Services/BentoDbService.cs`（scoped）會存取資料庫。

- 它注入 `IDbContextFactory<AppDbContext>`，每個方法用 `using var db = _dbFactory.CreateDbContext()` 建立短命的 context。
- 原因：Blazor Server 中同一個 circuit 可能同時有多個非同步操作。不要把 `AppDbContext` 直接注入元件，也不要長期持有。

資料表：

- `Users`：`Username` 唯一；`PasswordHash`；`RealName` 是畫面上顯示的姓名；`Role`；`MustChangePassword`。刪除使用者時會一併刪除他今天以後的訂單，過去的訂單保留供結算。
- `Orders`：預期每人每天一筆，但目前沒有唯一限制，`UserId` 也沒有外鍵。
  - 兩個 owned type：`BentoItem`（餐點 1）存在 `BentoName` / `BentoOption` 欄位，`AdditionalBentoItem`（餐點 2）存在 `AddBentoName` / `AddBentoOption` 欄位。
  - `Option` 目前沒有使用，是為 README 規劃中的飯量功能預留的。
- `LockedDates`：管理員對單一日期的鎖定設定，規則見下一節。
- `SystemConfig`：key/value 設定，存 `LineAccessToken`、`LineId`（每日統計群組）、`LineOrderId`（每月提醒群組）。
- `MonthlyMenus`：沒有使用。

餐點選項定義在 `Models/MealProvider.cs`（"A餐"、"B餐"、"素食"、"合菜"），以顯示字串直接存入 `Orders`。

### 訂餐鎖定規則（核心商業邏輯）

規則只寫在 `Services/OrderLockPolicy.cs`，伺服器端（`BentoDbService.UpsertOrderAsync`）和 UI 端（`CalendarCell.razor`）共用。修改規則只改這個檔案。

一般使用者的規則依序判斷：

1. 過去的日期、當天 09:00 以後：一律鎖定，管理員解鎖也無效。
2. `LockedDates` 有該日紀錄時：`IsLocked = true` 為鎖定（例如國定假日）；`IsLocked = false` 為開放，用來開放週末補班日。
3. 沒有紀錄時：週六日鎖定，平日開放。

管理員不受這些限制。

### 月曆元件 `Components/UI/CalendarCell.razor`

- 選擇餐點後，經過 500ms debounce 自動呼叫 `UpsertOrderAsync` 儲存，沒有「儲存」按鈕。選餐當下會再檢查一次鎖定，儲存失敗則還原成上一次成功儲存的值並顯示 Snackbar。
- `MainLayout` 用 `ErrorBoundary` 包住 `@Body`，並有 `#blazor-error-ui` 提示列；但事件處理函式仍應自行攔截例外。
- 用在三個地方：
  - `/home`：使用者自己的月曆。
  - `/admin/date-manager`：用鎖頭圖示切換日期鎖定。
  - `CalendarDialog`：在 `/admin/daily-manager` 由管理員修改某人的訂單，`IsEditMode=true` 會解除鎖定。
- 訂單寫給誰：管理員寫到 `GlobalState.FakeId`（由 daily-manager 設定，預設 1），一般使用者寫到自己的 Id。

### 背景工作與 LINE 推播

- `OrderReportService`：每天 09:05 呼叫 `BentoReportService.ProcessAndSendReportAsync`，把當日統計推播到 `LineId`。週末沒有人訂餐就不送。
- `NotifyOrderService`：每月 28 日 08:50 推播「完成下個月訂餐」提醒到 `LineOrderId`。
- 兩者都可以在 `/admin/system-config` 手動觸發。推播使用 `Line.Messaging` 套件呼叫 Messaging API 的 push，不是已停止服務的 LINE Notify。
- 所有時間都用 `DateTime.Now`，也就是伺服器的本地時間，所以正式機必須設為台灣時區（Asia/Taipei）。

### 菜單圖片

流程：

1. `/admin/menu-manager` 只接受 jpg，先在瀏覽器端壓縮。
2. `MenuUploadService` 把圖片寫到 `{工作目錄}/wwwroot/menu.jpg`（已被 .gitignore 排除）。
3. `MenuDialog` 以 `/menu.jpg?v={ticks}` 顯示，避免讀到快取。

## 程式規則

1. 使用者身分與權限只能來自伺服器端 `AuthenticationState` 的 claims。不要用 JS interop 的回傳值、localStorage 或任何瀏覽器送來的資料，決定使用者是誰或是否為管理員。
2. 會修改資料的操作，必須在伺服器端再檢查一次權限。
   - UI 的隱藏或停用不算防護，例如 `readonly-icon` 的 `pointer-events: none`；Blazor 的事件處理函式仍然可以被觸發。
   - 新增管理頁面一定要加 `@attribute [Authorize(Roles = "Admin")]`。
3. 會呼叫 service 的事件處理函式，必須攔截例外並用 `ISnackbar` 顯示中文訊息。例外逸出會終止整個 circuit。
4. 資料庫操作放在 service 中，並使用非同步的 EF API。
   - 日期一律存成午夜（`.Date`）。
   - 查詢某一天時用區間 `o.OrderDate >= day && o.OrderDate < day.AddDays(1)`，不要用 `.Date` 或 Year/Month/Day 比較，否則用不到 `(UserId, OrderDate)` 索引。
5. 不要修改或刪除 `MealProvider` 中現有的餐點名稱，只能新增。歷史訂單和統計都是用這些字串比對的。
6. 角色字串和 `SystemConfig` 的 key 目前散落在多個檔案。新增程式時不要再複製這些字串，改成集中定義的常數，並順手替換既有的用法。
7. 樣式規則：
   - 顏色優先使用 `MainLayout.razor` 中 `CustomTheme` 的 palette（或 `var(--mud-palette-*)`），不要再新增寫死的色碼。
   - 元件專屬的 CSS 放在 `元件名.razor.css`。`::deep` 只在 `.razor.css` 裡有效；寫在元件內的 `<style>` 會變成全域樣式，而且每個實例都會重複插入。
   - 手機版斷點沿用 960px 與 MudBlazor 的 `d-md-*` 類別。
8. 執行期間產生的檔案（例如資料庫、上傳的圖片）不要用 `Directory.GetCurrentDirectory()` 組路徑，改用 `IWebHostEnvironment` 的 `ContentRootPath` / `WebRootPath` 或設定值。
9. 新增的 C# 檔案要加上 file-scoped 的 `namespace bento_order.<資料夾>;`。
10. `bento-order.csproj` 中 `SQLitePCLRaw.lib.e_sqlite3` 的版本是刻意覆寫的安全修補，因為 EF Core 10.0.3 會間接帶入有漏洞的 2.1.11。升級 EF Core 後，要先確認漏洞檢查指令沒有回報問題，才能移除這個覆寫。
11. Commit 訊息沿用 `feat:` / `fix:` / `refactor:` 加上繁體中文說明，例如 `fix: 修復第二個餐點儲存bug`。

## 已知問題

[docs/known-issues.md](docs/known-issues.md) 列出 2026-11-24 檢視時發現的錯誤與待優化項目，依優先順序排列。修改相關程式前請先看過。修好一項就從清單中刪除，並更新本檔中受影響的描述。
