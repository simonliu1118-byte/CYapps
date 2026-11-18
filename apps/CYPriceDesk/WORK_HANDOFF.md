# CYPriceDesk 接手入口

目前狀態以 `TODO.md` 與 GitHub `main`／PR／Actions 為準；本文件只協助新對話接手，不新增永久規則或平行進度表。

## 從哪裡開始

1. 依序讀根 `REPOSITORY_RULES.md`、`REPO_POLICY.md`（含 §4.1 CYID Consumer Governance）、本目錄 `PROJECT_RULES.md`，並比對 AITeam `main` 共通規則版本。
2. 讀 `README.md`（架構、API、部署）、`TODO.md`、`CHANGELOG.md` 與 `docs/work-orders/`（整體路線與各版工作單）。
3. CYID 整合以 `apps/CYCloudIdentity/docs/CONSUMER_INTEGRATION_STANDARD.md` 為準；`src/identity.js` 由 `apps/CYAccountingWeb/src/identity-adapter.js` 移植，兩者的 provider 回應驗證與 first-login fail closed 應維持一致。
4. 在 `apps/CYPriceDesk/` 執行 `npm install`、`npm test`、`npm run build`。

## 目前交接（2026-11-17）

- 本專案自 2026-11-17 起由獨立對話負責；目前進度與待辦只看 `TODO.md`。
- 正式部署：Worker `cypricedesk`、D1 `cypricedesk-db`、網域 `price.chihyuancm.com`、GitHub Environment `cypricedesk-production`、Vercel 專案 `cypricedesk-fetcher`（Root Directory `apps/CYPriceDesk/fetcher`）。實際 ID 與權杖不寫入任何文件。
- 部署 workflow `.github/workflows/cypricedesk-deploy.yml` 只能從 `main` 手動觸發；部署識別碼由 workflow 在部署時從 Cloudflare 解析並先遮蔽，GitHub 只保留 `CLOUDFLARE_API_TOKEN`、`CLOUDFLARE_ACCOUNT_ID`、`CYPD_FETCHER_SECRET`。2026-11-18 首次部署 log 已驗證乾淨，不再需要部署後刪除 run log。
- 版本 Major 只由使用者決定；commit 身分 `simonliu1118-byte`（noreply email）；PR 以 squash 合併。
- 介面主色 petrol `#0F5E73`；平台色 蝦皮 `#EE4D2D`、momo `#B5179E`、酷澎 `#346AFF`（使用者 2026-11-17）。

## 架構重點

- Cloudflare Worker（`src/`）＋Workers Static Assets（`public/`）＋D1（`DB`）＋CYID private Service Binding（`IDENTITY`）。前端為原生 HTML／CSS／JS。
- `public/compare.js` 是三平台比價、注意度排序、手動比價計入規則與走勢圖資料整理的唯一實作，瀏覽器與 Worker 共用；`public/core.js` 只剩金額／時間格式與連結驗證。
- 業務資料全站一份；所有寫入與其 audit 在同一個 `DB.batch()`；同時編輯以 `revision` 檢查（409）。
- 部署用的 Worker／D1／Identity service／Application／Workspace ID／入口與 fetcher 網址在部署時由 `.github/scripts/resolve-deploy-runtime.mjs` 依公開主機名稱從 Cloudflare 解析並先 `::add-mask::`，不放 GitHub variables；source 只放 placeholder。要改 binding 須先在 Cloudflare 改。合併 `main` 不部署，只有 `workflow_dispatch` 從 `main` 才部署。

- 自動查價（V1.2.0）：`fetcher/`（Vercel Python，只接受 HMAC 簽章請求）→ Worker `src/search.js`（D1 快取 10 分鐘、同上游間隔 15 秒、blocked 冷卻 30 分鐘）→ `public/search.js`。簽章格式 Worker（`src/fetcher-client.js`）與 fetcher（`fetcher/app.py`）必須一致，兩邊測試共用同一組向量。解析邏輯（`fetcher/momo.py`、`fetcher/feebee.py`、`public/catalog-core.js`）由 sandbox 原樣搬移，改動前先看對應測試。
- 追蹤商品（V1.3.0）：`src/grouping.js`（純函式分組，改規則先看 `tests/tracking.test.js`）、`src/tracking-run.js`（單次查價＋`runScheduled`）、`src/tracking.js`（API）、`public/tracked.js`（V1.4.0 起另有 `chart.js`、`candidates.js`、`pending.js`、`src/manual.js`）。排程與手動查價共用 `runTracked`，查詢走 `src/search.js` 的 `querySource`，不另寫快取或節流。`next_run_at`／`last_run_at` 為 epoch 毫秒，其餘新表時間為 ISO 文字。
- 介面採 CY Web 風格，樣式集中在 `public/styles.css`（token 在檔頭，主色 `#0F5E73`）；新頁面沿用 `.panel`、`.data-table`、`.btn`、`.dialog-surface`，不要另建第二套。
- 測試與 CI 不得連線 momo、飛比或任何外部網站；Worker 測試以 `tests/support/fake-fetcher.js` 取代 `globalThis.fetch`。

## 驗證界線

- 假 fetcher、假 IDENTITY＋node:sqlite 替身的測試、`wrangler deploy --dry-run` 與本機 Playwright 不能替代部署後的實機驗證（真實 CYID、真實 D1、自訂網域）。未實際執行前保持待驗收，不把推測寫成成功。
- 舊 ChatGPT Sites 站與其 localStorage 資料不在本專案處理；搬遷方式見 `README.md`。
