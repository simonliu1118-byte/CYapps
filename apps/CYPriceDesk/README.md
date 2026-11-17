# CYPriceDesk — 價格觀測室

**V1.4.0：** 介面改版：統一查價、三平台（蝦皮／momo／酷澎）售價比價、單張多線走勢圖、待確認頁、手動比價；移除比價總覽與手動價格紀錄（含 CSV）。**V1.3.0：** 新增追蹤商品（定期自動查價、同規格比價、自己的售價紀錄），整站介面改為 CY Web 簡潔商務風格（主色已於 V1.4.0 改為 petrol `#0F5E73`）。**V1.2.0：** 新增自動查價（momo、飛比蝦皮、飛比酷澎）、人工規格分類與一鍵加入價格紀錄。**V1.1.0：** 手動查價工具已搬到 Cloudflare Worker＋D1，以 CYID（CYCloud Identity）登入。價格紀錄全站只有一份，有 CYPriceDesk Application Access 的人都能查看、新增、修改、刪除、匯入與匯出，每次寫入都留下 append-only 操作紀錄。自動查價與追蹤商品屬後續工作單（見 `docs/work-orders/README.md`）。

- 正式原始碼位置：`simonliu1118-byte/CYapps` 的 `apps/CYPriceDesk/`。
- 版本來源：`VERSION`（`1.4.0`）、`BUILD`（`0`）；`CYID_CONSUMER_VERSION` 為 `1.0.6`。這只是工程識別，不表示已有正式 GitHub Release 或產品驗收。
- 規則依根 `REPOSITORY_RULES.md`、`REPO_POLICY.md` 與 `PROJECT_RULES.md`；本文件只描述現況。

## 架構

```
瀏覽器 ── 原生 HTML／CSS／JS（public/）
   │
Cloudflare Worker（src/index.js）── Workers Static Assets（ASSETS）
   ├─ IDENTITY（private Service Binding）→ CYCloud Identity：login／resolve／logout
   ├─ DB（D1）：audit_log、manual_comparisons、search_cache、upstream_state、listing_classifications、tracked_*（見「追蹤商品」）
   ├─ Cron（*/10 * * * *）→ scheduled()：定期查價
   └─ CYPD_FETCHER_URL（HTTPS，HMAC 簽章）→ fetcher/（Vercel，Python）→ momo、飛比
```

| 檔案 | 責任 |
| --- | --- |
| `src/index.js` | Worker 入口：路由、登入閘門（未登入 `/` 302 `/login`、`/api/*` 401） |
| `src/identity.js` | CYID login／resolve／logout 與 `cypd_session` cookie，由 CYAccountingWeb 的 adapter 移植 |
| `src/audit.js` | `auditStmt`（每個寫入與其 audit 在同一個 `DB.batch()`）與 `GET /api/audit` |
| `src/manual.js`、`src/pending.js` | 手動比價 CRUD（revision＋audit）；`GET /api/pending` 待確認清單 |
| `src/search.js`、`src/fetcher-client.js` | `GET /api/search`：D1 快取、間隔、冷卻，再以簽章呼叫 fetcher |
| `src/tracking.js`、`src/tracking-run.js`、`src/grouping.js` | 追蹤商品 API、單次查價與排程、自動分組（純函式） |
| `src/classifications.js` | 人工規格分類 API（D1、全站共用、依來源隔離、寫 audit） |
| `fetcher/` | Python 抓價服務（Vercel）：`app.py`（Vercel Flask 預設入口）、`momo.py`、`feebee.py`、`tests/` |
| `public/tracked.js`、`public/shell.js` | 追蹤商品頁；頁面切換、使用者選單 |
| `public/search.js`、`public/catalog-core.js` | 自動查價頁；規格解析與單價統計（瀏覽器與 Worker 共用） |
| `public/compare.js` | 三平台比價、差距與注意度排序、手動比價計入規則、走勢圖資料整理的唯一實作；瀏覽器與 Worker 共用 |
| `public/core.js` | 金額與臺北時間格式、連結驗證（Worker 與瀏覽器共用） |
| `public/app.js`、`ui.js`、`chart.js`、`candidates.js`、`pending.js` | 使用者與操作紀錄頁、共用小工具、走勢圖（原生 SVG）、候選商品與判斷、待確認頁 |
| `public/index.html`／`login.html`／`styles.css` | 主畫面、獨立登入頁（PC／手機共用）、RWD |
| `migrations/` | D1 schema（`0001` 價格紀錄、`0002` 自動查價、`0003` 追蹤商品、`0004` 改版：三平台售價、手動比價） |
| `wrangler.template.jsonc`、`scripts/render-wrangler.mjs` | 只含 `__PLACEHOLDER__` 的設定模板，與部署時由環境變數產生設定的 script |
| `scripts/build.mjs` | 產生可供 public-package scan 的 `dist/` |
| `tests/` | `compare.test.js`（比價與走勢圖資料規則）、`worker.test.js`（Worker＋假 IDENTITY＋node:sqlite D1 替身）、`search.test.js`、`tracking.test.js`、`ui.test.cjs`（Playwright，另備） |

## 登入與授權

- `/login` 為獨立登入頁，表單 POST `/api/auth/login`（員工編號＋密碼 8–16 字元），Worker 呼叫 CYID `POST /v1/identity/login`。成功設定 HttpOnly、Secure、SameSite=Lax、帶 `Expires` 的 `cypd_session` cookie。
- 首次登入（尚未在 CY Web 完成 Email 驗證與密碼設定）一律 fail closed，不建立 Session，登入頁顯示前往 CY Web 的連結。
- 每個受保護請求都以 `POST /v1/identity/session/resolve` 重新驗證；被拒（停用、撤銷 Application Access 等）即清 cookie 並回 401／302。
- 登出：先呼叫 provider logout，再清 cookie。瀏覽器看不到 provider token。
- 沒有 app-local 角色限制：通過 resolve（含 Application Access）即可使用全部功能。

## 資料與 API

所有業務資料全站一份、不分使用者；`audit_log` 只新增不修改，每個寫入與其 audit 在同一個 `DB.batch()`。操作者一律取自 CYID principal。金額固定為新臺幣。

| API | 說明 |
| --- | --- |
| `GET /api/me` | 登入者 `{employeeNo, displayName, workspaceRole}` |
| `GET /api/audit?limit=` | 最近操作紀錄 |
| `GET /api/search?source=momo｜shopee｜coupang&keyword=&page=` | 查價：D1 快取 10 分鐘、同上游間隔 15 秒、blocked 冷卻 30 分鐘；飛比一次抓取同時供蝦皮與酷澎（查價頁「全部」由前端依序呼叫三個來源並合併成一張表） |
| `GET /api/search/recent` | 最近 10 個不同關鍵字（取自共用 `search_cache`，快取保留 1 天，所以只涵蓋最近一天） |
| `GET／PUT／DELETE /api/classifications` | 人工規格設定（「設定規格」，D1、全站共用、依來源隔離、寫 audit） |
| `GET／POST /api/tracked` | 列表（每平台 `{mine,min,median,count,gap,warn}`、`attention`、待確認筆數）／新增 |
| `GET／PUT /api/tracked/:id` | 詳情（三平台比較、走勢圖資料、候選、手動比價、售價紀錄）／編輯（帶 `revision`，衝突 409；不能改售價） |
| `PUT /api/tracked/:id/price` | 調整某平台售價 `{platform, price, note, revision}`，寫售價紀錄與 audit |
| `POST /api/tracked/:id/run` | 立即查價 |
| `PUT／DELETE /api/tracked/:id/judgments` | 人工判斷 `{source, listingKey, verdict: same｜other_package｜not_same, packageQuantity, packageUnit}`／恢復 |
| `POST /api/tracked/:id/manual`、`PUT／DELETE /api/tracked/:id/manual/:mid` | 手動比價 `{platform: shopee｜momo｜coupang｜other, seller, url, price, checkedAt(YYYY-MM-DD), note}`；修改與刪除帶 `revision` |
| `GET /api/pending` | 所有追蹤商品的待確認候選（依商品分組）與總數（導覽徽章） |

新增、修改、調整售價、手動比價、判斷、設定規格、立即查價都寫 audit；排程自動查價的紀錄在 `tracked_runs`（`trigger='schedule'`），不寫 audit。

## 介面（V1.4.0）

- 導覽：查價（預設首頁）、追蹤商品、待確認（徽章）、操作紀錄。
- **查價**：關鍵字＋來源（全部／momo／蝦皮／酷澎）；結果一張表（來源標籤、名稱、賣家、價格、規格、操作），來源／價格／名稱可排序，可依來源篩選；各來源狀態一行提示；momo 可載入更多；每列可「設定規格」「不使用」「加入追蹤」；無查詢時列出最近查價。
- **追蹤商品**：我的售價分蝦皮／momo（MO店+）／酷澎，空白＝該平台不比價；共用關鍵字（必填）＋進階的 momo／飛比覆寫；選填 CY Web 品號（本版只儲存與顯示）。每平台各自比較「我的售價」與「該平台最新一次查價的同規格最低價」：差距 =（我的 − 最低）÷ 最低，≥ 5% 以警示色顯示。手動比價（蝦皮／momo／酷澎）查價日期在 30 天內計入該平台同規格比較，超過顯示「已過期，不計入」，「其他」平台只列出。列表預設「最需要注意」：先依三平台最大正差距，再依待確認筆數，再依名稱。
- **商品詳情**：三張平台卡、走勢圖、候選商品（待確認／同規格／其他包裝／不相關／不是同一商品）、手動比價表、售價調整紀錄。
- **走勢圖**（`public/chart.js`，原生 SVG，不用圖表套件）：市場最低價（實線）、中位數（虛線，預設隱藏）、我的售價（同色粗階梯線）、手動比價（圓點）；可切換各線與 3 個月／1 年／全部；滑鼠移過（手機點按）顯示當日各線數值。平台顏色固定：蝦皮 `#EE4D2D`、momo `#B5179E`、酷澎 `#346AFF`，只用於標籤、線條與卡片色條；主色為 petrol `#0F5E73`。
- 手機（≤ 820px）導覽收進抽屜，表格可橫向捲動但頁面本身不橫向捲動。

### fetcher（Vercel）

`fetcher/` 沒有網頁，只接受 Worker 帶簽章的請求：`POST /v1/momo`（`{"keyword","page"}`）、`POST /v1/feebee`（`{"keyword"}`）；其他路徑 404、GET 一律 405。簽章：header `x-cypd-timestamp`（Unix 秒）與 `x-cypd-signature`（hex HMAC-SHA256，key＝`CYPD_FETCHER_SECRET`，訊息＝`timestamp + "." + raw body`）；時間差超過 300 秒或簽章不符回 401，未設 secret 回 503。momo 以 never-primp 模擬 Chrome 142／Windows（HTTP/2）、飛比用一般 HTTPS GET，解析邏輯由 sandbox 原樣搬移。

部署步驟（使用者授權後）：

1. Vercel 建立專案，Root Directory 指到 `apps/CYPriceDesk/fetcher`（Python 3.12、`requirements.txt`、`vercel.json`）。
2. 在 Vercel 專案設定環境變數 `CYPD_FETCHER_SECRET`（隨機長字串）。
3. GitHub Environment `cypricedesk-production` 新增 secret `CYPD_FETCHER_SECRET`（同一個值）與 variable `CF_CYPD_FETCHER_URL`（Vercel 網址，HTTPS）。部署 workflow 會在 deploy 後以 `wrangler secret put` 從 stdin 寫入 Worker secret，不印出。

來源限制：momo 為公開搜尋展示價，飛比為商品級第三方參考價；兩者都不等於結帳價、會員價或逐規格價格，也可能被站方限制（此時進入冷卻、不重試）。

## 追蹤商品與排程

**自動分組**（沿用 `catalog-core.js` 的 `parseTitle`，判斷在 `src/grouping.js`）：同規格（型號、包裝數量、單位、已設定的規格屬性都一致）進入比價；其他包裝只供參考；待確認等人工判斷；標題不含型號者為無關。人工判斷以「追蹤商品＋來源＋商品識別（momo 品號、飛比來源連結）」保存，下次查價自動套用，標題變了就回到待確認。

**排程**：Worker Cron `*/10 * * * *`，只在臺北時間 08:00–22:00 執行，每次最多處理 1 個到期的啟用商品；每次查 momo 第 1 頁與飛比一次（有效關鍵字＝個別覆寫，否則共用關鍵字；走與 `/api/search` 同一個 `querySource`）。查完 `next_run_at` 為隨機 7–14 天後；任一來源被限制或節流則 1 小時後重試。

**儲存（不自動刪除，至少保留 5 年）**：`price_observations`（同規格每一筆，走勢圖資料來源）、`group_summaries`、`tracked_runs` 只增不刪；`latest_candidates` 每個商品只留最近一次候選清單。我的售價每次調整寫入 `my_price_changes`（含平台）。

## 本機開發與驗證

```sh
cd apps/CYPriceDesk
npm install
npm test            # node --test：compare、catalog-core、Worker、查價、追蹤商品
python -m unittest discover -s fetcher/tests   # 需先 pip install -r fetcher/requirements.txt
npm run build       # 產生 dist/
python3 ../../.github/scripts/scan-public-package.py dist
```

- `npm run dev` 以 `wrangler dev` 啟動 Worker 與本機 D1，需要先設好 `CF_*` 環境變數（值與部署相同，本機測試可用假值；`CF_IDENTITY_SERVICE` 必須是可用的 CYID Worker 才能真正登入）。先以 `npm run db:migrate:local` 套用 migration。
- 沒有 CYID 時，可用測試替身起本機 server：`PORT=4173 SEED=1 NO_GAP=1 node tests/support/local-server.mjs`（任何員工編號與密碼都能登入，資料為記憶體內假資料），本機 server 的 fetcher 是回傳 fixture 的假實作，不連任何外部網站（`NO_GAP=1` 可略過 15 秒間隔，供截圖用）。再用 Playwright 執行 `NODE_PATH=<playwright 套件目錄> TEST_BASE_URL=http://localhost:4173 node tests/ui.test.cjs`（需帶 `SEED=1`；加 `SHOT_DIR=<目錄>` 會輸出 1440／390 截圖）。

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
5. 回報後再從 `main` 手動觸發部署；實機驗證（登入、停用帳號後 401、查價、追蹤商品、手動比價與操作紀錄）在部署後另行進行。

Copyright © 2026 C.C. Liu, Chihyuan Co. All Rights Reserved. 授權以 repository 根 `LICENSE` 為準。
