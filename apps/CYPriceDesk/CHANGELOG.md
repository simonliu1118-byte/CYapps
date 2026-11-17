# CYPriceDesk 變更紀錄

> 本文件只記錄歷史，不是規則來源。版本來源為 `VERSION` 與 `BUILD`。

## V1.4.0（Build 0）— 2026-11-17

升 Minor：介面改版與三平台比價。依據 `docs/work-orders/WO-1.4.0.md`。

- 導覽只留「查價」「追蹤商品」「待確認」（含筆數徽章）「操作紀錄」；預設首頁為「查價」。移除比價總覽、自動查價分頁、手動價格紀錄（新增查價、CSV 匯入匯出、`/api/records*`）。
- 查價：關鍵字＋來源下拉（全部／momo／蝦皮／酷澎），結果合併成一張可排序、可依來源篩選的表；每列可設定規格、不使用、加入追蹤；無查詢時顯示最近查價（`GET /api/search/recent`）。
- 追蹤商品：我的售價分蝦皮／momo／酷澎三格（空白不比價）與各自連結；共用關鍵字＋個別來源覆寫；新增選填「CY Web 品號」；每平台獨立比價（差距 5% 以上警示）；預設依「最需要注意」排序；可手動新增比價（30 天內計入）。
- 商品詳情：三張平台卡、單張多線走勢圖（原生 SVG，可切換各線、3 個月／1 年／全部、浮動數值框）、候選商品分頁籤、手動比價表、售價調整紀錄。
- 待確認頁（`GET /api/pending`）：所有追蹤商品的待確認候選依商品分組，直接判斷。
- 比價規則與走勢圖資料整理集中於 `public/compare.js`（Worker 與瀏覽器共用）；`public/core.js` 只留 Worker 與介面仍使用的部分。
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
