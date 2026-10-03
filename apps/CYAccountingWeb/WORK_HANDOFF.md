# CYAccountingWeb 目前工作交接

更新：2026/10/03（日本時間）。本文件是目前狀態，不新增永久規則。先讀根 AGENTS → REPOSITORY_RULES → REPO_POLICY → PROJECT_RULES，再讀本文件、README 與 TODO。

## 正式、公開 Release 與工作分支

| 範圍 | 最新已確認狀態 |
| --- | --- |
| 正式功能基準 | V0.22.1 Build 1；main `d822afd779642fbeae745d0f643c5d7d8fb1f1ab`，PR #290 已合併 |
| 正式部署 | [#370](https://github.com/simonliu1118-byte/CYapps/actions/runs/37035827619) 成功，D1 migrations、Worker/static assets、登入及 semantic assets 檢查成功 |
| 公開穩定版 | [V0.22.0](https://github.com/simonliu1118-byte/CYapps/releases/tag/cyaccountingweb-v0.22.0)，tag/source 不覆寫 |
| 平板工作 | V0.22.2 Build 0，`cyaccountingweb/tablet-interface`，[Draft PR #292](https://github.com/simonliu1118-byte/CYapps/pull/292)，未合併／部署／Release |
| 平板功能驗證 head | `8945abf4194097f8f2e67d04be0ff5297b515240`；[應用 CI #373](https://github.com/simonliu1118-byte/CYapps/actions/runs/37093135237) 與 [Governance #961](https://github.com/simonliu1118-byte/CYapps/actions/runs/37093135220) 成功 |
| 治理與 Identity | 共通 2.7.0、repo governance 2.3.27；CYACC consumer 1.0.1，provider 1.0.2／minimum 1.0.0 |

純文件提交不升 VERSION／BUILD，也不表示平板功能已部署。文件同步可能令 main／PR head 前進；以上 SHA 是可追溯的功能及驗證基準，最新狀態另以 GitHub refs／Actions 核對。

## 已完成與共用主路徑

| 功能 | 現行主路徑／邊界 |
| --- | --- |
| Worker、登入、權限 | `src/app.js` → CYID session resolution／server gate；USER 唯讀與 Excel，ADMIN 一般帳務，SUPER_ADMIN 才有永久刪除、備份管理與桌面移轉 |
| 新增記帳 | 所有裝置使用 `saveTransaction`／POST transactions；目前仍共用既有送出等待流程，optimistic 新增列為 TODO |
| 交易編輯 | `persistTransactionUpdate`；desktop inline、dialog、手機／平板 entry edit 共用 payload／summary／source-destination lock／optimistic／rollback |
| 舊查詢競態 | `cyTransactionMutationRevision` 保護月份讀取；成功寫入後不能被早發出的舊回應覆蓋，讀取失敗不能 rollback 已成功寫入 |
| 帳戶與科目 | 一個設定 writer、階層 renderer、group reorder、category reorder／same-kind reparent；排序失敗只還原相關欄位，不覆蓋並行改名 |
| 期初與 Excel | `src/opening-balances.js`／`buildOpeningBalanceSnapshot`；無 override 承接歷史交易，最近 override 為基準，exact-month override 生效 |
| 人工期初 | 原因必填、append-only audit、實際前／後值／CYID principal／時間，鎖帳拒絕；回到 automaticAmount 清除 override 並寫 clear audit，等待 server 成功 |
| 帳戶生命週期 | `src/account-lifecycle.js`；已封存、零交易、最新有效期初零時 SUPER_ADMIN 可永久刪除；早期非零／後續零基準及 audit 保留，已有稽核名稱不可重用 |
| SQLite 移轉 | 本機 sql.js 解析原始 `.db`；normalization／保守合併／occurrence dedupe／strict lock／atomic D1；opening import 產生 override＋migration audit，actor 來自 Session |
| 備份 | `backup-package` → `backup-service` → R2/GCS adapters；一次 D1 export，同 digest 雙副本，provider 失敗獨立；格式與階段見備份文件 |

Migration 0006 已將 `opening_balances` 轉為 `opening_balance_overrides`／append-only `opening_balance_audit` 並 DROP 舊表，schema 6。Runtime 不依賴舊表；歷史 SQL 與桌面來源 schema reader 可保留其原表名。回退只能使用 schema-6-compatible source，不能只將 Worker 回退到依賴舊表的 V0.21.10。舊備份未來復原需明確格式／schema 轉換。

## 平板目前實作

設計與驗收範圍集中於 [TABLET_UI_DESIGN.md](docs/TABLET_UI_DESIGN.md)。設計原始來源為 `cyaccountingweb/docs-tablet-ui-plan` 的 `b71e4108b7b3bd5e165dadeb526db4116c56be18`；使用者另明確要求 native-first，現行實作用原生 account/date/month。

- 一份 tablet CSS owner 取代先前兩塊平板樣式；橫向左表單／右帳本，直向大帳本／底部收合欄。
- 同一 DOM、草稿及 entry edit owner 跨旋轉保留；visualViewport 只改高度。保持展開使用原生 checkbox。
- 交易以可點擊編輯／刪除入口開始；沿用手機 entry editor／canonical writer，沒有另造平板滑動引擎。
- 選取同筆編輯不清除修改，換下一筆先還原原新增草稿；切換看帳月份取消未儲存編輯，不寫入資料。
- 帳戶／科目使用共用 touch/pen sorting；USER 隱藏記帳 rail，授權仍由 server 決定。

## 驗證與待驗收

正式 Build 1 與平板開發的自動測試包含真實 SQLite／Worker 的期初、稽核、角色、鎖帳、名稱／摘要上限、Excel parity、刪除與歷史保留、備份 inner v2／legacy outer v2、雙 provider export-once、桌面移轉最大 SQL/bind/payload 及前端 stale-read／rollback。

平板本機 23 組 regression、JS syntax、workflow YAML/shell、CYID supported window 通過；20 個上傳檔案逐一 read-back 核對 Git blob hash。tablet-ui 另驗證方向／觸控分類、旋轉草稿、鍵盤高度、收合／固定、換筆及切月份不誤存。

本機 Chromium 下載得到截斷／損壞 archive，尚未完成實際 browser layout。模擬測試不取代真實 iPad／Android 觸控、鍵盤、登入、分享與畫面驗收。手機／桌機最終實際記帳驗收、Password Recovery Email/browser、Backup Phase C `x/14`、復原／DR 及 `web_sessions` 退休仍在 TODO。

桌面 production 移轉內容已由使用者於 2026/10/03 確認 OK，這項內容驗收已結案；Public Git 不保存實際帳務筆數、金額、原資料庫或 runtime evidence。

## 下一步與交接注意

1. 在隔離環境預覽平板，不用正式帳務資料做測試素材，取得雙方向瀏覽器及真機驗收。
2. 依驗收結果修正 PR #292；保持共用元件、流程、writer 與權限，之後才確認合併／部署。沒有新的公開 Release 授權。
3. 使用者補齊資料並提供開帳年月後，才處理自動計算基準；目前沒有指定年月，沒有更動正式 baseline。
4. Phase C 只讀 production catalog／UI 記錄實際 `x/14`，不依日期推算，不提前切換 Phase D。

Git connector 曾發生 timeout／工作區積分不足。2026/10/03 平板 branch/tree/commit/ref/PR 寫入及 read-back 已成功；歷史 timeout 不代表 branch／file 不存在，後續請依回傳與 SHA 再確認。
