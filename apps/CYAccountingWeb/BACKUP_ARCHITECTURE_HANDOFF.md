# CYAccountingWeb 備份架構與維運交接

更新：2026/10/15（台灣時間）。正式程式版本 **V0.24.0**、D1 schema **9**；目前版本與部署證據見 [WORK_HANDOFF.md](WORK_HANDOFF.md)。Backup outer／inner format 仍為 2，新增欄位採既有相容格式。永久規則依正式三層來源。

## 模組與唯一資料來源

D1 是唯一正式帳務資料庫；Backup 不是 live DB 或雙向同步。Worker 唯一入口 `src/app.js`，目前模組：

| 模組 | 責任 |
| --- | --- |
| `src/backup-package.js` | D1 匯出為 portable accounting payload |
| `src/backup-storage-provider.js` | provider-neutral storage contract |
| `src/gcs-backup-provider.js` | GCS adapter，generation 封裝為 opaque versionToken |
| `src/r2-backup-provider.js` | R2 adapter |
| `src/gcs-backup.js` | 已驗收 BackupSet 格式、完整性與 GCS operations |
| `src/backup-service.js` | topology、API、logical catalog、policy audit 與排程 orchestration |
| `src/backup-restore.js` | 空白隔離 SQLite 套用 canonical migrations、匯入及逐欄逐列回讀驗證 |
| `src/backup-restore-runtime.js` | Workers 靜態 WASM host adapter；每次建立並關閉獨立記憶體 DB |

沒有 active Google Drive OAuth 備份路由、版本 Worker wrapper 或第二個 Backup owner。`legacy_gcs` 保留已驗收 GCS 路徑；`parallel_dual_provider` 匯出一次，將同一 immutable package 保存為 R2／GCS copies。每個 provider 分別 read-back 驗證 hash／byte size／manifest，失敗互相隔離，不能刪除另一邊已驗證成功 copy。

## 執行設定與 Phase D 切換

binding／contract 名稱為 `DB`、`IDENTITY`、`BACKUP_R2`、`BACKUP_TOPOLOGY`。正式 resource identifiers、storage credentials、Session 及實際備份內容只放受保護 runtime；不進 Public Git／Release／Artifact。

| 項目 | 現況 |
| --- | --- |
| Cron | `30 19 * * *`，每日台灣時間 03:30 |
| 正式 topology | R2＋GCS `parallel_dual_provider`；2026/10/15 已正式回讀 Phase D 生效。policy 由 append-only `backup_policy_events` 決定，不因部署自動切換 |
| Phase C | R2 每日 30 天；GCS 每日 14 天 |
| Phase D | R2 每日 30 天；GCS 台灣時間週三、週日 03:30，26 週／182 天 |
| 手動／匯入前 | 各 phase 均雙 provider；沿用該 phase retention |
| 排程 gate | 本次已從正式備份頁確認 **14／14**；只計 scheduled、同 logical backupId、有效 package digest、兩 provider 成功且已回讀驗證 |
| 切換 gate | SUPER_ADMIN、provider 設定完整、14 次 gate，加上同一份備份的 R2／GCS 隔離還原皆成功且驗證時間在 24 小時內 |

啟用只新增 policy event 與操作者／驗證證據，**切換當下不清除既有物件**。未排 GCS 的日期不建立假失敗 copy，UI 顯示「本日未排程」。後續僅由既有 provider cleanup 處理超期 managed objects，失敗仍維持兩 provider 隔離；保留期限依 provider object 建立時間計算。原 Phase C 驗收證據不會被 Phase D 的 R2-only 日清除。

正式操作：桌機設定 → 備份／復原 →「驗證隔離還原」→ 兩 provider 通過 →「啟用 Phase D」。伺服器重新驗證門檻及最新 Phase C 排程，避免只信任 client；最新失敗或超時證據不得回退重用舊成功。啟用具 idempotence，不另加排程或第二 policy authority。[PR #446](https://github.com/simonliu1118-byte/CYapps/pull/446) 與 [main deploy](https://github.com/simonliu1118-byte/CYapps/actions/runs/38608683533) 已成功；正式 R2／GCS 副本隔離還原、完整資料比對及資料庫檢查已通過。使用者於另一瀏覽器完成啟用後，2026/10/15 已重新整理正式備份頁，確認「Phase D 已啟用」、R2 30 天／GCS 182 天及週三／週日 03:30 排程。首次新排程結果仍待觀察；不把啟用成功當成排程或 DR 已驗收。

provider lifecycle 如啟用，期限須長於 application policy，避免提前刪除 replication／rollback 來源；本次不擴大 provider IAM 或修改 bucket lifecycle，外部 lifecycle 與 budget 檢查仍列 TODO。

## 格式、schema 與 catalog

| 層次 | 現行格式 |
| --- | --- |
| Outer | `CYAccountingWebBackupSet / formatVersion 2` |
| Inner | `CYAccountingWebBackup / formatVersion 2` |
| 目前新備份 schema | **9** |
| 物件 | `CYAccountingWeb/<backup-id>/manifest.json` 與 `data.json` |
| Manifest | app/schema version、row counts、SHA-256、byte size、dataFormat／dataFormatVersion |

現行 inner v2 payload 包含交易、帳戶、科目、設定、openingBalanceOverrides、append-only openingBalanceAudit 等必要帳務資料。schema 7 起包含 additive `colorSlot`；schema 8 納入 `excelImportRuns` 指定期間替換稽核。schema 9 新增 backup verification／policy 的維運稽核，portable accounting payload 仍為原八個資料陣列；不備份 Session、backup catalog 或 policy audit，也不代表完整 D1 byte clone。統計指定移轉科目沿用 app settings 一同保存。`formatVersion`／`dataFormatVersion` 維持 2，不改寫舊備份。

歷史相容範圍仍包括：

- 舊 outer v2 manifest 缺少後來新增的 additive 欄位；
- schema 6 payload 沒有 `colorSlot`；
- 更早合法物件可能仍使用舊 `openingBalances` 表示。

既有物件不重寫。reader／validator 的資料格式相容不得被誤當成 application runtime 版本殼。未來真正 Restore 舊備份到目前 schema 仍需明確且驗證過的 migration/normalization 流程，不能只依欄位存在與否猜測。

logical catalog 使用 `backup_sets`／`backup_copies`，一個 backupId 列一次，provider 健康狀態分開。舊 `backup_runs` 保留歷史證據，不作另一套 routing authority。

## 管理權限、Restore 與跨 App 邊界

現行 Backup 管理由 server 判斷 SUPER_ADMIN；ADMIN／USER 不得管理。CYID 是唯一 Session authority，UI 隱藏不代替 API authorization。

本次新增隔離還原驗證：從 R2／GCS 各回讀 manifest／data，核對 byte size、SHA-256 與 logical catalog，再於全新記憶體 SQLite 套用正式 migrations，匯入帳戶（含封存）、分類／科目、交易（保留重複發生筆）、期初基準與 audit、設定及 Excel import audit。SQLite integrity／foreign-key check 及 canonical exporter 全欄全列回讀比對均通過才成功；驗證結果／操作者寫入 append-only metadata，不向 client 回傳帳務內容，不具正式帳本 writer。讀入 data 上限 8 MiB，支援來源 schema **8／9**；6／7 的舊資料 normalization 尚未實作，reader 相容不等於可還原。

WASM 固定 `sql.js@1.14.2` 的 browser JS／WASM 配對。Wrangler compile-time define 停用 library 的 WebWorker location loader，透過靜態 module 與 `instantiateWasm` 使用 Workers 支援的 precompiled WASM；不修改 runtime globals、不 eval、不 fetch 執行碼。共用 pinned engine，每次新建／關閉 DB；正式資料不落檔、不進 log。

**正式帳本 Restore 尚未開放，DR 未完成。** 未來流程至少需要：有效 provider copy → integrity／App/schema 驗證 → SUPER_ADMIN server gate → 雙重破壞性確認 → 受控 D1 restore → 對帳 → audit；是否加入 pre-restore backup 於實作時確認。R2 不可用時讀 GCS、Cloudflare/D1 故障後用 GCS 重建新 D1，仍需完整 DR 演練。

Phase E shared Backup Service 尚未啟用。未來可共用 provider storage mechanics，但不接管 CYACC accounting restore；每 App dataset／authorization／restore 仍隔離，caller 由 server-side mapping 識別，不只信任傳入 appId，不共享廣權限憑證。

## 已有與尚缺證據

自動驗證已有 inner v2、legacy outer v2 compatibility、export-once、copy digest parity、provider failure isolation、topology selection，以及 schema 7/account `colorSlot` 的現行 application regression。V0.22.11 main deploy #431 已成功套用 D1 migration 並部署 production source。

V0.24.0 新增真實 SQLite 逐欄回讀、實際 workerd 靜態 WASM 重複請求隔離、台灣時區排程、30／182 天 retention、14 次 gate、失敗／過期／竄改證據拒絕、append-only 稽核與權限測試。正式 Phase C 14／14 已回讀；真實 R2／GCS 副本隔離驗證亦已於部署後通過；Phase D 啟用已由正式備份頁回讀確認；尚未取得首次新排程執行結果。這些測試不等於正式帳本 Restore 或 DR 完成；後續工作見 [TODO.md](TODO.md)。
