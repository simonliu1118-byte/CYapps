# CYAccountingWeb 備份架構與維運交接

更新：2026/10/30（台灣時間）。本文件描述**現行程式契約及歷史維運證據**。目前已部署 V0.25.18 Build 2／D1 **schema 15**；最新部署 SHA、驗收邊界與使用者待辦見 [WORK_HANDOFF.md](WORK_HANDOFF.md)、[TODO.md](TODO.md)。Backup outer／inner `formatVersion` 均維持 **2**；尚未經受保護維運環境重新查證的每日排程／provider 健康不依文件或 CI 結果推定。

**最新 schema / migration 摘要：** schema 12 查詢 covering indexes；schema 13 期初快照（衍生資料，不可攜備份）；schema 14 `transactions.marker_color`；schema 15 `transactions.marker_target`（summary/category）。**標記是顯示用 metadata**，改色／改位置不改帳務內容、也不使期初快照失效；已鎖月份仍可標記，但一般財務修改仍禁止，唯讀 USER 不可標記。現行新備份會保存這兩欄，舊 schema 6–13 還原時補空色彩，6–14 的標記位置預設 `summary`。schema 6–15 的合法來源可經 canonical normalizer 驗證還原到 schema 15；這是程式支援範圍，不表示每份真實歷史備份都已單獨驗收。

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

啟用只新增 policy event 與操作者／驗證證據，**切換當下不清除既有物件**。未排 GCS 的日期不建立假失敗 copy，桌機 UI 留白，觸控資訊保留未排程狀態。後續僅由既有 provider cleanup 處理超期 managed objects，失敗仍維持兩 provider 隔離；保留期限依 provider object 建立時間計算。原 Phase C 驗收證據不會被 Phase D 的 R2-only 日清除。

Phase D 啟用時的**歷史**流程：桌機設定 → 備份／復原 → 兩 provider 隔離驗證通過 → 啟用。現已啟用，測試進度／啟用控制退場；手動檢查按鈕及說明已由 V0.25.5 移除，改每日自動檢查；真正重建由底部左側「還原操作…」開啟獨立 dialog，右側為緊急復原入口。伺服器重新驗證門檻及最新 Phase C 排程，避免只信任 client；最新失敗或超時證據不得回退重用舊成功。啟用具 idempotence，不另加排程或第二 policy authority。[PR #446](https://github.com/simonliu1118-byte/CYapps/pull/446) 與 [main deploy](https://github.com/simonliu1118-byte/CYapps/actions/runs/38608683533) 已成功；正式 R2／GCS 副本隔離還原、完整資料比對及資料庫檢查已通過。使用者於另一瀏覽器完成啟用後，2026/10/15 已重新整理正式備份頁，確認「Phase D 已啟用」、R2 30 天／GCS 182 天及週三／週日 03:30 排程。首次新排程結果仍待觀察；不把啟用成功當成排程或 DR 已驗收。

provider lifecycle 如啟用，期限須長於 application policy，避免提前刪除 replication／rollback 來源；本次不擴大 provider IAM 或修改 bucket lifecycle，外部 lifecycle 仍列 TODO。每月低額預算已由使用者建立於帳單帳戶範圍，依其決定包含優惠，實際支出門檻 50／90／100%；幣別與 Email 送達未由清單驗收，不宣稱自動支出硬上限。

## 格式、schema 與 catalog

| 層次 | 現行格式 |
| --- | --- |
| Outer | `CYAccountingWebBackupSet / formatVersion 2` |
| Inner | `CYAccountingWebBackup / formatVersion 2` |
| 目前新備份 schema | **15**（截至 V0.25.18 Build 2） |
| 物件 | `CYAccountingWeb/<backup-id>/manifest.json` 與 `data.json` |
| Manifest | app/schema version、row counts、SHA-256、byte size、dataFormat／dataFormatVersion |

現行 inner v2 payload 包含交易（含 `markerColor`／`markerTarget`）、帳戶、科目、設定、openingBalanceOverrides、append-only openingBalanceAudit 等必要帳務資料。schema 7 起包含 additive `colorSlot`；schema 8 納入 `excelImportRuns` 指定期間替換稽核。schema 9 新增 backup verification／policy 的維運稽核，portable accounting payload 仍為原八個資料陣列；schema 10 以 manifest recoveryContext 額外保存 catalog／policy／restore proof，仍不備份 Session 或機密，也不代表完整 D1 byte clone。schema 11 加入健康衍生狀態（不屬 portable payload）；schema 12 增加交易讀取索引、schema 13 增加期初衍生快照（兩者不隨備份搬運）；schema 14／15 為交易螢光顏色與文字標記位置，會隨 portable transaction rows 保存。統計指定移轉科目沿用 app settings 一同保存。`formatVersion`／`dataFormatVersion` 維持 2，不改寫舊備份。

歷史相容範圍仍包括：

- 舊 outer v2 manifest 缺少後來新增的 additive 欄位；
- schema 6 payload 沒有 `colorSlot`；
- 更早合法物件可能仍使用舊 `openingBalances` 表示。

既有物件不重寫。reader／validator 的資料格式相容不得被誤當成 application runtime 版本殼。V0.25.2 當時已支援來源 schema 6～10；**現行** `src/backup-restore.js` normalizer 支援 schema 6～15，對舊來源補 `markerColor=null` 與 `markerTarget='summary'`，先驗證原 package immutable bytes/digest 才 normalization。不只依欄位存在與否猜測。

logical catalog 使用 `backup_sets`／`backup_copies`，一個 backupId 列一次，provider 健康狀態分開。舊 `backup_runs` 保留歷史證據，不作另一套 routing authority。

## 管理權限、Restore 與跨 App 邊界

現行 Backup 管理由 server 判斷 SUPER_ADMIN；ADMIN／USER 不得管理。CYID 是唯一 Session authority，UI 隱藏不代替 API authorization。

本次新增隔離還原驗證：從 R2／GCS 各回讀 manifest／data，核對 byte size、SHA-256 與 logical catalog，再於全新記憶體 SQLite 套用正式 migrations，匯入帳戶（含封存）、分類／科目、交易（保留重複發生筆）、期初基準與 audit、設定及 Excel import audit。SQLite integrity／foreign-key check 及 canonical exporter 全欄全列回讀比對均通過才成功；驗證結果／操作者寫入 append-only metadata，不向 client 回傳帳務內容，V0.25.0 另由受控 recovery domain 寫入獨立空白 D1，不覆寫正式 DB。讀入 data 上限 8 MiB，**目前支援來源 schema 6～15**；6／7 以及缺少新增標記欄位的舊 schema 都經 canonical normalization 後完整比對。

WASM 固定 `sql.js@1.14.2` 的 browser JS／WASM 配對。Wrangler compile-time define 停用 library 的 WebWorker location loader，透過靜態 module 與 `instantiateWasm` 使用 Workers 支援的 precompiled WASM；不修改 runtime globals、不 eval、不 fetch 執行碼。共用 pinned engine，每次新建／關閉 DB；正式資料不落檔、不進 log。

V0.25.0 提供獨立 D1 受控復原與 GCS 重建入口；本批正式部署及 GCS → 獨立 D1 完整還原驗收通過，證據見 WORK_HANDOFF。正式流量 promotion 不屬 HTTP 還原動作，需依下方維運手冊明確切換；不以演練自動覆寫當前帳本。

Phase E shared Backup Service 尚未啟用。未來可共用 provider storage mechanics，但不接管 CYACC accounting restore；每 App dataset／authorization／restore 仍隔離，caller 由 server-side mapping 識別，不只信任傳入 appId，不共享廣權限憑證。

## 已有與尚缺證據

自動驗證已有 inner v2、legacy outer v2 compatibility、export-once、copy digest parity、provider failure isolation、topology selection，以及 schema 7/account `colorSlot` 的現行 application regression。V0.22.11 main deploy #431 已成功套用 D1 migration 並部署 production source。

V0.24.0 新增真實 SQLite 逐欄回讀、實際 workerd 靜態 WASM 重複請求隔離、台灣時區排程、30／182 天 retention、14 次 gate、失敗／過期／竄改證據拒絕、append-only 稽核與權限測試。正式 Phase C 14／14 已回讀；真實 R2／GCS 副本隔離驗證亦已於部署後通過；Phase D 啟用已由正式備份頁回讀確認；首次 R2-only 新排程已回讀成功；新政策下的 GCS 週期排程仍待觀察。V0.25.0 已完成正式 GCS → 獨立 D1 重建驗收，production promotion 不在本次演練範圍；後續工作見 [TODO.md](TODO.md)。

V0.24.1 在 Phase D 生效後退場 Phase C 驗收／測試用 UI 與已完成的啟用控制；底層 gate 及 append-only 驗收／policy 紀錄保留。後續 V0.25.0 的正式獨立 D1 還原演練已完成，見下節；production promotion 仍屬另行維運操作。

## 受控獨立 D1 復原與災難復原（V0.25.0 起的歷史流程；現行 schema 15）

`backup-service.js` 為唯一 API／orchestration owner；`backup-recovery.js` 延伸 canonical restore mapping 寫入獨立空白 D1，`backup-recovery-context.js` 集中維運資料 allowlist。`/recovery` 是相同 CYID SUPER_ADMIN guard 的 server-rendered presentation，沿用 `/api/backup/recovery/preview`、`commit`；不依賴原帳本才能載入。沒有新 Identity authority、觀察器、retry bootstrap 或第二個 Backup API owner。備份按鈕由自身 action 呼叫既有 `cyConfirm`，刪除 Adaptive capture 的重複確認路徑。

格式 2 新 manifest 可攜帶 `recoveryContext`：backup_sets、backup_copies、backup_restore_checks、backup_policy_events、backup_runs。它與 manifest 一起受既有 hash／package read-back 驗證；來源 DB 可用時額外核對 catalog。GCS 使用受保護 provider credentials 讀回；原 DB 不可用時，schema 10 的 context 提供獨立重建所需維運狀態。context 是建備份當下快照，不包含該份備份稍後才寫入的 catalog 列。Session、OAuth、憑證及工作區自己的 recovery preview/run/event 不複製；既有帳務 payload 八組 arrays 及 append-only 期初／Excel audit 完整保留。

1. 以 SUPER_ADMIN 選定備份、來源與理由。server 驗證 bytes／format／schema／digest，執行全新記憶體 SQLite 實際還原核對。
2. 10 分鐘 preview 綁定 server actor、來源 hash、context hash 與目標工作區；勾選核對並輸入「重建獨立帳本」後才可執行，重新驗證來源，過期或變動須重新預覽。
3. 原子 D1 batch 再次確認目標為已 provision、未 claim、完全符合初始種子快照。僅替換目標種子 master rows，寫入全部帳務及 context，新增 epoch 與 recovery_hold。禁止清空正式 DB、刪除任何歷史 audit 或重用已還原工作區。
4. 回讀全部欄列、政策／catalog／proof；來源 SQLite 使用 integrity_check，實際 D1 使用 quick_check 與 foreign_key_check。結果 append-only。成功也不自動改正式 binding，原 D1 保留。

資料及 manifest 各限 8 MiB；400 列一組、最多 40 statements 的原子 batch。超出限制須維運另行評估，不進行部分寫入。**目前來源 schema 6～15 可經現行還原驗證支援**；6～9 若無 recoveryContext 仍需原 DB 可用，並非所有舊備份都能在原 DB 中斷時獨立復原。新復原事件保存在目標，原 DB 可用時另寫完成證據；保留原 DB 的歷史不能解讀為備份已含所有物理表。

### 部署與正式切換手冊

受保護 Production Deploy 以現有 Cloudflare token 找到／建立本 App 的獨立 recovery D1，套用 canonical migrations，私下注入 RECOVERY_DB；所有實際名稱／ID mask，不上傳 config 或資料。既有 DB 不刪除；後續 deploy 不清除已使用的工作區。Worker 沒有新增 Cloudflare 管理 token。若現有 token 無建立權限，部署明確失敗，不能默默退回正式 DB。

正式帳本 promotion 與演練是分開的維運動作，本批**不執行**：

1. 取得實際切換來源／時間與使用者授權，維護期間先做目前正式帳本 paired backup 並回讀。保留原 DB、全部 audit 與復原證據。
2. 確認目標 verified record、完整資料／政策／鎖帳狀態、epoch 及 schema；再次核對，不能只看 HTTP 成功。
3. 在受保護部署環境將 CF_D1_DATABASE_ID／NAME 指向已驗證目標；於核准的維護切換解除該目標 recovery_hold。正式 DB binding 名稱維持 DB，CYID、R2、GCS 設定維持 canonical owners。
4. 經正常 CI 部署，驗證登入、角色、月帳、鎖帳、統計及 Excel。舊頁送出不符 epoch 回 409 並重新載入；未解除 hold 的目標拒絕一般 API。
5. 如驗證失敗，部署環境切回保留的原 DB；不自動刪除任何 DB 或 audit。需要新一次演練時，另行準備新空白工作區，不在已還原目標上覆寫。

目前實作／本地測試與正式 GCS 演練的證據分別列於 WORK_HANDOFF；不得以本地測試宣稱正式流量切換通過。

**歷史驗收**（2026/10/15）：V0.25.0 Build 1、PR #450／#451、main runs 38671076691／38671618879 全成功。真實新備份雙副本回讀及 GCS 獨立 D1 重建通過，主站回讀 verified 且已禁止重用目標；原正式帳本／月份合計維持。原 D1 不可用與 R2 缺席由本地實際 workerd／D1／API 測試驗證，正式環境不以停用服務模擬故障。資料／稽核／政策完整比對及資料庫檢查成功不等於 production traffic promotion 已執行。

V0.25.2 normalization：schema 6 僅補 migration 0007 定義的帳戶色碼，排序依 created_at／id；6／7 缺 excelImportRuns 時補空陣列。交易、期初與既有 audit 全部原樣保留，未改寫來源 bytes／manifest／hash。驗證與目標 writer 共用同一 normalized accounting payload 並回報 normalization；新 schema 缺稽核仍拒絕。真實 workerd WASM 與 SQLite／D1 合成資料回歸涵蓋 6／7／10，不據此宣稱真實歷史備份已逐份驗收。

## V0.25.5 每日背景檢查與告警

沿現有 Cron，在每日備份後執行 health check，備份失敗也執行；R2 與 GCS 各驗證最新應有 copy（失敗 copy 不回退較舊成功），R2 36 小時／GCS 120 小時過期視為異常。GCS 未排程日回讀最近實際副本，不要求同日雙副本。全程沿原 digest、catalog 及 fresh SQLite 完整還原 verifier，不寫正式帳本。

`backup_health_state` 是唯一 current health／lease／active incident／notification state；不屬 portable accounting 或 source recovery evidence，還原後需重新檢查目標環境。`backup_restore_checks` 保留既有 append-only 證據，automation actor 不冒用 SUPER_ADMIN。復原 hold 工作區不跑排程。健康結果超過 36 小時，超管登入視為檢查過期警告。

異常透過 private `BACKUP_NOTIFICATIONS` → CYID `ConsumerNotifications`，scope 只從部署 env 取得；送出固定 type 與 UUID，不送帳務、備份內容、Email、resource ID 或 Session。CYID 根據目前受保護超管與已驗證信箱寄固定通知，沿原額度；同一 active incident 去重，failed/deferred 於下次排程重試，新事件最多一小時一封。超管每次進入主頁讀同一健康狀態，異常以 alertdialog 提示，正常不顯示檢查面板。

0011 forward migration 刪除已退役 app-local Session 表並建立 health state，帳務與稽核不變；sandbox 套用同 migration，歷史 V0.25.5 的目標為 schema 11、來源為 6～10；現行已升為目標 schema 15、來源 6～15，仍須區分合成測試與真實來源驗收。首次正式每日 check／實際異常郵件需依真實事件觀察，不用假異常或破壞正式副本作驗收。
