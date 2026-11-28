# CYPriceDesk 變更紀錄

> 本文件只記錄歷史，不是規則來源。版本來源為 `VERSION` 與 `BUILD`。

## V1.6.0（Build 0）— 2026-11-28

- Grok 依商品條件補完新增追蹤欄位，附欄位依據和關鍵字試搜結果。草稿可再開啟、回答提問或捨棄。
- momo 與飛比酷澎候選可派工判讀規格及選項價格。人工可同意、修正採用或要求再查。同規格價格在複審後更新當輪觀測。
- 商品詳情及待確認頁可刪除當輪候選，並同步更新待確認筆數。
- 新增 D1 migration `0005_grok.sql`、Grok webhook 及機器接口。部署設定見 README，操作約定見 [Grok routine 說明](docs/GROK_ROUTINE.md)。

## V1.5.0（Build 0）— 2026-11-23

升 Minor：改為經 CY Web 登入（使用者 2026-11-23：不保留備用，直接切換並拿掉舊的）。依據 `apps/CYCloudIdentity/docs/SSO_PORTAL_DESIGN.md` §6、`CONSUMER_INTEGRATION_STANDARD.md` §21.3。

- 未登入的頁面請求產生 `state`（cookie `__Host-cypd_sso_state`，10 分鐘）後 303 到 CY Web；`/auth/sso/callback` 驗證 `state`、以 IDENTITY binding 兌換交換碼、設定與舊登入完全相同屬性的 `cypd_session` cookie，並回到原本要去的同源路徑。`/auth/*` 回應一律 `no-store`、`no-referrer`。
- 移除：獨立登入頁（`public/login.html`、`/login`）、`POST /api/auth/login`、`GET /api/auth/portal`、`loginWithCyid`、密碼長度檢查、員工編號／密碼表單、記住我與忘記密碼。首次登入與密碼相關畫面由 CY Web 負責。
- 登出只登出本 App，之後顯示「已登出」頁與「重新登入」按鈕（不自動導向，避免迴圈）。
- `CYID_CONSUMER_VERSION` 1.0.6 → 1.2.0。
- **部署前置**：先在 CY Web「系統登記」→ 設定，為價格觀測室登記登入回跳網址 `https://price.chihyuancm.com/auth/sso/callback`，再部署本版；未登記時使用者無法登入。

## V1.4.0（Build 0）— 2026-11-17

升 Minor：介面改版與三平台比價。依據 `docs/work-orders/WO-1.4.0.md`。

- 導覽只留「查價」「追蹤商品」「待確認」（含筆數徽章）「操作紀錄」；預設首頁為「查價」。移除比價總覽、自動查價分頁、手動價格紀錄（新增查價、CSV 匯入匯出、`/api/records*`）。
- 查價：關鍵字＋來源下拉（全部／momo／蝦皮／酷澎），結果合併成一張可排序、可依來源篩選的表；每列可設定規格、不使用、加入追蹤；無查詢時顯示最近查價（`GET /api/search/recent`）。
- 追蹤商品：我的售價分蝦皮／momo／酷澎三格（空白不比價）與各自連結；共用關鍵字＋個別來源覆寫；新增選填「CY Web 品號」；每平台獨立比價（差距 5% 以上警示）；預設依「最需要注意」排序；可手動新增比價（30 天內計入）。
- 商品詳情：三張平台卡、單張多線走勢圖（原生 SVG，可切換各線、3 個月／1 年／全部、浮動數值框）、候選商品分頁籤、手動比價表、售價調整紀錄。
- 待確認頁（`GET /api/pending`）：所有追蹤商品的待確認候選依商品分組，直接判斷。
- 比價規則與走勢圖資料整理集中於 `public/compare.js`（Worker 與瀏覽器共用）；`public/core.js` 只留 Worker 與介面仍使用的部分。
- 審查修正：手機查價表賣家併入商品欄，價格欄不需捲動即可看到；表格內操作按鈕不換行。調整售價可留空，表示該平台停止比價（記入調價紀錄，走勢圖該段中斷）。
- `migrations/0004_redesign.sql`：重建 `tracked_products`、`my_price_changes`（加 `platform`），新增 `manual_comparisons`，移除 `price_records`。正式庫無業務資料，不搬移舊資料。

## V1.3.2 — 2026-11-17

- 抓價服務（Vercel）入口改為根目錄 `fetcher/app.py`（Vercel Flask 預設入口），移除轉址規則；原轉址會讓 Flask 收到改寫後的路徑，`/v1/momo`、`/v1/feebee` 一律 404。

## V1.3.1 — 2026-11-16

- 登入頁改為與 CY Web 登入卡片相同的版面與數值（標題、必填標示、記住我、忘記密碼？連到 CY Web），主色維持石油藍。
- 網站圖示與側欄品牌改用 CY Icon Family 的 PRC 圖示（AITeam #86）。

## V1.3.0（Build 0）

升 Minor：新增追蹤商品與定期查價。依據 `docs/work-orders/WO-1.3.0.md`。

- 新增「追蹤商品」：自動分組（同規格／其他包裝／待確認／無關）、人工判斷（依追蹤商品＋來源＋商品識別保存，標題變了回到待確認）、自己的售價與修改紀錄、立即查價。
- Worker Cron `*/10`：臺北 08:00–22:00、每次最多 1 個到期商品、查完隨機 7–14 天後再查；`/api/search` 與排程共用 `querySource`（快取、間隔、冷卻）。
- `migrations/0003_tracked_products.sql`（儲存方案 B）；`wrangler.template.jsonc` 加 `triggers.crons`。
- 整站介面改為 CY Web 簡潔商務風格（主色靛藍 `#3B4CCA`，使用者指定），移除行銷式文字與裝飾。

## V1.2.0（Build 0）

升 Minor：新增自動查價能力。依據 `docs/work-orders/WO-1.2.0.md`。

- 新增 `fetcher/`（Vercel Python）：momo（never-primp）與飛比抓價，解析邏輯自 sandbox ScraplingProbe 原樣搬移；只接受 HMAC-SHA256 簽章請求。
- Worker 新增 `GET /api/search`（D1 快取 10 分鐘、同上游 15 秒間隔、blocked 後冷卻 30 分鐘、飛比蝦皮／酷澎共用一次抓取）與 `/api/classifications`（人工分類，D1、全站共用、依來源隔離、寫 audit）。
- 新增「自動查價」頁（momo、飛比蝦皮、飛比酷澎分開查詢）、規格解析與單位價統計、查到的結果一鍵加入價格紀錄。
- `migrations/0002_auto_query.sql`；`wrangler.template.jsonc` 加 `CYPD_FETCHER_URL`；`CYPD_FETCHER_SECRET` 為 Worker secret。
- CI 加 Python 3.12 fetcher 測試；部署 workflow 於 deploy 後寫入 `CYPD_FETCHER_SECRET`。

## V1.1.0（Build 0）

升 Minor：儲存與登入方式改變，使用者明顯感受。依據 `docs/work-orders/WO-1.1.0.md`。

- 手動查價原型從瀏覽器 localStorage（ChatGPT Sites）搬到 Cloudflare Worker＋Workers Static Assets＋D1，以 CYID 登入（`CYID_CONSUMER_VERSION` 1.0.6）。
- 價格紀錄全站一份；有 Application Access 者皆可查看、新增、修改、刪除、匯入、匯出；以 `revision` 處理同時編輯（409）。
- 新增、修改、刪除、匯入皆寫 append-only 操作紀錄（操作者取自 CYID principal），畫面新增「操作紀錄」。
- 側欄與頂部改為顯示登入者姓名與「登出」；新增獨立登入頁 `/login`。
- 比較、CSV、範例模式等原有語意不變；`public/core.js` 仍是驗證／比較／CSV 的唯一實作，Worker 端匯入同一份。
- 新增 `migrations/0001_initial.sql`、`wrangler.template.jsonc`、`scripts/render-wrangler.mjs`；移除 `deploy/hosting.template.json`。
- CI：`cypricedesk-check.yml` 改為 npm ci／測試／語法／建置／scan／wrangler dry-run；新增只能手動觸發的 `cypricedesk-deploy.yml`。

## V1.0.0（Build 0）

初始需求探索原型：瀏覽器 localStorage、ChatGPT Sites 私有部署。
