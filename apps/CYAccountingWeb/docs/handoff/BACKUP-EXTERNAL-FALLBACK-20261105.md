# CYAccountingWeb V1.1.1：外部試還原及分時備援

日期：2026/11/05（台灣時間）。交付 Claude 審查；本文件不代表已合併、部署或正式排程驗收。CYID Consumer Impact: NONE。VERSION=1.1.1、BUILD=0：與最新 V1.1.0 main 整合後發布；原 PR 初稿為 V1.0.3 Build 1。

## 使用者確認範圍

- 現用 Workers Free，先用公開 CYapps repository 執行外部檢查；使用者已知帳務備份會進入 GitHub-hosted runner 處理，不能宣稱零外洩風險。
- 外部未做完時，由 Worker 分批接續，不限兩個時段。只有外部與備援均異常才透過既有 CYID 通知超管；只有外部異常時在網頁顯示，不寄信。
- 日後升級 Workers Paid，驗收 Worker 單次完整試還原後移除外部、分批、短效授權與暫存流程。已列入 TODO。
- 已另外建立每週四 10:00（Asia/Taipei）的使用者提醒，請使用者要求 AI 檢查公開日誌／檔案是否外洩；提醒本身不等於完成稽核。

## 變更後流程與 ownership

| 關注點 | 變更前 | 本次 |
| --- | --- | --- |
| 備份與管理 API | backup-service.js | 沿用；機器端點仍由此模組匯出、app.js 路由 |
| 自動驗證狀態與排程 | backup-service.js 內的兩次全量試還原、D1 checkpoint | backup-verification.js 唯一協調器；R2 active.json 是唯一 job／lease／cursor 狀態 |
| 財務欄位與核對 | backup-package.js、backup-restore.js | 沿用 canonical mapping／插入／完整欄列比較；完整與分批共用，沒有另寫另一套財務 verifier |
| SQLite 引擎 | backup-restore-runtime.js | 沿用 static WASM；每次開獨立 DB，分批從 R2 snapshot 重開 |
| 呈現 | public/backup-ui.js | 同一 health API，區分外部未完成、備援已通過、兩邊異常、狀態不可讀 |
| 通知 | CYID ConsumerNotifications | 沿用固定 type／事件去重，不傳帳務或 Email |

移除原 03:30 做一整份 R2、08:00 做一整份 GCS 的排程驗證路徑及中斷立即寄信行為。手動／受控復原仍使用原完整 verifier。新增的外部與分批 fallback 是使用者明確同意的免費方案暫時執行方式，不新增 observer、裝置 renderer、帳務 writer 或 Identity authority。原 `backup_health_state` 不再擁有 job；只保存少量結案狀態投影及 R2 協調器故障計數，供 R2 故障時保留獨立通知途徑。無 schema migration，schema 16。

## 排程與續跑

- Wrangler 改為單一 `*/5 * * * *`，降低 Free 帳號 Cron 名額使用。由 ScheduledController 時間換算台灣時間：03:30 執行原備份、08:00 原補跑；其他喚醒才處理驗證。**不會每五分鐘匯出帳本。**
- GitHub `cyaccountingweb-backup-verify.yml` 每日台灣 04:10（UTC 20:10）或 main 手動啟動，15 分鐘工作逾時。沒有 PR 事件、Artifact、cache 或備份檔上傳。
- 外部尚未領取工作的寬限至 05:00；領取後給 20 分鐘。明確失敗則下個五分鐘喚醒開始備援。GitHub 排程可能延遲或漏跑，所以不能只靠外部排程。
- 每個 job 凍結 R2／GCS 各自最新應有來源的 ID、digest、schema 與筆數；Phase D 的 GCS 可是較早的週三／週日副本，不要求當日 GCS。失敗或過期來源不回退舊成功。
- 備援分開下載 manifest／data、核對原始 bytes／digest，然後每次最多插入及回讀 500 筆。Accounting 與 recovery context 都分批；最後完整 integrity／FK／總筆數檢查後才寫成功證據。
- 每次 snapshot 及原始快取都採唯一物件 key，CAS 最後公布 cursor。中止／晚到的執行不能覆寫已公布的 snapshot／來源；舊 snapshot 在新 checkpoint 成功後清理。來源快取與孤兒檔在換 job 時只清理該 job namespace，不掃整個 bucket、不刪正式備份。
- 正常持續前進不算失敗，可跨午夜接續；連續三次失敗／失去 lease 才判定備援異常。外部晚到失敗不得蓋掉已完成的成功證據。任一路徑完成後，當日 idle tick 只讀一個小 R2 狀態，不做 D1 查詢或 SQLite。
- 兩邊失敗使用同一 incident，CYID 去重；寄信失敗下次用原 incident 重試。R2 協調器連續故障時，以既有 D1 單列投影記錄、寬限後再通知；若當日已有完整成功則不誤報。若 D1 與 R2 都不可讀，無可靠證據判定兩邊狀態，網頁顯示不可讀，不捏造失敗／成功或寄信結果。

## 公開 repository 的資料邊界

- 備份內容僅在 GitHub runner 記憶體處理；沒有寫入工作目錄、Artifact、cache、Release、summary 或提交。Cloudflare／GCS 儲存金鑰只留 Worker。
- Worker 驗證 GitHub 簽章、issuer、audience、不可變 repository ID、repository 名稱、main ref、指定 workflow_ref、schedule/manual 事件、GitHub-hosted runner 及有效期限；工作記錄只保留 run ID/attempt，不保存 bearer token。
- GitHub 只有讀取該工作凍結的兩份來源與回報少量證據的能力，不能呼叫帳務寫入、CYID Session、還原／切換或列出任意儲存物件。所有回應 no-store。
- runner 使用固定成功／失敗文字，不印例外、HTTP response、來源 ID、digest 或資料。SQL/WASM 輸出關閉；依賴安裝停用 lifecycle scripts，兩個官方 Action 固定完整 commit SHA。
- 保留 Public repository 的風險：GitHub 本身及可修改 main 的信任對象仍在資料處理邊界內；未來程式或依賴變更仍須審查。這些措施不是零外洩保證。

## 用量估算及實際測試

- 本次程式實作／驗證沒有查詢或寫入正式 D1、下載正式備份或寄正式信件；正式 D1 新增讀寫量均為 **0**。前輪診斷查詢不屬於本次測試。全部測試資料為合成資料。
- 正常外部成功：來源 metadata 使用既有 backup_sets 日期索引、copy 主鍵及 LIMIT 1；約十餘次小 metadata 查詢、兩筆成功 proof 及一筆結案投影，沒有重掃正式 transactions。實際 D1 rows_read/written 仍須正式執行後由 meta 回讀，不把傳回列數當掃描量。
- 平常每五分鐘最多一次狀態 GET，約 288 次／日；外部另下載兩 provider 的 manifest／data 四個串流。原備份上傳、回讀及 08:00 補跑成本不變。
- 備援每步：最多 500 筆插入／回讀（僅隔離 SQLite），一個 hold 小查詢、約 2 次狀態 GET／2 次條件 PUT，再加原始快取／snapshot 的 GET、PUT、DELETE。暫存資料不進正式 D1。
- 當前實作每個 batch 會重新解析已驗證且不可覆寫的原始快取並重開 SQLite，避免保存第二套資料格式；這會有讀取放大。約 9 千筆時，完整雙 provider 備援可能累積數百 MB 至約 1 GB 的 R2 內部傳輸；資料越大越多步，**不是固定兩次**。操作次數為數百至千餘次／工作，仍須與同帳號其他服務總量比對，不宣稱零成本或無限擴充。
- 保留現有 8 MiB 還原來源、20,000 筆匯出／recovery context 邊界及 Workers 128 MB 記憶體限制。
- 官方 Free CPU 是 **每次 HTTP／Cron 10ms**。分批及本機成功不證明正式免費環境可完成；WASM 初始化、全檔解析、snapshot 開啟及最終 integrity 仍可能超限。**正式 Free CPU／memory 與高資料量驗收為合併／發布審查重點，不能以本機結果代替。** 外部路徑避免在 Worker 執行這些大量運算。

## 驗證及待審查

本機已通過：backup package/provider/compatibility/service/Phase D、health/schema、verification coordinator、public runner output、schedule、controlled recovery、restore、UI、deploy config/flow、frontend architecture、desktop UI、CYID integration、startup 與 build metadata。

實際 workerd／static WASM 使用 **9,003 筆合成交易**；多個獨立 HTTP 呼叫從 R2 逐批重開快照、每批不超過 500，最後完整回讀通過。原 current／schema 6、7、11、12 完整試還原及雙 provider 測試仍通過。正式 CPU 限額未由本機模擬。

`tests/backup-verification-runner.mjs` 啟動實際 runner 程式，對成功、含識別字串的損壞來源與連線錯誤，驗證 stdout 固定文字、stderr 空白，回報證據沒有資料／token／來源識別。這只能驗證本版輸出，不能取代每週公開內容稽核。

Worker bundle dry-run 通過（僅 CI placeholder 設定，沒有部署）；新增 workflow 只在 main 生效。部署時 `CF_BACKUP_VERIFY_REPOSITORY_ID` 使用 `github.repository_id` 注入，沿現有 development Environment 的 custom domain 變數，沒有新增 Cloudflare/GCS key 給 GitHub。

Claude 審查請核對：來源凍結與 Phase D、CAS／中止／晚到結果、完整欄列核對是否無退化、Public 資料邊界、免費 CPU 驗收、讀寫成本、CYID 去重。PR 通過後仍須依當次發布授權走既有 deployment workflow；本次未合併、部署或建立 Release。

## 2026/11/05 合併發布整合

- 使用者明確授權 PR #596 與 CYWEB PR #433 合併部署；Claude 事後複審仍待，不代填通過。
- 發布前 main 已進到 V1.1.0（#601，來源 b44fe85），故本批整合後版本採 V1.1.1 Build 0，避免覆蓋或回退版本。
- 保留 schema 16 的 pending_confirmation 與 transaction_confirmation_audit；canonical export mapper、完整／分批 SQLite 及 static WASM migration 列表同步整合。新分批合成案例包含非零待確認旗標與一筆確認稽核，必須完整回讀通過。
- 本批相對當前 main 不新增 migration、不改待確認業務規則；原 V1.0.3.txt 歷史不改寫。正式部署與外部實測結果待下節補入。
