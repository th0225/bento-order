# 已知問題與優化清單

2026-11-24 全面檢視程式碼後整理，依優先順序排列，行號以當天的程式碼為準。
修好一項就把它從清單刪掉，並同步更新 `CLAUDE.md` 中受影響的描述。

## 🔴 高優先

H1（預設密碼）、H2（鎖定規則不一致導致畫面當掉）、H3（刪除使用者後訂單仍計入）已於 2026-11-24 修正。
剩下的後續工作：

- 正式機的所有帳號都改過密碼後，移除 `BentoDbService.VerifyPassword` 中相容明碼密碼的比對（`user.PasswordHash == password`）。

## 🟠 中優先

### M1. Orders 沒有 (UserId, OrderDate) 唯一索引

- 位置：[AppDbContext.cs:23-24](../Data/AppDbContext.cs#L23-L24) 是一般索引；Upsert 採用「先查詢，沒有再新增」（[BentoDbService.cs:227-235](../Services/BentoDbService.cs#L227-L235)）。
- 影響：兩個分頁或兩個人（使用者與管理員）同時操作同一天時，可能產生兩筆訂單。統計會重複計算，而且 [Home.razor:62-64](../Components/Pages/Home.razor#L62-L64) 的 `SingleOrDefault` 會丟出例外，讓頁面當掉。
- 建議：先用 migration 清除重複資料，再改成 `IsUnique()`；Upsert 遇到唯一鍵衝突（`DbUpdateException`）時改為更新。

### M2. GlobalState.FakeId 是各頁面共用的「目前編輯對象」

- 位置：[AdminDailyManager.razor:89](../Components/Pages/AdminDailyManager.razor#L89) 設定 FakeId；[CalendarCell.razor:341-342](../Components/UI/CalendarCell.razor#L341-L342) 管理員的操作一律寫入 FakeId 對應的使用者。
- 影響：管理員在「餐點管理」改過某人的訂單後，若在「日期鎖定」頁誤選了餐點，或進 `/home` 點餐，訂單都會寫到那個人身上。`/home` 顯示的是管理員自己的訂單，存的卻是別人的。此外，日期鎖定頁本來就不應該出現餐點選單。
- 建議：把目標 UserId 當成 `CalendarCell` 的 `[Parameter]` 傳入，並移除 FakeId；日期鎖定頁改用只有鎖定開關的元件，或加一個模式參數隱藏餐點選單。

### M3. 使用者身分可能被瀏覽器端偽造

- 位置：[Login.razor:100-103](../Components/Pages/Login.razor#L100-L103) 用 JS 回傳的 JSON 設定 `GlobalState.CurrentUser`；[GlobalState.cs:21-64](../Models/GlobalState.cs#L21-L64) 的 `EnsureInitialized` 也信任 JS 回傳值。
- 影響：JS interop 的回傳值完全由瀏覽器控制。有心人只要改寫 `window.bentoAuth` 並攔下跳轉，就能在自己的 circuit 中讓 `GlobalState.IsAdmin` 變成 true，然後在 `/home` 繞過截止時間、修改別人的訂單、切換日期鎖定。目前登入後會 forceLoad，所以較難觸發，但這是設計上的漏洞。
- 建議：
  - `CurrentUser` 只從 `AuthenticationState` 的 claims 建立。
  - 登入成功後直接 forceLoad 到 `/`，由伺服器決定導向哪一頁。
  - 移除 `EnsureInitialized`、localStorage 的 `user` 快取與 `/auth/session`。

### M4. 登入沒有失敗次數限制

- 位置：[Program.cs:139-185](../Program.cs#L139-L185)
- 建議：使用 ASP.NET Core Rate Limiting（`AddRateLimiter`），針對 `/auth/login` 以 IP 限制頻率。

### M5. 所有時間邏輯都依賴伺服器時區

- 位置：所有的 `DateTime.Now` / `DateTime.Today`，例如 [BentoDbService.cs:204](../Services/BentoDbService.cs#L204)、[CalendarCell.razor:319](../Components/UI/CalendarCell.razor#L319)、[OrderReportService.cs:15](../Services/OrderReportService.cs#L15)、[NotifyOrderService.cs:15](../Services/NotifyOrderService.cs#L15)、[BentoReportService.cs:16](../Services/BentoReportService.cs#L16)。
- 影響：09:00 截止、09:05 推播、每月 28 日提醒，全部以伺服器本地時間計算。雲端 VM 預設通常是 UTC；如果沒有設成 Asia/Taipei，截止時間會變成台灣時間 17:00。
- 建議：短期先確認正式機的時區設定；長期改為注入 `TimeProvider`，並明確轉換成 Asia/Taipei。

### M6. 資料檔的位置依賴「工作目錄」

- 位置：[appsettings.json:10](../appsettings.json#L10) 的 `database/app.db` 是相對路徑；[MenuUploadService.cs:19-20](../Services/MenuUploadService.cs#L19-L20) 使用 `Directory.GetCurrentDirectory()/wwwroot`。
- 影響：資料庫和菜單圖存在哪裡，取決於服務啟動時的工作目錄。如果工作目錄剛好是發佈輸出的資料夾，重新部署就會把它們刪掉（例如之前 workflow 中 scp 的 `rm: true`）。
- 建議：設定一個資料目錄（例如 appsettings 中的 `DataDirectory`），以 `ContentRootPath` 為基準解析，並放在發佈資料夾之外。菜單圖也改存到這個目錄，再用端點或 `PhysicalFileProvider` 提供。

## 🟡 低優先：程式品質與效能

- **L1 CSS**：[CalendarCell.razor](../Components/UI/CalendarCell.razor) 內的 `<style>` 用了 `::deep`（第 142、181-203 行）。`::deep` 只在 `.razor.css`（CSS isolation）中有效，所以這些手機版修正規則完全沒有作用。而且元件內的 `<style>` 每個格子都會重複插入一次（一個月 28–31 份）。建議改放到 `CalendarCell.razor.css`。另外有多處寫死的色碼（例如 `#4E342E`），應改用 MudTheme 的 palette。
- **L2 重複程式碼**：
  - `Home.razor` 與 `AdminDateLock.razor` 的月曆版面和 CSS 幾乎相同，可以抽成共用的月曆元件。
  - 角色字串 `"Admin"` / `"User"`，以及設定鍵 `LineAccessToken` / `LineId` / `LineOrderId` 散落在多個檔案，應改成常數。
- **L3 沒用到的程式碼**：
  - `MonthlyOrder`（`MonthlyMenus` 資料表）。
  - `BentoItem.Option` 與 `MealProvider.GetMealOptions`（為飯量功能預留）。
  - `GlobalState.IsDarkMode` / `SetDarkMode`（目前沒有暗色 palette）。
  - `MainLayout.razor` 的 `@rendermode InteractiveAuto`（專案沒有 WebAssembly）和前三行 `@using`。
  - `MainLayout.razor.css` 中範本留下的樣式，以及 `.rice-cycle-btn`。
  - `AdminDateLock.razor` 的 `orders` 欄位。
  - 多個頁面注入了卻沒用到的 `IJSRuntime`、`NavigationManager`、`GlobalState`。
- **L4 查詢效能**：用 `.Date` 或 Year/Month/Day 比較日期（[BentoDbService.cs:276-279](../Services/BentoDbService.cs#L276-L279)、299、325、351）無法使用索引；`GetLockedDatesAsync` 每次都撈出全部資料。目前資料量小，影響不大，但改成日期區間查詢比較好。
- **L5 資料一致性**：`SaveConfigAsync`、`SaveLockedDateAsync` 在 async 方法中用同步的 `FirstOrDefault`；`SystemConfig.Key` 和 `LockedDate.Date` 沒有唯一索引。
- **L6 「是否已訂」判斷不一致**：[BentoDbService.cs:366-368](../Services/BentoDbService.cs#L366-L368) 的 `HasOrdered` 把 null 當成已訂，統計則用 `IsNullOrEmpty`。建議在 `Order` 上加一個共用屬性。
- **L7 餐點名稱**：餐點名稱以顯示字串（例如 `"A餐"`）直接存入資料庫。修改 `MealProvider` 的名稱，歷史訂單和統計就會對不上。
- **L8 命名空間**：`Models/LockedDate.cs`、`Services/BentoReportService.cs`、`Services/NotifyOrderService.cs`、`Services/OrderReportService.cs` 都在全域命名空間。
- **L9 雜項**：
  - `App.razor` 的 `lang="en"` 應改為 `zh-Hant-TW`。
  - 載入了 Roboto 字型，但主題字型是 Noto Sans TC，而 Noto Sans TC 沒有載入。
  - LINE 訊息標題「新技術午餐統計」寫死在 [BentoReportService.cs:43](../Services/BentoReportService.cs#L43)。
  - `AdminStats` 的結算區間（上月 26 日～本月 25 日）過了 25 日不會自動換到下一期；而且它用 `OnParametersSetAsync` 載入資料。
  - `MenuDialog.razor:22` 的 style 少了分號。
  - 「尚未上傳菜單」的提示永遠不會出現，因為 `ImageUrl` 固定是 `/menu.jpg`。
  - 每月通知的成功訊息誤寫成「統計訊息」（[AdminSystemConfig.razor:163](../Components/Pages/AdminSystemConfig.razor#L163)）。
  - `MainLayout` 的 `GlobalState.OnChange += StateHasChanged` 應該改用 `InvokeAsync`。
- **L10 測試**：沒有測試專案。鎖定規則、統計、Upsert 這幾段商業邏輯最值得先補 xUnit 測試（可搭配 SQLite in-memory）。
