# CYPriceDesk 變更紀錄

> 本文件只記錄歷史，不是規則來源。版本來源為 `VERSION` 與 `BUILD`。

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
