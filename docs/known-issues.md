# 已知問題與優化清單

2026-11-24 全面檢視程式碼後整理。高、中、低優先的項目已於同日修正，這裡只列出刻意保留或尚待處理的部分。
修好一項就把它從清單刪掉，並同步更新 `CLAUDE.md` 中受影響的描述。

## 尚待處理

- **明碼密碼相容邏輯**：正式機的所有帳號都改過密碼後，移除 `BentoDbService.VerifyPassword` 中的 `user.PasswordHash == password` 比對。
- **顏色**：主要元件已改用 MudTheme palette（`var(--mud-palette-*)`），但頁面上仍有一些寫死的色碼，例如管理頁的 `#f8f9fa` 背景、`app.css` 的導覽列配色。之後要做暗色模式時需要一併整理。
- **正式機的資料目錄**：程式已支援 `DataDirectory` 設定（見 `Services/AppPaths.cs`），但正式機目前仍沿用舊位置（工作目錄下的 `database/app.db` 與 `wwwroot/menu.jpg`）。建議把資料搬到發佈資料夾之外，並設定 `DataDirectory`。

## 刻意保留

- **餐點名稱以字串存入資料庫**（`"A餐"` 等）：改成 ID 會牽涉所有歷史資料，維持「`MealProvider` 只能新增、不能改名」的規則即可。
- **`BentoItem.Option`、`MealProvider.GetMealOptions`**：README 規劃中的飯量功能會用到。
- **`GlobalState.IsDarkMode` / `SetDarkMode`**：README 規劃中的暗色模式會用到，目前還沒有暗色 palette。
