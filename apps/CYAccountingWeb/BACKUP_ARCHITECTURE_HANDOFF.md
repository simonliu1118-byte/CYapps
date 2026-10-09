# CYAccountingWeb 備份架構與維運交接

更新：2026/10/05。CYAccountingWeb 目前正式功能版本 **V0.22.11 Build 0**、D1 schema **7**。V0.22.4～V0.22.11 未改變 Backup topology 或 outer/inner format version；V0.22.11 只以 additive account metadata 保存 `colorSlot`。永久規則依 `PROJECT_RULES.md`、根 `REPO_POLICY.md` 與 `REPOSITORY_RULES.md`；目前版本與部署證據見 [WORK_HANDOFF.md](WORK_HANDOFF.md)。

## 模組與唯一資料來源

D1 是唯一正式帳務資料庫；Backup 不是 live DB 或雙向同步。Worker 唯一入口 `src/app.js`，目前模組：

| 模組 | 責任 |
| --- | --- |
| `src/backup-package.js` | D1 匯出為 portable accounting payload |
| `src/backup-storage-provider.js` | provider-neutral storage contract |
| `src/gcs-backup-provider.js` | GCS adapter，generation 封裝為 opaque versionToken |
| `src/r2-backup-provider.js` | R2 adapter |
| `src/gcs-backup.js` | 已驗收 BackupSet 格式、完整性與 GCS operations |
| `src/backup-service.js` | topology、API、logical catalog 與排程 orchestration |

沒有 active Google Drive OAuth 備份路由、版本 Worker wrapper 或第二個 Backup owner。`legacy_gcs` 保留已驗收 GCS 路徑；`parallel_dual_provider` 匯出一次，將同一 immutable package 保存為 R2／GCS copies。每個 provider 分別 read-back 驗證 hash／byte size／manifest，失敗互相隔離，不能刪除另一邊已驗證成功 copy。

## 執行設定與目前 Phase C

binding／contract 名稱為 `DB`、`IDENTITY`、`BACKUP_R2`、`BACKUP_TOPOLOGY`。正式 resource identifiers、storage credentials、Session 及實際備份內容只放受保護 runtime；不進 Public Git／Release／Artifact。

| 項目 | 現況 |
| --- | --- |
| Cron | `30 19 * * *`，每日台灣時間 03:30 |
| 正式 topology | Phase C，R2＋GCS parallel dual provider |
| Retention | R2 application 30 天；GCS 每日 14 天 |
| 已驗收 | GCS production 2026/09/26；manual paired production 2026/09/27 |
| 排程 gate | 連續 14 次有效 scheduled paired backup；最新 `x/14` 尚未讀取，不按日期猜測 |
| 下一階段 | Phase D 未啟用；gate 通過後才評估 R2 每日、GCS 每週三／週日、26 週／182 天 |

`x/14` 只計 scheduled、同 logical backupId、有效 package digest、R2/GCS 均成功的紀錄；手動備份不計數。不要在切換當天大量刪除已驗收 GCS 備份。provider lifecycle 如啟用，期限須長於 application policy，避免提前刪除 replication／rollback 來源。

## 格式、schema 與 catalog

| 層次 | 現行格式 |
| --- | --- |
| Outer | `CYAccountingWebBackupSet / formatVersion 2` |
| Inner | `CYAccountingWebBackup / formatVersion 2` |
| 目前新備份 schema | **7** |
| 物件 | `CYAccountingWeb/<backup-id>/manifest.json` 與 `data.json` |
| Manifest | app/schema version、row counts、SHA-256、byte size、dataFormat／dataFormatVersion |

現行 inner v2 payload 包含交易、帳戶、科目、設定、openingBalanceOverrides、append-only openingBalanceAudit 等必要帳務資料。schema 7 帳戶可包含 additive `colorSlot`，用於保留 V0.22.11 的固定帳戶色號；這次沒有提升 `formatVersion`／`dataFormatVersion`。

歷史相容範圍仍包括：

- 舊 outer v2 manifest 缺少後來新增的 additive 欄位；
- schema 6 payload 沒有 `colorSlot`；
- 更早合法物件可能仍使用舊 `openingBalances` 表示。

既有物件不重寫。reader／validator 的資料格式相容不得被誤當成 application runtime 版本殼。未來真正 Restore 舊備份到目前 schema 仍需明確且驗證過的 migration/normalization 流程，不能只依欄位存在與否猜測。

logical catalog 使用 `backup_sets`／`backup_copies`，一個 backupId 列一次，provider 健康狀態分開。舊 `backup_runs` 保留歷史證據，不作另一套 routing authority。

## 管理權限、Restore 與跨 App 邊界

現行 Backup 管理由 server 判斷 SUPER_ADMIN；ADMIN／USER 不得管理。CYID 是唯一 Session authority，UI 隱藏不代替 API authorization。

Restore 尚未實作／驗收。未來流程至少需要：有效 provider copy → integrity／App/schema 驗證 → SUPER_ADMIN server gate → 雙重破壞性確認 → 受控 D1 restore → 對帳 → audit；是否加入 pre-restore backup 於實作時確認。R2 不可用時讀 GCS、Cloudflare/D1 故障後用 GCS 重建新 D1，仍需完整 DR 演練。

Phase E shared Backup Service 尚未啟用。未來可共用 provider storage mechanics，但不接管 CYACC accounting restore；每 App dataset／authorization／restore 仍隔離，caller 由 server-side mapping 識別，不只信任傳入 appId，不共享廣權限憑證。

## 已有與尚缺證據

自動驗證已有 inner v2、legacy outer v2 compatibility、export-once、copy digest parity、provider failure isolation、topology selection，以及 schema 7/account `colorSlot` 的現行 application regression。V0.22.11 main deploy #431 已成功套用 D1 migration 並部署 production source。

這些證據**不等於** scheduled `14/14`、Restore 或 DR 已完成。後續工作仍以 [TODO.md](TODO.md) 為準。
