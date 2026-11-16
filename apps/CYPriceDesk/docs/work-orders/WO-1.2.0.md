# WO-1.2.0 自動查價：抓價服務（Vercel）＋站內查詢與一鍵加入價格紀錄（第二階段）

- 狀態：待執行
- 分支：`cypricedesk/feature-auto-query`（自 `cypricedesk/feature-cloud-worker` 分出，WO-1.1.0 合併後再合併 `main`）　PR：（執行者填）
- 依據：`apps/CYPriceDesk/PROJECT_RULES.md`（governance 2.3.60 版）、WO-1.1.0、本目錄 `README.md` 整合路線
- 來源程式：sandbox `governance/scrapling-probe-init` 分支 `apps/ScraplingProbe/site/`（`app.py`、`feebee_search.py`、`static/catalog_core.js`、`static/catalog_ui.js`、`tests/`）；本機副本在 `/home/user/scrapling/apps/ScraplingProbe/site/`

## 0. 執行限制（必讀）

- 不合併、不部署、不發 Release；只在指定分支提交並 push。
- 可修改 `.github/workflows/cypricedesk-check.yml`、`cypricedesk-deploy.yml`；其他 workflow 不動。
- 測試與文件只用假資料／離線 HTML、JSON fixture；**測試與 CI 不得連線 momo、飛比或任何外部網站**。
- 不改變已定案事項；確需偏離時在完成報告說明。
- 不加多餘的測試與防護（共通規則 §11.4）。

## 1. 背景（使用者原話）

- 「把手動查價和自動查價整個整合」
- 「先放 vercel，現在都還在個人測試階段」
- 「模擬連線特徵是合法的」（momo 維持原本 never-primp Chrome 模擬方式）

## 2. 已定案事項

1. 抓價維持 sandbox 原本的 Python 程式與方法：momo 用 never-primp（Chrome 142／Windows、HTTP/2），飛比用一般 HTTPS GET；**解析邏輯原樣搬移**（`parse_results`、`parse_all_results`、`_card` 等），只拿掉網頁畫面、Basic 密碼閘門與行程內快取／節流（改由 Worker＋D1 負責）。
2. 抓價服務放在本專案 `fetcher/`，部署到 Vercel（使用者自行把 Vercel 專案 Root Directory 指到 `apps/CYPriceDesk/fetcher`）。它沒有網頁，只接受 Worker 帶 HMAC 簽章的請求。
3. 飛比一次查詢同時回傳蝦皮與酷澎兩組結果；站內三個來源（momo、飛比蝦皮、飛比酷澎）分開查詢、分開顯示，飛比結果固定標示「飛比收錄的商品級第三方參考價，來源價格更新時間未確認，非即時或逐規格價格」。
4. 快取與節流放 Worker＋D1，全站共用：同來源＋關鍵字＋頁碼 10 分鐘內用快取；同一上游（momo、飛比各自）兩次實際請求至少間隔 15 秒；上游回 blocked 後該上游冷卻 30 分鐘。
5. 查到的結果可「加入價格紀錄」：開啟既有新增對話框並預填商品名稱、平台、賣家、連結、價格、查價時間（現在），規格由使用者填；儲存走既有 `POST /api/records`（有 audit）。
6. 規格解析沿用 sandbox `catalog_core.js`（型號、包裝數量、單位、屬性推測、單位價）；人工分類由 localStorage 改存 D1、全站共用、寫 audit。三個來源共用同一套解析與 UI，但分類資料以 `source` 隔離。
7. 版本 `1.1.0` → `1.2.0`、`BUILD=0`。

## 3. 範圍

### 3.1 抓價服務 `apps/CYPriceDesk/fetcher/`
- `api/index.py`（Flask app，Vercel Python runtime）：
  - `POST /v1/momo`，body `{"keyword":str,"page":1..5}` → `{"status","raw_count","results":[{title,code,price,relevance,match_label,url}],"checked_at"}`。
  - `POST /v1/feebee`，body `{"keyword":str}` → `{"status","source_url","checked_at","markets":{"shopee":{"status","results":[...]},"coupang":{...}}}`。
  - 其他路徑 404；GET 一律 405。
- `momo.py`、`feebee.py`：由 sandbox `app.py` 的 momo 部分與 `feebee_search.py` 搬移；`normalize_keyword` 等共用驗證保留。
- 簽章驗證：header `x-cypd-timestamp`（Unix 秒）與 `x-cypd-signature`（hex HMAC-SHA256，key＝環境變數 `CYPD_FETCHER_SECRET`，訊息＝`timestamp + "." + raw body`）；時間差超過 300 秒或簽章不符回 401；環境變數未設回 503。用 `hmac.compare_digest`。
- `requirements.txt`（Flask、never-primp，版本範圍同 sandbox）、`vercel.json`（`maxDuration` 30）、`.python-version`（3.12）。
- `tests/`：搬移 sandbox `test_app.py`、`test_feebee_search.py` 中的解析測試（離線 fixture），刪除與網頁畫面、Basic 密碼、行程內快取有關的測試；新增簽章通過／不通過、未設 secret 各一個測試。

### 3.2 Worker
- `src/fetcher-client.js`：以 Web Crypto 計算 HMAC、呼叫 `env.CYPD_FETCHER_URL`；逾時 25 秒。
- `src/search.js`：`GET /api/search?source=momo|shopee|coupang&keyword=&page=`：
  - keyword 正規化規則同 sandbox（2–80 字元）。
  - 讀 `search_cache`（10 分鐘內命中直接回傳，回應帶 `fromCache: true`）。飛比的 shopee 與 coupang 共用同一筆 `feebee` 快取（一次抓兩組）。
  - 未命中：查 `upstream_state`（`momo`、`feebee` 兩列：`last_request_at`、`blocked_until`）；冷卻中回 `blocked_cooldown`；距上次不足 15 秒回 `rate_limited`（附剩餘秒數）；否則先更新 `last_request_at` 再呼叫 fetcher（條件式 UPDATE 防止兩個請求同時通過）。
  - 結果 `blocked`／`provider_refused` → 設 `blocked_until = now + 30 分鐘`；`ok`／`no_match`／`no_price` 才寫快取。
- `src/classifications.js`：`GET /api/classifications?source=`、`PUT /api/classifications`（`{source, listingKey, model, quantity, unit, attributes, decision}`）、`DELETE`；寫 audit（`classification.update`／`classification.delete`）。`listingKey`：momo 用品號，飛比用來源連結。
- `wrangler.template.jsonc` 加 `vars.CYPD_FETCHER_URL`（placeholder）；`CYPD_FETCHER_SECRET` 是 Worker secret。
- `migrations/0002_auto_query.sql`：`search_cache(source, keyword, page, payload_json, fetched_at, PRIMARY KEY(source,keyword,page))`、`upstream_state(upstream PRIMARY KEY, last_request_at, blocked_until)` 預先插入 `momo`、`feebee` 兩列、`listing_classifications(source, listing_key, model, quantity, unit, attributes_json, decision, updated_at, updated_by, PRIMARY KEY(source, listing_key))`。快取只保留 10 分鐘內有效資料：寫入新快取時同時刪除 `fetched_at` 超過 1 天的舊列。

### 3.3 前端
- 側欄新增「自動查價」頁（同一個 `index.html`，以 hash 切換；不另開第二套 app 狀態）。
- 頁面：來源切換（momo／飛比蝦皮／飛比酷澎）、關鍵字、momo 頁碼（上一頁／下一頁，最多 5 頁）、排序（相關程度、價格低到高、高到低）、結果表格（商品、價格、賣家／來源、連結、規格解析、「加入價格紀錄」）。狀態訊息沿用 sandbox `index.html` 的文字（rate_limited、blocked 等）；飛比區固定顯示 2.3 的說明。
- 規格解析與人工分類：移植 `catalog_core.js`（放 `public/catalog-core.js`）與 `catalog_ui.js` 的 UI（分類對話框、篩選、同型號價格彙整），資料改走 `/api/classifications`。
- 「加入價格紀錄」：開啟既有新增對話框並預填（2.5）。

### 3.4 CI／部署
- `cypricedesk-check.yml`：加 Python 3.12，`pip install -r fetcher/requirements.txt` 與 `python -m unittest discover -s fetcher/tests`。
- `cypricedesk-deploy.yml`：deploy 後以 `wrangler secret put CYPD_FETCHER_SECRET` 從 Environment secret `CYPD_FETCHER_SECRET` 寫入（stdin，不印出）；新增 var `CF_CYPD_FETCHER_URL`。

### 3.5 文件
- README：自動查價說明、fetcher 部署到 Vercel 的步驟（Root Directory、環境變數 `CYPD_FETCHER_SECRET`）、Worker secret 設定、來源限制說明。
- TODO、WORK_HANDOFF、CHANGELOG（V1.2.0）。

## 4. 不做
- 追蹤商品、排程、自己的售價、查價歷史（WO-1.3.0）。
- momo／飛比以外的來源、蝦皮逐規格價格。
- 刪除 sandbox 專案（審查者在本單合併後處理）。

## 5. 實作指引
- 先讀 sandbox 原始檔與其 `README.md`、`STANDALONE_SITE_PLAN.md`（了解語意與限制），再搬移；解析函式不要重寫。
- Worker 測試沿用 WO-1.1.0 的假 IDENTITY 與 node:sqlite D1；fetcher 以假的 `fetch` 回傳 fixture JSON。至少測：快取命中不呼叫 fetcher、15 秒內第二次回 rate_limited、blocked 後冷卻、飛比一次抓取供兩個來源使用、簽章格式與 fetcher 端驗證一致（同一組 secret／body／timestamp 兩邊算出相同簽章）。
- 截圖用 WO-1.1.0 的本機 server，fetcher 以假資料回應。

## 6. 驗收清單
- [ ] fetcher：簽章正確才處理；momo／飛比解析測試全過；不含網頁與密碼閘門。
- [ ] Worker：快取、15 秒間隔、30 分鐘冷卻、飛比共用一次抓取皆符合 2.4。
- [ ] 自動查價頁三個來源分開查詢與顯示，飛比說明固定顯示；狀態訊息正確。
- [ ] 「加入價格紀錄」預填正確，儲存後出現在比價總覽且有 audit。
- [ ] 人工分類存 D1、全站共用、依來源隔離、有 audit。
- [ ] `npm test`、Python unittest、build、public-package scan、`wrangler deploy --dry-run` 通過。
- [ ] `VERSION`=1.2.0、`BUILD`=0、`package.json`；README／TODO／WORK_HANDOFF／CHANGELOG 已更新。
- [ ] 截圖（1440／390）：自動查價 momo 結果、飛比蝦皮結果、分類對話框、加入價格紀錄預填對話框。
- [ ] commit 身分 `simonliu1118-byte`。

## 7. 實機驗證要求
部署由使用者授權後進行：Vercel 建立 fetcher 專案並設 `CYPD_FETCHER_SECRET`；GitHub Environment 加 `CYPD_FETCHER_SECRET`（secret）與 `CF_CYPD_FETCHER_URL`（var）；部署後各來源單次實測。

## 8. 完成報告（執行者填）

## 9. 審查紀錄（審查者填）
