# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 專案概要

公司內部的便當訂餐系統。員工在月曆上選每天的餐點，09:00 截止。管理員可以管理使用者、補登訂單、鎖定日期、上傳菜單。系統每天 09:05 把當日統計推播到 LINE 群組。

- 技術：.NET 10、Blazor Web App（全站 Interactive Server）、MudBlazor 9、EF Core 10 + SQLite。
- 沒有前端建置流程，也沒有 WebAssembly 專案。
- 介面文字、程式註解、commit 訊息都用繁體中文；回覆使用者也請用繁體中文。

## 常用指令

```bash
dotnet build          # 建置主專案與測試（目前 0 warning，請維持）
dotnet test           # 執行 tests/BentoOrder.Tests
dotnet test --filter "FullyQualifiedName~OrderLockPolicyTests"   # 只跑某一組測試
dotnet watch          # 開發用，http://localhost:5045，存檔後自動熱重載
dotnet list package --vulnerable --include-transitive   # 檢查套件漏洞
```

- 本機登入帳號：`admin` / `admin`，由 `AppDbContext` 的種子資料建立。第一次登入後必須修改密碼。
- 資料庫位置：沒設定 `DataDirectory` 時是 `database/app.db`（相對於 ContentRoot，也就是執行時的工作目錄），已被 .gitignore 排除。
  - 啟動時會自動建立資料夾，並執行 `Database.Migrate()`。
  - 要重置資料，刪掉這個資料夾即可。
- `dotnet watch` 若出現 inotify 上限錯誤，可以調高 `fs.inotify.max_user_instances`，或改用 `DOTNET_USE_POLLING_FILE_WATCHER=1 dotnet watch`。
- 測試專案 `tests/BentoOrder.Tests`（xUnit）：`TestDb` 用記憶體內 SQLite 並套用所有 migration，可直接測 `BentoDbService`。主專案的 csproj 已排除 `tests/**`，不會重複編譯。
- 沒有 linter。改動畫面時，除了 `dotnet build` / `dotnet test`，還要實際跑起來操作受影響的頁面，一般使用者與管理員各試一次。
- 部署：過去透過 GitHub Actions 發佈到 GCP VM 的 systemd 服務 `bento-order.service`。這個 workflow 已在 commit a592cfa 刪除，目前的部署方式請向使用者確認。

### 修改資料庫 schema

```bash
dotnet tool install --global dotnet-ef --version "10.*"   # 第一次使用前需安裝
dotnet ef migrations add <PascalCase名稱>
```

- 修改 `Models/` 或 `Data/AppDbContext.cs` 後，一定要產生 migration，並連同 `AppDbContextModelSnapshot.cs` 一起 commit。
- 加唯一索引或改資料格式時，要在 migration 中先用 `migrationBuilder.Sql` 整理既有資料（參考 `AddUniqueIndexes`），否則正式機啟動時 migration 會失敗。
- 不要修改已經存在的 migration。
- migration 會在正式機啟動時自動套用，所以必須能安全處理既有資料。SQLite 的 ALTER 功能有限，EF 常會以重建資料表的方式處理。

## 架構

### 執行模式

- `App.razor` 在 `<Routes>` 與 `<HeadOutlet>` 上設定 `InteractiveServer`，所有元件都在伺服器上透過 SignalR circuit 執行。
- 每個瀏覽器分頁就是一個 circuit，各自擁有一組 scoped 服務，`GlobalState` 也是。
- 事件處理函式中沒有攔截的例外，可能終止整個 circuit，讓畫面失去回應。`MainLayout` 用 `ErrorBoundary` 包住 `@Body`，並有 `#blazor-error-ui` 提示列，但事件處理函式仍應自行攔截例外。
- 自訂的 JS 都寫在 `App.razor` 的 inline `<script>` 裡：
  - `window.bentoAuth`：登入與登出，只呼叫 API 並回傳結果字串，不保存任何身分資料。
  - `window.compressImage`：在瀏覽器端把菜單圖片壓縮成 JPEG。壓縮後的 bytes 會透過 circuit 傳回伺服器，所以 `MaximumReceiveMessageSize` 設為 10MB。

### 驗證與授權

1. `Login.razor` 呼叫 JS 的 `bentoAuth.login`，它會 `fetch POST /auth/login`（`Program.cs` 中的 minimal API）。
2. 伺服器用 ASP.NET Identity 的 `PasswordHasher` 驗證密碼（舊的明碼密碼會在登入時自動轉成雜湊），然後發出 cookie `BentoOrderAuth`，有效 7 天。
3. 登入成功後以 `forceLoad: true` 重新載入 `/`，建立新的 circuit。`MainLayout.OnInitializedAsync` 從 `AuthenticationState` 的 claims 建立 `GlobalState.CurrentUser`（這是唯一的身分來源），`Login.razor` 再依角色導向。
4. 頁面用 `[Authorize]` 或 `[Authorize(Roles = Roles.Admin)]` 保護。`Routes.razor` 的 `AuthorizeRouteView` 在未授權時導回 `/`。
5. 每次 HTTP 請求時，cookie 的 `OnValidatePrincipal`（`Program.cs`）會確認使用者仍存在，被刪除的使用者會立即登出。
6. `Users.MustChangePassword = true` 時，`MainLayout` 會以 `ChangePasswordForm` 取代 `@Body`，改完密碼前看不到任何頁面。目前只有預設的 admin 帳號會被設為 true；一般使用者不強制改密碼（使用者明確要求），但可用右上角「改密碼」自行修改，管理員也可在使用者管理頁重設。改密碼與重設密碼都在 `BentoDbService`，密碼至少 `MinPasswordLength` 個字元。

- 角色定義在 `Models/AppConstants.cs` 的 `Roles`。管理員登入後導向 `/admin/users`，一般使用者導向 `/home`。
- 管理員選單項目定義在 `Components/Layout/AdminNavItems.cs`，電腦版左側選單（`NavMenu`）與手機版（小於 960px）AppBar 的下拉選單共用。新增管理頁時加在這裡。
- `/auth/login` 有限流：同一個 IP 每 5 分鐘最多 10 次，超過回傳 429。
- `/auth/*` 不套用 `/not-found` 狀態碼頁面，API 會回傳原本的狀態碼（401、429）。

### 資料存取

只有 `Services/BentoDbService.cs`（scoped）會存取資料庫。

- 它注入 `IDbContextFactory<AppDbContext>`，每個方法用 `using var db = _dbFactory.CreateDbContext()` 建立短命的 context。
- 原因：Blazor Server 中同一個 circuit 可能同時有多個非同步操作。不要把 `AppDbContext` 直接注入元件，也不要長期持有。

資料表：

- `Users`：`Username` 唯一；`PasswordHash`；`RealName` 是畫面上顯示的姓名；`Role`；`MustChangePassword`。刪除使用者時會一併刪除他今天以後的訂單，過去的訂單保留供結算。
- `Orders`：`(UserId, OrderDate)` 唯一，每人每天一筆。`UserId` 沒有外鍵。`MealCount` / `HasMeal` 是計算屬性，統計與「是否已訂」都以它為準。
  - 兩個 owned type：`BentoItem`（餐點 1）存在 `BentoName` / `BentoOption` 欄位，`AdditionalBentoItem`（餐點 2）存在 `AddBentoName` / `AddBentoOption` 欄位。
  - `Option` 目前沒有使用，是為 README 規劃中的飯量功能預留的。
- `LockedDates`：單一日期的鎖定設定，`Date` 唯一，規則見下一節。`Note` 是備註（例如國定假日名稱），顯示在月曆格子；管理員切換鎖定時會保留備註。
  - 國定假日匯入（`Services/HolidayImportService.cs`，在 `/admin/date-manager` 操作）：從政府資料開放平臺（資料集 14718「中華民國政府行政機關辦公日曆表」）下載該年度 CSV，或由管理員上傳同一份 CSV。平日放假 → `IsLocked = true`，週末上班（補班）→ `IsLocked = false`，其他日期不動。
- `SystemConfig`：key/value 設定，`Key` 唯一，可用的 key 定義在 `Models/AppConstants.cs` 的 `ConfigKeys`（LINE Token、兩個群組 ID、每日統計標題）。

- `MealTypes`：餐點種類（`Name` 唯一、`SortOrder` 排序），管理員在 `/admin/meal-types` 新增、改名、刪除、排序。

餐點種類以**名稱字串**直接存入 `Orders`，因此 `BentoDbService` 會處理一致性：

- 改名（`RenameMealTypeAsync`）：在同一個 transaction 中一併更新所有訂單（含歷史）的名稱。
- 刪除（`DeleteMealTypeAsync`）：今天以後還有訂單用到時拒絕；只有過去訂單用到時可刪，歷史保留原名稱。
- 儲存訂單（`UpsertOrderAsync`）：餐點名稱必須是空白、現有的種類，或這筆訂單原本的值。
- 頁面讀一次 `GetMealTypesAsync()`，再用 `MealTypes` 參數傳給 `CalendarCell`；已被刪除但訂單仍在用的名稱，會額外加進下拉選項以便顯示。
- `Models/MealProvider.cs` 只剩飯量選項（`GetMealOptions`），為 README 規劃中的飯量功能保留。

### 訂餐鎖定規則（核心商業邏輯）

規則只寫在 `Services/OrderLockPolicy.cs`，伺服器端（`BentoDbService.UpsertOrderAsync`）和 UI 端（`CalendarCell.razor`）共用。修改規則只改這個檔案。

一般使用者的規則依序判斷：

1. 過去的日期、當天 09:00 以後：一律鎖定，管理員解鎖也無效。
2. `LockedDates` 有該日紀錄時：`IsLocked = true` 為鎖定（例如國定假日）；`IsLocked = false` 為開放，用來開放週末補班日。
3. 沒有紀錄時：週六日鎖定，平日開放。

管理員不受這些限制。

### 月曆元件

- `Components/UI/MonthCalendar.razor`：月曆版面（切換月份、星期列、日期格），格子內容由 `CellTemplate` 決定。`/home` 與 `/admin/date-manager` 共用。
- `Components/UI/CalendarCell.razor`：單一天的格子。
  - 選擇餐點後，經過 500ms debounce 自動呼叫 `UpsertOrderAsync` 儲存，沒有「儲存」按鈕。選餐當下會再檢查一次鎖定，儲存失敗則還原成上一次成功儲存的值並顯示 Snackbar。
  - 訂單寫給 `TargetUserId` 參數指定的人；一般使用者一律寫給自己（元件內強制）。
  - 不會修改傳入的 `Order` 物件，畫面上的值存在元件自己的欄位。
- 批次訂餐：`/home` 的「批次訂餐」按鈕開啟 `BatchOrderDialog`，呼叫 `BentoDbService.BatchUpsertOrdersAsync`，把餐點套用到目前月份中指定星期幾的日子。一律依 `OrderLockPolicy` 略過已截止或鎖定的日子（管理員也一樣），預設不覆蓋已訂的日子。
- 用在三個地方：
  - `/home`：使用者自己的月曆。
  - `/admin/date-manager`：`ShowMeals="false"`，只用鎖頭圖示切換日期鎖定。
  - `CalendarDialog`：在 `/admin/daily-manager` 由管理員修改某人的訂單，`IsEditMode=true` 會解除鎖定。

### 背景工作與 LINE 推播

- `OrderReportService`：每天 09:05（台灣時間）呼叫 `BentoReportService.ProcessAndSendReportAsync`，把當日統計推播到 `LineId`。週末沒有人訂餐就不送。
- `NotifyOrderService`：每月 28 日 08:50 推播「完成下個月訂餐」提醒到 `LineOrderId`。
- 兩者都可以在 `/admin/system-config` 手動觸發。推播使用 `Line.Messaging` 套件呼叫 Messaging API 的 push，不是已停止服務的 LINE Notify。
- 每日統計訊息的標題可在設定頁修改（`ConfigKeys.LineReportTitle`），沒設定時用 `BentoReportService.DefaultReportTitle`。

### 時間

「現在」與「今天」一律用 `Services/AppTime.cs`（固定以 Asia/Taipei 計算），不受伺服器時區影響。不要直接用 `DateTime.Now` / `DateTime.Today`。

### 資料檔位置與菜單圖片

`Services/AppPaths.cs` 決定資料庫與菜單圖片的位置：

- 設定 `DataDirectory`（appsettings 或環境變數）時：資料庫是 `{DataDirectory}/app.db`，菜單在 `{DataDirectory}/menus/`。正式機建議設在發佈資料夾之外。
- 沒設定時：資料庫沿用 `ConnectionStrings:DefaultConnection`，菜單在資料庫所在資料夾的 `menus/`。

每月菜單（`Services/MenuService.cs`，singleton）：

1. 每個月一個檔案 `menus/yyyy-MM.jpg`。`/admin/menu-manager` 選月份上傳（預設：20 日以後為下個月）、更換或刪除；只接受 jpg，先在瀏覽器端壓縮（最長邊 2000px）。
2. `Program.cs` 的 `/menu-image/{yyyy-MM}` 端點以 no-cache 回傳圖片。
3. AppBar 的「菜單」按鈕開啟 `MenuDialog`，顯示首頁月曆目前所在月份（`GlobalState.CalendarMonth`）的菜單，對話框內可切換月份。
4. 舊版的單一菜單（`AppPaths.LegacyMenuFilePath`，例如 `wwwroot/menu.jpg`）會在啟動時由 `MenuService.MigrateLegacyMenu` 依上傳日期搬到對應月份。

### 資料備份

- `Services/BackupService.cs`（singleton）：以 `VACUUM INTO` 備份資料庫，連同 `menus/` 壓縮成 `backups/backup-yyyyMMdd-HHmmss.zip`，保留 30 天。不要改成直接複製 `app.db`，因為資料庫開著時最新資料可能還在 WAL 檔。
- `BackupBackgroundService`：每天 03:00（台灣時間）執行。失敗時寫入 log，`/admin/backups` 會顯示最近一次的結果（沒有主動通知）。
- `/admin/backups` 可立即備份與下載；下載端點 `/admin/backup-file/{name}` 只限管理員，檔名由 `BackupService.GetPath` 檢查以防路徑穿越。
- 還原步驟與異地備份建議見 [docs/backup-restore.md](docs/backup-restore.md)。

## 程式規則

1. 使用者身分與權限只能來自伺服器端 `AuthenticationState` 的 claims。不要用 JS interop 的回傳值、localStorage 或任何瀏覽器送來的資料，決定使用者是誰或是否為管理員。
2. 會修改資料的操作，必須在伺服器端再檢查一次權限。
   - UI 的隱藏或停用不算防護，例如 `readonly-icon` 的 `pointer-events: none`；Blazor 的事件處理函式仍然可以被觸發。
   - 新增管理頁面一定要加 `@attribute [Authorize(Roles = Roles.Admin)]`。
3. 會呼叫 service 的事件處理函式，必須攔截例外並用 `ISnackbar` 顯示中文訊息。例外逸出會終止整個 circuit。
4. 資料庫操作放在 service 中，並使用非同步的 EF API。
   - 日期一律存成午夜（`.Date`）；「現在」用 `AppTime`。
   - 查詢某一天時用區間 `o.OrderDate >= day && o.OrderDate < day.AddDays(1)`，不要用 `.Date` 或 Year/Month/Day 比較，否則用不到 `(UserId, OrderDate)` 索引。
5. 不要直接用 SQL 或 EF 修改 `MealTypes` 的名稱或刪除資料，一律透過 `BentoDbService` 的餐點種類方法，才能維持訂單中的名稱一致。
6. 角色與 `SystemConfig` 的 key 一律用 `Models/AppConstants.cs` 的 `Roles` / `ConfigKeys`，不要再寫字串。新增的常數也放在那裡。
7. 樣式規則：
   - 有亮色與暗色兩種主題，**不要寫死色碼**：優先用 MudBlazor 的 `var(--mud-palette-*)`（`MainLayout.razor` 的 `CustomTheme` 定義 `PaletteLight` 與 `PaletteDark`）；MudBlazor 沒有的顏色，在 `wwwroot/app.css` 開頭的 `--bento-*` 變數新增亮色與暗色兩個值（`:root` 與 `:root.bento-dark`）。
   - 主題切換：AppBar 的按鈕 → `GlobalState.SetDarkMode` + JS `bentoTheme.set`（寫入 `bento-theme` cookie、切換 `<html>` 的 `bento-dark` class）。`App.razor` 讀 cookie 讓伺服器直接輸出對應主題；沒有 cookie 時跟隨系統設定。
   - 元件專屬的 CSS 放在 `元件名.razor.css`。`::deep` 只在 `.razor.css` 裡有效；寫在元件內的 `<style>` 會變成全域樣式，而且每個實例都會重複插入。
   - 手機版斷點沿用 960px 與 MudBlazor 的 `d-md-*` 類別。
8. 執行期間產生的檔案（例如資料庫、上傳的圖片）的路徑一律由 `AppPaths` 提供，不要用 `Directory.GetCurrentDirectory()` 自己組。
9. 新增的 C# 檔案要加上 file-scoped 的 `namespace bento_order.<資料夾>;`。
10. `bento-order.csproj` 中 `SQLitePCLRaw.lib.e_sqlite3` 的版本是刻意覆寫的安全修補，因為 EF Core 10.0.3 會間接帶入有漏洞的 2.1.11。升級 EF Core 後，要先確認漏洞檢查指令沒有回報問題，才能移除這個覆寫。
11. 商業邏輯（鎖定規則、統計、訂單、密碼）有改動時，在 `tests/BentoOrder.Tests` 補上或更新測試。
12. Commit 訊息沿用 `feat:` / `fix:` / `refactor:` 加上繁體中文說明，例如 `fix: 修復第二個餐點儲存bug`。

## 已知問題

[docs/known-issues.md](docs/known-issues.md) 列出尚待處理與刻意保留的項目。修改相關程式前請先看過。修好一項就從清單中刪除，並更新本檔中受影響的描述。
