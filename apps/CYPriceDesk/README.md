# CYPriceDesk — 價格觀測室

**V1.3.0：** 新增追蹤商品（定期自動查價、同規格比價、自己的售價紀錄），整站介面改為 CY Web 簡潔商務風格（主色靛藍 `#3B4CCA`）。**V1.2.0：** 新增自動查價（momo、飛比蝦皮、飛比酷澎）、人工規格分類與一鍵加入價格紀錄。**V1.1.0：** 手動查價工具已搬到 Cloudflare Worker＋D1，以 CYID（CYCloud Identity）登入。價格紀錄全站只有一份，有 CYPriceDesk Application Access 的人都能查看、新增、修改、刪除、匯入與匯出，每次寫入都留下 append-only 操作紀錄。自動查價與追蹤商品屬後續工作單（見 `docs/work-orders/README.md`）。

- 正式原始碼位置：`simonliu1118-byte/CYapps` 的 `apps/CYPriceDesk/`。
- 版本來源：`VERSION`（`1.3.0`）、`BUILD`（`0`）；`CYID_CONSUMER_VERSION` 為 `1.0.6`。這只是工程識別，不表示已有正式 GitHub Release 或產品驗收。
- 規則依根 `REPOSITORY_RULES.md`、`REPO_POLICY.md` 與 `PROJECT_RULES.md`；本文件只描述現況。

## 架構

```
瀏覽器 ── 原生 HTML／CSS／JS（public/）
   │
Cloudflare Worker（src/index.js）── Workers Static Assets（ASSETS）
   ├─ IDENTITY（private Service Binding）→ CYCloud Identity：login／resolve／logout
   ├─ DB（D1）：price_records、audit_log、search_cache、upstream_state、listing_classifications、tracked_*（見「追蹤商品」）
   ├─ Cron（*/10 * * * *）→ scheduled()：定期查價
   └─ CYPD_FETCHER_URL（HTTPS，HMAC 簽章）→ fetcher/（Vercel，Python）→ momo、飛比
```

| 檔案 | 責任 |
| --- | --- |
| `src/index.js` | Worker 入口：路由、登入閘門（未登入 `/` 302 `/login`、`/api/*` 401） |
| `src/identity.js` | CYID login／resolve／logout 與 `cypd_session` cookie，由 CYAccountingWeb 的 adapter 移植 |
| `src/records.js` | 價格紀錄 API、D1 存取、audit 寫入（每個寫入與其 audit 在同一個 `DB.batch()`） |
| `src/search.js`、`src/fetcher-client.js` | `GET /api/search`：D1 快取、間隔、冷卻，再以簽章呼叫 fetcher |
| `src/tracking.js`、`src/tracking-run.js`、`src/grouping.js` | 追蹤商品 API、單次查價與排程、自動分組（純函式） |
| `src/classifications.js` | 人工規格分類 API（D1、全站共用、依來源隔離、寫 audit） |
| `fetcher/` | Python 抓價服務（Vercel）：`app.py`（Vercel Flask 預設入口）、`momo.py`、`feebee.py`、`tests/` |
| `public/tracked.js`、`public/shell.js` | 追蹤商品頁；頁面切換、使用者選單 |
| `public/search.js`、`public/catalog-core.js` | 自動查價頁；規格解析與單價統計（瀏覽器與 Worker 共用） |
| `public/core.js` | 驗證、臺北時間、分組、價格比較、CSV、「相同商品規格＋連結保留較新查價」的唯一實作；瀏覽器與 Worker 共用同一份 |
| `public/app.js` | 前端 state、API 呼叫與 render；範例模式只存在記憶體 |
| `public/index.html`／`login.html`／`styles.css` | 主畫面、獨立登入頁（PC／手機共用）、RWD |
| `migrations/` | D1 schema（`0001` 價格紀錄、`0002` 自動查價、`0003` 追蹤商品） |
| `wrangler.template.jsonc`、`scripts/render-wrangler.mjs` | 只含 `__PLACEHOLDER__` 的設定模板，與部署時由環境變數產生設定的 script |
| `scripts/build.mjs` | 產生可供 public-package scan 的 `dist/` |
| `tests/` | `core.test.js`（計價規則）、`worker.test.js`（Worker＋假 IDENTITY＋node:sqlite D1 替身）、`app-flow.test.js`（真實前端事件流程對真實 Worker）、`ui.test.cjs`（Playwright，另備） |

## 登入與授權

- `/login` 為獨立登入頁，表單 POST `/api/auth/login`（員工編號＋密碼 8–16 字元），Worker 呼叫 CYID `POST /v1/identity/login`。成功設定 HttpOnly、Secure、SameSite=Lax、帶 `Expires` 的 `cypd_session` cookie。
- 首次登入（尚未在 CY Web 完成 Email 驗證與密碼設定）一律 fail closed，不建立 Session，登入頁顯示前往 CY Web 的連結。
- 每個受保護請求都以 `POST /v1/identity/session/resolve` 重新驗證；被拒（停用、撤銷 Application Access 等）即清 cookie 並回 401／302。
- 登出：先呼叫 provider logout，再清 cookie。瀏覽器看不到 provider token。
- 沒有 app-local 角色限制：通過 resolve（含 Application Access）即可使用全部功能。

## 資料與 API

價格紀錄全站一份，`price_records` 每筆有 `revision`；`audit_log` 只新增不修改。

| API | 說明 |
| --- | --- |
| `GET /api/me` | 登入者 `{employeeNo, displayName, workspaceRole}` |
| `GET /api/records` | 全部紀錄 |
| `POST /api/records` | 新增；相同商品規格＋連結會依 `core.js` 規則合併更新或拒絕較舊資料 |
| `PUT /api/records/:id` | 修改，body 帶 `revision`；不符回 409「資料已被他人更新，請重新整理」 |
| `DELETE /api/records/:id?revision=N` | 刪除，同樣檢查 revision |
| `POST /api/records/import` | 送預覽確認後的列，一次 batch 寫入，寫一筆 `record.import` audit |
| `GET /api/audit?limit=50` | 最近操作紀錄 |

操作者一律取自 CYID principal，不採用 client 傳入的身分。金額固定為新臺幣；優惠券、會員價等條件記在備註，不自動扣抵。

## 功能與比較語意

- 以「商品名稱＋規格」分組；不同容量、型號或組合不混算。
- 可先存連結為「待查價」，未知價格不列入排名。
- 最低價、並列最低價、金額／百分比差異、最高與最低價差；可切換商品價格或含運總價（含運＝價格＋運費，未知運費排除，免運需填 0）。
- 平台篩選只隱藏其他列，最低價基準仍取同商品規格的全部平台。超過 7 天的查價提示更新。
- CSV／TSV 貼上與 CSV 檔案匯入（預覽驗證）、CSV 匯出。
- 「看看範例」的資料只在瀏覽器記憶體，不會寫入正式資料。
- 「操作紀錄」列出最近 50 筆新增、修改、刪除、匯入、人工分類與追蹤商品相關操作。

## 自動查價（V1.2.0）

側欄「自動查價」（`#auto-query`）分三個來源**分開查詢、分開顯示**：momo、飛比蝦皮、飛比酷澎。飛比一次抓取同時取得蝦皮與酷澎兩組結果，兩個來源共用同一筆快取。飛比結果固定標示「飛比收錄的商品級第三方參考價，來源價格更新時間未確認，非即時或逐規格價格」。

- `GET /api/search?source=momo|shopee|coupang&keyword=&page=`：關鍵字 2–80 字元；momo 可翻頁（最多 5 頁）。同來源＋關鍵字＋頁碼 10 分鐘內用 D1 快取（`fromCache: true`）；同一上游（momo、飛比各自）兩次實際請求至少間隔 15 秒（`rate_limited`）；上游回 `blocked`／`provider_refused` 後該上游冷卻 30 分鐘（`blocked_cooldown`）。只有 `ok`／`no_match`／`no_price` 寫快取，寫入時刪除超過 1 天的舊列。
- `GET／PUT／DELETE /api/classifications`：人工分類（型號、包裝數量、單位、其他規格、納入／不使用）存 D1，全站共用，依來源隔離，`listingKey` 在 momo 為品號、飛比為來源連結；每次異動寫 `classification.update`／`classification.delete` audit。
- 結果可「加入價格紀錄」：開啟新增對話框並預填商品名稱、平台、賣家、連結、價格與查價時間（現在），規格由使用者填；儲存走既有 `POST /api/records`（有 audit）。
- 規格解析（型號、包裝數量、單位、屬性、單位價、同型號彙整）沿用 sandbox `catalog_core.js`；因分類改存 D1（無標題簽章欄位），不再有「標題改變需複核」。

### fetcher（Vercel）

`fetcher/` 沒有網頁，只接受 Worker 帶簽章的請求：`POST /v1/momo`（`{"keyword","page"}`）、`POST /v1/feebee`（`{"keyword"}`）；其他路徑 404、GET 一律 405。簽章：header `x-cypd-timestamp`（Unix 秒）與 `x-cypd-signature`（hex HMAC-SHA256，key＝`CYPD_FETCHER_SECRET`，訊息＝`timestamp + "." + raw body`）；時間差超過 300 秒或簽章不符回 401，未設 secret 回 503。momo 以 never-primp 模擬 Chrome 142／Windows（HTTP/2）、飛比用一般 HTTPS GET，解析邏輯由 sandbox 原樣搬移。

部署步驟（使用者授權後）：

1. Vercel 建立專案，Root Directory 指到 `apps/CYPriceDesk/fetcher`（Python 3.12、`requirements.txt`、`vercel.json`）。
2. 在 Vercel 專案設定環境變數 `CYPD_FETCHER_SECRET`（隨機長字串）。
3. GitHub Environment `cypricedesk-production` 新增 secret `CYPD_FETCHER_SECRET`（同一個值）與 variable `CF_CYPD_FETCHER_URL`（Vercel 網址，HTTPS）。部署 workflow 會在 deploy 後以 `wrangler secret put` 從 stdin 寫入 Worker secret，不印出。

來源限制：momo 為公開搜尋展示價，飛比為商品級第三方參考價；兩者都不等於結帳價、會員價或逐規格價格，也可能被站方限制（此時進入冷卻、不重試）。

## 追蹤商品（V1.3.0）

側欄「追蹤商品」（`#tracked`）：設定要追蹤的商品（名稱、型號、規格屬性、包裝數量與單位、自己的售價與連結、momo／飛比關鍵字、啟用或停用），系統定期自動查價，結果作為調整定價的依據。

**自動分組**（沿用 `catalog-core.js` 的 `parseTitle`，判斷在 `src/grouping.js`）：

| 分組 | 條件 | 顯示 |
| --- | --- | --- |
| 同規格 | 型號相同，且包裝數量、單位、已設定的規格屬性都一致 | 進入比價；momo、飛比蝦皮、飛比酷澎各自列出，也顯示總最低價，每筆標來源 |
| 其他包裝 | 型號相同但數量或單位不同 | 依「數量＋單位」分組顯示價格範圍，不與自己的售價比較 |
| 待確認 | 解析不出型號或數量、標題同時出現多個規格、規格屬性不明或不同 | 等人工判斷 |
| 無關 | 標題不含型號（或是別的型號，如 15301 對 1530-0） | 不顯示，只計數 |

**人工判斷**：待確認或分組有誤的商品可標記「同規格」「其他包裝（填數量＋單位）」「不是同一商品」。判斷以「追蹤商品＋來源＋商品識別（momo 品號、飛比來源連結）」保存，下次查價自動套用；記下當時標題，標題變了就回到待確認；「不是同一商品」不再顯示，可在「已排除」恢復。判斷 API 與介面共用，供之後 Grok BOT 使用。

**排程**：Worker Cron `*/10 * * * *`，只在臺北時間 08:00–22:00 執行，每次最多處理 1 個到期（`next_run_at <= now`，依 `next_run_at` 排序）的啟用商品；查 momo 第 1 頁與飛比一次（走與 `/api/search` 同一個 `querySource`：10 分鐘快取、15 秒間隔、30 分鐘冷卻）。查完 `next_run_at` 為隨機 7–14 天後的 08:00–22:00；新增商品為未來 0–7 天內隨機，讓 1000 個商品自然分散。任一來源被節流、冷卻或失敗，該商品延後 1 小時，其他來源結果照存。詳情頁「立即查價」走同一流程。

**儲存（方案 B，不自動刪除，至少保留 5 年）**：`price_observations`（同規格每一筆）、`group_summaries`（每次的同規格與各其他包裝組的筆數、最低、中位數、最高）、`tracked_runs`（每次查價與各來源狀態）只增不刪；`latest_candidates` 每個追蹤商品只留最近一次完整候選清單，重新查價時依來源覆蓋。估算：1000 個商品約每日 100 次查價、200 次請求；5 年約 150 萬筆、300 MB。

**自己的售價**：只能經「修改售價」更新，每次留紀錄（時間、操作者、改前、改後、備註）並寫 audit。

| API | 說明 |
| --- | --- |
| `GET／POST /api/tracked` | 列表（含最近一次同規格最低價、中位數、待確認筆數）／新增 |
| `GET／PUT /api/tracked/:id` | 詳情（售價紀錄、最近候選、同規格歷次、最近一次狀態）／編輯（帶 `revision`，衝突 409；不能改售價） |
| `PUT /api/tracked/:id/price` | 改售價 `{price, note, revision}` |
| `POST /api/tracked/:id/run` | 立即查價 |
| `PUT／DELETE /api/tracked/:id/judgments` | 人工判斷 `{source, listingKey, verdict: same｜other_package｜not_same, packageQuantity, packageUnit}`／恢復（`?source=&listingKey=`） |

新增、修改、改售價、判斷、立即查價都寫 audit（`tracked.*`、`judgment.*`）；排程自動查價的紀錄在 `tracked_runs`（`trigger='schedule'`），不寫 audit。

## 介面（V1.3.0）

整站採 CY Web 簡潔商務風格（取其視覺語言與數值，不引入框架）：白底左側導覽、頂部頁名與右上角使用者選單（姓名、登出）、白色面板與細框線、灰底表頭資料表、分段按鈕、對話框右上 × 與底部靠右按鈕；主色為靛藍 `#3B4CCA`（使用者指定），圖示為暫用的佔位圖，之後換正式圖示。手機版（≤820px）導覽改為抽屜，表格（≤680px）改為逐筆卡片。

## 本機開發與驗證

```sh
cd apps/CYPriceDesk
npm install
npm test            # node --test：core、catalog-core、Worker、自動查價、追蹤商品、前端事件流程
python -m unittest discover -s fetcher/tests   # 需先 pip install -r fetcher/requirements.txt
npm run build       # 產生 dist/
python3 ../../.github/scripts/scan-public-package.py dist
```

- `npm run dev` 以 `wrangler dev` 啟動 Worker 與本機 D1，需要先設好 `CF_*` 環境變數（值與部署相同，本機測試可用假值；`CF_IDENTITY_SERVICE` 必須是可用的 CYID Worker 才能真正登入）。先以 `npm run db:migrate:local` 套用 migration。
- 沒有 CYID 時，可用測試替身起本機 server：`PORT=4173 SEED=1 node tests/support/local-server.mjs`（任何員工編號與密碼都能登入，資料為記憶體內假資料），本機 server 的 fetcher 是回傳 fixture 的假實作，不連任何外部網站（`NO_GAP=1` 可略過 15 秒間隔，供截圖用）。再用 Playwright 執行 `NODE_PATH=<playwright 套件目錄> TEST_BASE_URL=http://localhost:4173 node tests/ui.test.cjs`（不帶 `SEED`）。

## 部署

部署採合併與發布分離：PR 只驗證；合併 `main` 不會部署；只有從 `main` 手動觸發 `CYPriceDesk Deploy`（`workflow_dispatch`）才會部署。它會核對 `github.sha` 等於遠端 `main`、由 GitHub Environment 產生部署設定、套用 D1 migration 後 `wrangler deploy`。部署用的 D1 ID、Identity service 名稱、Application／Workspace ID 與自訂網域一律由 Environment 注入，source 只放 placeholder，不得 commit。

### GitHub Environment `cypricedesk-production`

Secrets：`CLOUDFLARE_API_TOKEN`、`CLOUDFLARE_ACCOUNT_ID`、`CYPD_FETCHER_SECRET`

Variables：`CF_WORKER_NAME`、`CF_IDENTITY_SERVICE`、`CF_D1_DATABASE_NAME`、`CF_D1_DATABASE_ID`、`CF_CYID_APPLICATION_ID`、`CF_CYID_WORKSPACE_ID`、`CF_CYWEB_PORTAL_URL`、`CF_CYPRICEDESK_CUSTOM_DOMAIN`、`CF_CYPD_FETCHER_URL`

### 首次部署前需要使用者完成

1. 在 CY Web／CYID 註冊 CYPriceDesk Application，取得 Application ID 並授權使用者 Application Access。
2. 建立 Cloudflare D1 database，記下名稱與 ID。
3. 建立 GitHub Environment `cypricedesk-production` 並填入上列 Secrets／Variables。
4. 決定自訂網域並完成 Cloudflare 設定。
5. 回報後再從 `main` 手動觸發部署；實機驗證（登入、停用帳號後 401、新增／修改／刪除／匯入與操作紀錄）在部署後另行進行。

## 舊 Sites 資料搬遷

舊 ChatGPT Sites 站的資料只在各瀏覽器的 localStorage，不會自動帶過來。搬遷步驟：

1. 在舊站按「匯出 CSV」。
2. 登入新站，按「匯入」選擇該 CSV，預覽確認後匯入。
3. 確認資料無誤後，由使用者另行決定是否關閉舊站；本專案不修改或關閉舊 Sites 網站。

Copyright © 2026 C.C. Liu, Chihyuan Co. All Rights Reserved. 授權以 repository 根 `LICENSE` 為準。
