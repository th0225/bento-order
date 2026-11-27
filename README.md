# 🍱 便當訂餐系統 (Bento Order System)

公司內部的便當訂餐系統，以 **Blazor Server** 與 **MudBlazor** 開發。員工在月曆上選每天的餐點，管理員負責統計、補登與設定，每天自動把統計推播到 LINE 群組。

## 🌟 功能

### 一般使用者

- **月曆訂餐**：每天預設一份餐點，可從下拉選單「新增第二份餐點」，按 ✕ 取消。選好後自動儲存，格子會顯示「✓ 已儲存」。
- **截止與鎖定**：每天 **09:00** 截止當日訂單。週末、國定假日不開放訂餐，管理員開放的補班日可以訂。滑鼠停在鎖定的格子上會顯示原因。
- **未訂提醒**：可訂但還沒訂的日子會以橘色框標示，月份卡片顯示「本月還有 N 天未訂」。
- **批次訂餐**：把一種餐點一次套用到本月指定星期幾的日子，會自動略過已截止或鎖定的日子。
- **每月菜單**：右上角的菜單按鈕會顯示目前月份的菜單，可以切換月份、放大縮小。
- **手機版**：月曆改成條列式，開啟時自動捲到今天。
- **暗色模式**：可手動切換；沒選過時跟隨系統設定。
- **自行修改密碼**。

### 管理員

- **使用者管理**：新增、刪除使用者，重設密碼（同時解除登入鎖定）。
- **每日統計與明細**：查看各餐點的數量與每個人的訂餐狀態，可以不受日期與截止時間限制，補登或修改任何人的訂單。
- **區間結算**：統計自訂日期區間（例如上月 26 日到本月 25 日）的訂餐量。
- **餐點種類管理**：新增、改名、刪除、排序。改名會一併更新歷史訂單。
- **日期管理**：手動鎖定或開放某一天。也可以從政府資料開放平臺匯入國定假日與補班日，網路不通時可上傳 CSV。
- **菜單管理**：依月份上傳、更換或刪除菜單圖片，上傳前會在瀏覽器壓縮。
- **資料備份**：每天 03:00 自動備份資料庫與菜單，保留 30 天，也可以手動備份與下載。
- **LINE 推播**：每天 09:05 推播當日統計，每月 28 日 08:50 提醒大家訂下個月的餐點，都可以手動觸發。

### 安全性

- 密碼以 ASP.NET Identity 的 `PasswordHasher` 雜湊儲存，至少 8 個字元。
- 同一個帳號連續登入失敗 5 次會鎖定 15 分鐘；同一個 IP 每 5 分鐘最多嘗試 10 次。
- 改密碼或重設密碼後，所有裝置上的登入都會失效。
- 預設的 admin 帳號第一次登入時必須改密碼。

## 🛠️ 技術

- .NET 10、Blazor Web App（全站 Interactive Server）
- MudBlazor 9
- Entity Framework Core 10 + SQLite
- LINE Messaging API（`Line.Messaging` 套件）
- xUnit 測試（`tests/BentoOrder.Tests`）

## 🚀 開發

```bash
dotnet build
dotnet test
dotnet watch          # http://localhost:5045
```

- 預設帳號是 `admin` / `admin`，第一次登入後必須修改密碼。
- 資料庫預設在 `database/app.db`，第一次啟動時會自動建立並套用 migration。要重置資料，刪掉 `database/` 即可。
- 修改資料表之後要產生 migration：

  ```bash
  dotnet tool install --global dotnet-ef --version "10.*"
  dotnet ef migrations add <名稱>
  ```

更完整的開發規則與架構說明見 [CLAUDE.md](CLAUDE.md)，尚待處理的項目見 [docs/known-issues.md](docs/known-issues.md)。

## 📦 部署

1. 發佈：

   ```bash
   dotnet publish -c Release -o ./publish
   ```

2. 用環境變數 `DataDirectory` 把資料放在發佈資料夾之外，更新程式時才不會覆蓋到資料。例如在 systemd 服務中設定：

   ```ini
   [Service]
   Environment=DataDirectory=/var/lib/bento-order
   ```

   - 資料庫、菜單、備份會分別放在 `app.db`、`menus/`、`backups/`。
   - 這個資料夾必須屬於執行服務的帳號，因為 SQLite 要在同一個資料夾建立 `-wal` 與 `-shm` 檔。權限不對時，啟動會出現 `attempt to write a readonly database`。

3. 啟動時會自動套用資料庫 migration，既有資料會保留。
4. 用 nginx 反向代理時，要轉送 WebSocket，否則 Blazor 會頻繁斷線：

   ```nginx
   proxy_http_version 1.1;
   proxy_set_header Upgrade $http_upgrade;
   proxy_set_header Connection "upgrade";
   proxy_set_header Host $host;
   proxy_read_timeout 300s;
   ```

備份還原步驟見 [docs/backup-restore.md](docs/backup-restore.md)。

## 📅 TODO

- [ ] **飯量選項**：每份餐點可以選正常、多、少（資料欄位 `Option` 已預留）。
