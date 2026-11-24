# 資料備份與還原

## 自動備份

- 每天 03:00（台灣時間）自動備份，保留最近 30 天。
- 備份檔位置：資料庫所在資料夾的 `backups/`（有設定 `DataDirectory` 時為 `{DataDirectory}/backups/`）。
- 檔名：`backup-yyyyMMdd-HHmmss.zip`，內容：
  - `app.db`：資料庫完整副本（以 SQLite `VACUUM INTO` 產生，網站運作中備份也一致）
  - `menus/`：每月菜單圖片
- 管理員可在「資料備份」頁（`/admin/backups`）立即備份、查看與下載備份檔。
- 備份失敗時會寫入伺服器 log，並在「資料備份」頁顯示失敗訊息；請不定期進去確認最近一次備份是否成功。

## 還原

1. 停止服務：

   ```bash
   sudo systemctl stop bento-order.service
   ```

2. 先把目前的資料另存一份（萬一還原錯了還能回來）：

   ```bash
   cd <資料庫所在資料夾>        # 例如發佈資料夾下的 database/
   mkdir -p ../before-restore && cp -a app.db* menus ../before-restore/
   ```

3. 解壓縮要還原的備份，取代資料庫並刪除 WAL 檔：

   ```bash
   unzip -o backups/backup-20261124-030000.zip -d /tmp/restore
   cp /tmp/restore/app.db app.db
   rm -f app.db-wal app.db-shm
   cp -a /tmp/restore/menus/. menus/
   ```

   > 一定要刪除 `app.db-wal`、`app.db-shm`，否則 SQLite 可能把舊的 WAL 套用到還原後的資料庫。

4. 啟動服務：

   ```bash
   sudo systemctl start bento-order.service
   ```

   啟動時會自動套用 migration，所以還原較舊版本的備份也可以。

## 異地備份（建議）

程式內的備份與資料庫在同一顆磁碟上，只能防止誤刪、誤改與檔案損毀；整台 VM 或磁碟故障時會一起遺失。建議另外：

- **GCP 磁碟快照排程**：Google Cloud 主控台 → Compute Engine → 快照 → 建立快照排程（例如每天一次、保留 7～14 天），再到 VM 的開機磁碟「編輯」→ 套用這個排程。
- 或不定期從「資料備份」頁下載備份檔，存到其他地方。
