# CYAccountingWeb 備份架構與維運交接

更新：2026/10/03。正式功能版本 V0.22.1 Build 1；平板 V0.22.2 開發中，未變更備份拓樸／格式／排程。本文件描述現行實作與維運待辦，永久規則依 PROJECT_RULES.md、REPO_POLICY.md、REPOSITORY_RULES.md。目前版本證據見 [WORK_HANDOFF.md](WORK_HANDOFF.md)。

## 模組與唯一資料來源

D1 是唯一正式帳務資料庫；備份不是 live DB 或雙向同步。Worker 唯一入口 `src/app.js`，目前模組：

| 模組 | 責任 |
| --- | --- |
| `src/backup-package.js` | D1 匯出為 portable accounting payload |
| `src/backup-storage-provider.js` | provider-neutral storage contract |
| `src/gcs-backup-provider.js` | GCS adapter，generation 封裝為 opaque versionToken |
| `src/r2-backup-provider.js` | R2 adapter |
| `src/gcs-backup.js` | 已驗收 BackupSet 格式、完整性與 GCS operations |
| `src/backup-service.js` | 拓樸、API、logical catalog 與排程 orchestration |

沒有 active Google Drive OAuth 備份路由、版本 Worker wrapper 或第二個備份 owner。`legacy_gcs` 保留已驗收 GCS 路徑；`parallel_dual_provider` 匯出一次，將同一 immutable package 保存為 R2／GCS copies。每個 provider 分別 read-back 驗證 hash／byte size／manifest，失敗互相隔離，不能刪除另一邊已驗證的成功 copy。

## 執行設定與目前 Phase C

binding／contract 名稱為 `DB`、`IDENTITY`、`BACKUP_R2`、`BACKUP_TOPOLOGY`。正式 resource identifiers、storage credentials、Session 及實際備份內容只放受保護 runtime；不進 Public Git／Release／Artifact。

| 項目 | 現況 |
| --- | --- |
| Cron | `30 19 * * *`，每日台灣時間 03:30 |
| 正式拓樸 | Phase C，R2＋GCS parallel dual provider |
| Retention | R2 application 30 天；GCS 每日 14 天 |
| 已驗收 | GCS production 2026/09/26；manual paired production 2026/09/27 |
| 排程 gate | 連續 14 次有效 scheduled paired backup；最新 `x/14` 尚未讀取，不按日期猜測 |
| 下一階段 | Phase D 未啟用；gate 通過後才評估 R2 每日、GCS 每週三／週日、26 週／182 天 |

`x/14` 只計 scheduled、同 logical backupId、有效 package digest、R2/GCS 均成功的紀錄；手動備份不計數。不要在切換當天大量刪除已驗收 GCS 備份。provider lifecycle 如啟用，期限須長於當時 application policy，避免提前刪除 replication／rollback 來源。

## 格式與 catalog

| 層次 | 現行格式 |
| --- | --- |
| Outer | `CYAccountingWebBackupSet / formatVersion 2` |
| Inner | `CYAccountingWebBackup / formatVersion 2`，schema 6 |
| 物件 | `CYAccountingWeb/<backup-id>/manifest.json` 與 `data.json` |
| Manifest | app/schema version、row counts、SHA-256、byte size、dataFormat／dataFormatVersion |

inner v2 包含帳戶 `archivedAt`、openingBalanceOverrides（理由／actor）、append-only openingBalanceAudit（前／後值／CYID actor）、交易、科目及設定；不包含 Identity secrets／Sessions。appVersion 來自產生 package 的實際 source，不能因文件更新而改寫既有物件。

舊 outer v2 物件可沒有新增 manifest 欄位，payload 可為舊 `openingBalances`；reader／validator 保留資料格式相容，既有物件不重寫。未來復原舊備份到 schema 6，仍需明確且驗證過的轉換。這是資料格式相容，不是保留舊 runtime 殼。未來共通 outer `CYBackupSet / formatVersion 1` 尚未啟用，需獨立格式遷移。

logical catalog 使用 `backup_sets`／`backup_copies`，一個 backupId 列一次，provider 健康狀態分開。舊 `backup_runs` 保留備份歷史證據，不作另一套 routing authority。

## 管理權限、復原與跨 App 邊界

現行備份管理由 server 判斷 SUPER_ADMIN；ADMIN／USER 不得管理。CYID 是唯一 Session 權威，UI 隱藏不代替 API 授權。

復原尚未實作／驗收。未來流程需要有效 provider copy → 完整性／App/schema 驗證 → SUPER_ADMIN server gate → 雙重破壞性確認 → 受控 D1 restore → 對帳 → audit；是否加入 pre-restore backup 於實作時確認。R2 不可用時讀 GCS、Cloudflare/D1 故障後用 GCS 重建新 D1，仍需完整 DR 演練。

Phase E shared Backup Service 尚未啟用。未來可共用 provider storage mechanics，但不接管 CYACC accounting restore；每 App dataset／授權／復原仍隔離，caller 由 server-side mapping 識別，不只信任傳入 appId，不共享廣權限憑證。

已完成自動證據包括 inner v2、legacy outer v2 compatibility、export-once、copy digest parity、provider failure isolation 與 topology selection；不等於 scheduled `14/14` 或復原驗收。完整待辦集中於 [TODO.md](TODO.md)。
