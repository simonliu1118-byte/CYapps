# WO-1.1.0 價格觀測室改為 Cloudflare Worker＋D1＋CYID（第一階段：手動查價上雲）

- 狀態：待執行
- 分支：`cypricedesk/feature-cloud-worker`　PR：（執行者填）
- 依據：`REPOSITORY_RULES.md`、`REPO_POLICY.md` §2–§4.1、`apps/CYPriceDesk/PROJECT_RULES.md`、`apps/CYCloudIdentity/docs/CONSUMER_INTEGRATION_STANDARD.md`（contract 1.0.6）
- 整體路線：見本目錄 `README.md`

## 0. 執行限制（必讀）

- 不合併、不部署、不發 Release；只在指定分支提交並開 PR。
- 可新增／修改 `.github/workflows/cypricedesk-*.yml`（本單明列），其他 workflow 不動。
- 測試與文件只用假資料；不得提交任何真實 Workspace ID、Application ID、D1 ID、service 名稱、token 或價格紀錄。
- 不改變已定案事項；確需偏離時在完成報告說明，交由審查決定。
- 不加多餘的測試與防護（共通規則 §11.4）：只做本單要求的功能與實際會發生的錯誤處理；不加重複驗證、不會發生的情境檢查、層層 fail-closed、自設上限、安全開關，也不為這些寫測試。CYID 標準本身要求的 fail-closed 行為（§8、§12）不在此限。

## 1. 背景（使用者原話）

- 「把手動查價和自動查價整個整合，我希望能做到我設定特定商品（比方我蝦皮的某個商品），然後定期自動用那個商品去查價給出結果，作為調整定價的依據」
- 「價格紀錄只會有一份，能進來這個站的都能看到」
- 「有使用權的都可查價和編輯，留 audit」

本單是第一階段：把現有手動查價原型（瀏覽器 localStorage、ChatGPT Sites）搬到 Cloudflare Worker＋D1，以 CYID 登入，功能不變。自動查價（WO-1.2.0）與追蹤商品定期查價（WO-1.3.0）是後續工作單。

## 2. 已定案事項

1. 架構比照 `apps/CYAccountingWeb`：Cloudflare Workers＋Workers Static Assets＋D1（binding `DB`）＋ CYID private Service Binding（binding `IDENTITY`）。前端維持原生 HTML／CSS／JS，不導入框架。
2. 價格紀錄全站只有一份（不分使用者）。有 CYPriceDesk Application Access 的人都能看、新增、修改、刪除、匯入、匯出；不分 Role。
3. 每一次新增、修改、刪除、匯入都寫 append-only audit（操作者取自 CYID principal，不採用 client 傳入的身分）。
4. 現有功能語意全部保留：分組、最低價／並列最低、差額、含運總價、平台篩選、7 天未更新提示、待查價、CSV／TSV 貼上與檔案匯入（預覽驗證）、CSV 匯出、相同商品規格＋連結保留較新查價、範例模式（範例不寫入正式資料）。
5. `public/core.js` 是驗證、分組、比較、CSV 的唯一實作；Worker 端 import 同一份檔案做伺服器端驗證，不另寫第二套。
6. 版本 `1.0.0` → `1.1.0`（Minor：儲存與登入方式改變，使用者明顯感受），`BUILD=0`。`CYID_CONSUMER_VERSION=1.0.6`。
7. ChatGPT Sites 的部署模板 `deploy/hosting.template.json` 移除；README 說明舊站資料以 CSV 匯出、新站匯入。舊 Sites 網站不在本單關閉。
8. 部署採合併與發布分離：PR 只驗證；合併 `main` 不部署；只有 `workflow_dispatch`（從 `main`）才部署，比照 `cyaccountingweb-deploy.yml`。部署用的 D1 ID、Identity service 名稱、Application／Workspace ID、自訂網域由 GitHub Environment 注入，source 只放 `__PLACEHOLDER__`。

## 3. 範圍

### 3.1 專案結構（`apps/CYPriceDesk/`）
- `src/index.js`：Worker 入口（fetch handler、路由）。
- `src/identity.js`：CYID login／resolve／logout 與 cookie 處理。**以 `apps/CYAccountingWeb/src/identity-adapter.js` 為範本移植**，保留它的 provider 回應驗證、first-login fail closed、錯誤正規化；拿掉 CYACC 專屬部分（Tablet 特例只保留 `SameSite=Lax`＋明確 `Expires`）。
- `src/records.js`：價格紀錄 API 與 D1 存取、audit 寫入。
- `public/`：沿用現有 `index.html`、`styles.css`、`core.js`、`app.js`；新增 `login.html`（獨立登入頁，PC／手機共用）。
- `migrations/0001_initial.sql`：見 3.3。
- `wrangler.template.jsonc`：仿 CYACC，只保留 `DB`、`IDENTITY`、`ASSETS`、`vars`（`CYID_APPLICATION_ID`、`CYID_WORKSPACE_ID`、`CYWEB_PORTAL_URL`）、custom domain route；全部 placeholder。
- `scripts/render-wrangler.mjs`：從環境變數產生 `wrangler.generated.jsonc`／`wrangler.deploy.jsonc`（仿 CYACC，產物列入 `.gitignore`）。
- `scripts/build.mjs`：保留，產生可掃描的 `dist/`（public 靜態檔）。
- `package.json`：`version` 與 `VERSION` 一致；scripts：`test`、`build`、`dev`、`deploy`、`db:migrate:local`、`db:migrate:remote`；devDependency `wrangler`（^4）。
- `CYID_CONSUMER_VERSION`：`1.0.6`。
- 刪除 `deploy/hosting.template.json`。

### 3.2 登入與授權
- 未登入存取 `/`、`/index.html` → 302 到 `/login`；`/api/*`（登入 API 除外）→ 401 JSON。
- `/login`：表單 POST `/api/auth/login`（員工編號＋密碼 8–16 字元）→ Worker 呼叫 `IDENTITY` `POST /v1/identity/login` → 成功設定 cookie `cypd_session`（HttpOnly、Secure、SameSite=Lax、Path=/、Expires=provider `expiresAt`）→ 302 `/`。失敗回到 `/login` 顯示正規化錯誤；first-login-required 顯示「請先到 CY Web 完成 Email 驗證與密碼設定」並附 `CYWEB_PORTAL_URL` 連結，不建立 Session。
- 每個受保護請求以 `POST /v1/identity/session/resolve`（`Authorization: Bearer`、`x-identity-application`）驗證；拒絕即清 cookie 並 401／302。
- `POST /api/auth/logout`：先呼叫 provider logout，再清 cookie，302 `/login`。
- `GET /api/me`：回傳 `{employeeNo, displayName, workspaceRole}` 給畫面顯示。
- 沒有 app-local 角色限制：通過 resolve（含 Application Access）即可使用全部功能。

### 3.3 D1 schema（`0001_initial.sql`）
```sql
CREATE TABLE price_records (
  id TEXT PRIMARY KEY,               -- crypto.randomUUID()
  product TEXT NOT NULL, spec TEXT NOT NULL DEFAULT '',
  platform TEXT NOT NULL DEFAULT '', seller TEXT NOT NULL DEFAULT '',
  url TEXT NOT NULL DEFAULT '',
  price REAL, shipping REAL,         -- NULL = 未知
  checked_at TEXT,                   -- ISO UTC；NULL = 待查價
  note TEXT NOT NULL DEFAULT '',
  revision INTEGER NOT NULL DEFAULT 1,
  created_at TEXT NOT NULL, created_by TEXT NOT NULL,
  updated_at TEXT NOT NULL, updated_by TEXT NOT NULL
);
CREATE INDEX idx_price_records_group ON price_records(product, spec);
CREATE TABLE audit_log (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  at TEXT NOT NULL, actor_employee_id TEXT NOT NULL, actor_name TEXT NOT NULL,
  action TEXT NOT NULL,              -- record.create / record.update / record.delete / record.import
  entity_id TEXT, before_json TEXT, after_json TEXT
);
CREATE INDEX idx_audit_at ON audit_log(at DESC);
```
欄位名稱依現有 `core.js` 的 offer 物件對應；若現有欄位與上表不同，以 `core.js` 語意為準調整並在完成報告說明。

### 3.4 API
- `GET /api/records` → 全部紀錄（現有原型就是一次載入全部）。
- `POST /api/records` → 新增；伺服器以 `core.js` 驗證。
- `PUT /api/records/:id` → 修改，body 帶 `revision`；revision 不符回 409「資料已被他人更新，請重新整理」。
- `DELETE /api/records/:id?revision=N` → 刪除（同樣檢查 revision）。
- `POST /api/records/import` → 送預覽後確認的列；伺服器套用 `core.js` 既有「相同商品規格＋連結保留較新查價」規則；一次 batch 寫入，寫一筆 `record.import` audit（after_json 記新增／更新筆數與 id 清單）。
- `GET /api/audit?limit=50` → 最近操作紀錄。
- 每個寫入與它的 audit 在同一個 `DB.batch()` 內。

### 3.5 前端
- `app.js`：資料來源由 localStorage 改為上述 API；保留單一 state／mutation／render 結構。寫入成功後更新 state；失敗顯示錯誤並還原（409 時提示重新整理）。
- 移除 localStorage 正式資料讀寫（`price-desk:v1`）與跨分頁覆蓋保護（改由 revision 處理）；範例模式保留在記憶體。
- 側欄／頂部的「私有網站・僅擁有者」「只屬於你的價格筆記」等文字改為顯示登入者姓名與「登出」按鈕。
- 新增「操作紀錄」區塊或對話框，列出 `GET /api/audit` 最近 50 筆（時間、操作者、動作、商品）。

### 3.6 CI 與部署 workflow
- 修改 `.github/workflows/cypricedesk-check.yml`：PR 時 `npm ci`、`npm test`、`node --check` 所有 `src/*.js`、`public/*.js`、`npm run build`、public-package scan `dist`、以 placeholder 值跑 `render-wrangler` 後 `npx wrangler deploy --dry-run`。
- 新增 `.github/workflows/cypricedesk-deploy.yml`：僅 `workflow_dispatch`；核對 `github.sha` 等於遠端 `main`；從 GitHub Environment `cypricedesk-production` 取 secrets／vars 產生 deploy config；`wrangler d1 migrations apply DB --remote` → `wrangler deploy`；固定 concurrency group 不取消進行中的部署。比照 `cyaccountingweb-deploy.yml` 的寫法，但不需要 backup、recovery、release 等 CYACC 專屬步驟。
- 需要的 Environment 名稱與變數清單寫進 README（名稱即可，不寫值）。

### 3.7 文件
- `README.md`：改寫為新架構、本機開發（`npm run dev`、本機 migration）、部署步驟、需要使用者在 CY Web 註冊 Application 與設定 GitHub Environment 的清單、舊 Sites 資料 CSV 搬遷步驟。
- `TODO.md`：更新目前基準與下一步（WO-1.2.0）。
- `WORK_HANDOFF.md`：更新接手入口。
- 新增 `CHANGELOG.md`，記錄 V1.1.0。
- `PROJECT_RULES.md` **不改**（若認為需要，寫在完成報告由審查決定）。

## 4. 不做
- 自動查價、飛比／momo、追蹤商品、排程（WO-1.2.0／1.3.0）。
- 角色權限差異、USER 唯讀。
- Backup／Restore、R2、Excel。
- 實際部署、建立 D1、註冊 CYID Application。
- 關閉或修改 ChatGPT Sites 舊站。

## 5. 實作指引
- CYID：逐條對照 `CONSUMER_INTEGRATION_STANDARD.md` §7–§13、§15；`identity-adapter.js` 的 provider 回應驗證與錯誤對應直接沿用。
- Worker 路由：`assets.run_worker_first` 至少含 `/`、`/index.html`、`/login`、`/login.html`、`/api/*`，讓首頁先過登入判斷。
- 測試（`node --test`）：
  - `core.js` 既有測試保留並調整。
  - Worker 測試以假的 `IDENTITY`（回傳固定 principal／拒絕）與記憶體 D1 替身（可用 `node:sqlite` 實作 `prepare/bind/all/first/run/batch`）跑：未登入 302／401、登入成功設 cookie、first-login fail closed、resolve 拒絕後 401、新增／修改（含 409）／刪除／匯入各寫一筆 audit。
- 本機實際畫面：`npm run dev`（wrangler dev，本機 D1）配假的 IDENTITY 不易，畫面驗證可用 Worker 測試替身起一個本機 server，或在完成報告附 Playwright 截圖（1440 與 390 寬：登入頁、比價總覽、新增對話框、操作紀錄）。

## 6. 驗收清單
- [ ] 未登入：`/` 302 `/login`；`/api/records` 401。
- [ ] 登入成功設定 `cypd_session`（HttpOnly、Secure、SameSite=Lax、Expires）；first-login-required 不建立 Session 並顯示前往 CY Web。
- [ ] resolve 被拒（停用、撤銷 App Access）後下一個請求即 401。
- [ ] 登出先呼叫 provider logout 再清 cookie。
- [ ] 價格紀錄新增／修改／刪除／匯入走 API，寫入與 audit 同一個 batch；revision 衝突回 409。
- [ ] 原有比較、CSV、範例模式行為不變（既有測試通過）。
- [ ] 前端沒有 localStorage 正式資料讀寫；沒有 provider token 出現在前端、URL、log、audit。
- [ ] `npm test`、`npm run build`、public-package scan、`wrangler deploy --dry-run`（placeholder 設定）通過；PR CI 綠燈。
- [ ] `VERSION`=1.1.0、`BUILD`=0、`package.json` version、`CYID_CONSUMER_VERSION`=1.0.6；README／TODO／WORK_HANDOFF／CHANGELOG 已更新。
- [ ] 截圖：登入頁、比價總覽、新增對話框、操作紀錄（1440／390）。
- [ ] commit 身分 `simonliu1118-byte <286269326+simonliu1118-byte@users.noreply.github.com>`。

## 7. 實機驗證要求
部署由使用者授權後另行進行（需先在 CY Web 註冊 CYPriceDesk Application、建立 D1、設定 GitHub Environment）。

## 8. 完成報告（執行者填）

## 9. 審查紀錄（審查者填）
