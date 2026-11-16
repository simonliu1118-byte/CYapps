# CYPriceDesk 接手入口

目前狀態以 `TODO.md` 與 GitHub `main`／PR／Actions 為準；本文件只協助新對話接手，不新增永久規則或平行進度表。

## 從哪裡開始

1. 依序讀根 `REPOSITORY_RULES.md`、`REPO_POLICY.md`（含 §4.1 CYID Consumer Governance）、本目錄 `PROJECT_RULES.md`，並比對 AITeam `main` 共通規則版本。
2. 讀 `README.md`（架構、API、部署）、`TODO.md`、`CHANGELOG.md` 與 `docs/work-orders/`（整體路線與各版工作單）。
3. CYID 整合以 `apps/CYCloudIdentity/docs/CONSUMER_INTEGRATION_STANDARD.md` 為準；`src/identity.js` 由 `apps/CYAccountingWeb/src/identity-adapter.js` 移植，兩者的 provider 回應驗證與 first-login fail closed 應維持一致。
4. 在 `apps/CYPriceDesk/` 執行 `npm install`、`npm test`、`npm run build`。

## 架構重點

- Cloudflare Worker（`src/`）＋Workers Static Assets（`public/`）＋D1（`DB`）＋CYID private Service Binding（`IDENTITY`）。前端為原生 HTML／CSS／JS。
- `public/core.js` 是驗證、分組、比較、CSV 的唯一實作，瀏覽器與 Worker 共用。
- 價格紀錄全站一份；所有寫入與其 audit 在同一個 `DB.batch()`；同時編輯以 `revision` 檢查（409）。
- 部署用的 D1 ID、Identity service 名稱、Application／Workspace ID、網域由 GitHub Environment `cypricedesk-production` 注入，source 只放 placeholder。合併 `main` 不部署，只有 `workflow_dispatch` 從 `main` 才部署。

## 驗證界線

- 假 IDENTITY＋node:sqlite 替身的測試、`wrangler deploy --dry-run` 與本機 Playwright 不能替代部署後的實機驗證（真實 CYID、真實 D1、自訂網域）。未實際執行前保持待驗收，不把推測寫成成功。
- 舊 ChatGPT Sites 站與其 localStorage 資料不在本專案處理；搬遷方式見 `README.md`。
