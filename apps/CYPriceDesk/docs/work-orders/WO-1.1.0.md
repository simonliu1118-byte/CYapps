# WO-1.1.0 價格觀測室改為 Cloudflare Worker＋D1＋CYID（第一階段：手動查價上雲）

- 狀態：已實作，待審查（尚未合併、尚未部署）
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

### 8.1 驗收清單結果

- [x] 未登入：`/` 與 `/index.html` 302 `/login`；`/api/records`、`/api/audit` 401；`/login` 可開啟（`tests/worker.test.js`）。
- [x] 登入成功設定 `cypd_session`（HttpOnly、Secure、SameSite=Lax、Path=/、Expires）；first-login-required 與錯誤密碼不建立 Session，first-login 轉 `/login?error=first-login`，登入頁以 `/api/auth/portal` 取得 `CYWEB_PORTAL_URL` 顯示前往 CY Web 連結（worker 測試＋Playwright 截圖流程）。
- [x] resolve 被拒後下一個請求即 401 並清 cookie；`/` 302 `/login`（worker 測試）。
- [x] 登出先呼叫 provider logout，再清 cookie、302 `/login`（worker 測試檢查 provider 最後一次呼叫為 logout、Session 已撤銷）。
- [x] 新增／修改／刪除／匯入走 API；每個寫入與其 audit 在同一個 `DB.batch()`（測試包裝 `batch` 驗證語句數為 2、2、4、2）；revision 衝突回 409，修改與刪除各驗證一次。
- [x] 原有比較、CSV、範例模式行為不變：`core.test.js` 原封不動通過；`app-flow.test.js` 的事件流程改對真實 Worker 執行並通過。
- [x] 前端沒有 localStorage 正式資料讀寫（`price-desk:v1`、跨分頁保護已移除）；provider token 只在 HttpOnly cookie，不出現在前端、URL、audit（測試檢查 `audit_log` 不含 `cyid_`）。
- [x] 本機：`npm test`、`npm run build`、public-package scan、`wrangler deploy --dry-run`（placeholder 設定）通過。**PR CI 綠燈尚未確認**（尚未開 PR；見 8.2 的 governance 偏離）。
- [x] `VERSION`=1.1.0、`BUILD`=0、`package.json` version=1.1.0、`CYID_CONSUMER_VERSION`=1.0.6；README／TODO／WORK_HANDOFF／CHANGELOG 已更新。
- [x] 截圖（1440／390）：登入頁、比價總覽、新增對話框、操作紀錄，見 8.4。
- [x] commit 身分 `simonliu1118-byte <286269326+simonliu1118-byte@users.noreply.github.com>`。

### 8.2 偏離與需審查決定的事項

1. **Governance Check 會失敗（需審查決定）**：新增 `CYID_CONSUMER_VERSION` 後，`governance-check.yml` 要求同專案 `PROJECT_RULES.md` 含 `CONSUMER_INTEGRATION_STANDARD.md`，目前 `apps/CYPriceDesk/PROJECT_RULES.md` 沒有。工單 §3.7 明定 `PROJECT_RULES.md` 不改，且修改它依 `REPO_POLICY.md` 需走 `governance/*` branch 並更新 `GOVERNANCE_VERSION`／`GOVERNANCE_CHANGELOG.md`，所以本分支未動。本機實測：暫時在 `PROJECT_RULES.md` 加入含該檔名的一行後，governance 的結構檢查通過（已還原）。建議審查者另開 `governance/*` PR，在 `PROJECT_RULES.md` 採用 CYID consumer standard（CYACC 的做法），或決定本分支改由其他方式處理；在此之前本 PR 的 Governance Check 預期為紅。
2. `wrangler.template.jsonc` 設 `"workers_dev": false`（CYACC 為 true），避免多一個 workers.dev 入口繞過自訂網域；`compatibility_date` 與 CYACC 相同。
3. 欄位對照：D1 欄位與 §3.3 完全一致，`checked_at` 對應 `core.js` 的 `checkedAt`，其餘同名，沒有調整。
4. API 回應格式：成功回純 JSON 物件（`{records}`、`{record}`、`{entries}`、`/api/me` 為工單指定的三個欄位）；錯誤回 `{error}`。新增另有 `GET /api/auth/portal`（公開，供登入頁取得 CY Web 連結，沿用 CYACC 的 `account-portal` 作法）。
5. `POST /api/records` 沿用原前端語意：相同商品規格＋連結且較新則合併更新（audit 為 `record.update`），較舊則回 422；`PUT` 重複連結回 422。這些訊息與原前端相同，只是判斷搬到伺服器。
6. 未移植 CYACC adapter 的 Tablet 特例、recovery、角色／寫入權限判斷與 login 後的二次 resolve（`verifyFreshLoginSession`）；保留 provider 回應驗證、first-login fail closed、timeout 與錯誤正規化。cookie 保留 `Max-Age` 與明確 `Expires`。
7. 保留 CYACC 的 HTTP→HTTPS 308（localhost 例外）。注意 `wrangler dev` 會把 request host 改成 custom domain 路由，所以 `npm run dev` 加了 `--local-upstream localhost:8787`，否則本機會被導向 https。
8. `tests/ui.test.cjs`（Playwright 腳本）原本依賴 localStorage 與跨分頁覆蓋保護，已改成對 `tests/support/local-server.mjs`（真實 Worker＋假 IDENTITY＋node:sqlite）執行，跨分頁改為 409 提示並加入操作紀錄檢查；這個 local server 同時用來產生截圖。這是工單 §5 允許的「用 Worker 測試替身起本機 server」，不是新增功能。
9. 部署 workflow 在 deploy 前多跑一次 `npm test`；Environment 名稱與變數清單見 README（Secrets：`CLOUDFLARE_API_TOKEN`、`CLOUDFLARE_ACCOUNT_ID`、`CF_IDENTITY_SERVICE`；Variables：`CF_WORKER_NAME`、`CF_D1_DATABASE_NAME`、`CF_D1_DATABASE_ID`、`CF_CYID_APPLICATION_ID`、`CF_CYID_WORKSPACE_ID`、`CF_CYWEB_PORTAL_URL`、`CF_CYPRICEDESK_CUSTOM_DOMAIN`）。

### 8.3 指令輸出摘要（apps/CYPriceDesk，Node v22.23.4）

- `npm install`：新增 38 個套件，產生並提交 `package-lock.json`（只含 registry.npmjs.org）。
- `npm test`：12 tests，pass 12、fail 0（core 5、Worker 5、前端事件流程 2；node:sqlite 實驗性警告可忽略）。
- `node --check`：`src/*.js`、`public/*.js`、`scripts/*.mjs`、`tests/support/*` 全部通過。
- `npm run build`：`Static Site built in dist/`；`python3 ../../.github/scripts/scan-public-package.py dist`：`Public artifact safety scan passed.`
- `node scripts/render-wrangler.mjs`（placeholder 值）＋`npx wrangler deploy --dry-run --config wrangler.ci.generated.jsonc`：wrangler 4.163.0，Read 5 files from assets，bindings 為 DB、IDENTITY、ASSETS 與三個 vars，`--dry-run: exiting now.`。
- `npx wrangler d1 migrations apply DB --local`：`0001_initial.sql` 成功（5 commands）。
- `wrangler dev`（placeholder 設定）實測：`/login` 200（由 ASSETS 提供）、`/` 302 `/login`、`/api/records` 401、`/styles.css` 200。
- Playwright（真實 Chromium，對 local server）`tests/ui.test.cjs`：`passed: true`，16 項檢查含桌面／平板／手機無水平溢出與無 JavaScript 錯誤。
- Governance 結構檢查（從 `governance-check.yml` 抽出的 python 在 repo 根目錄執行）：因 8.2 第 1 點失敗；暫時加入採用字串後 `Local governance structure OK`。公開 package scanner 自我測試通過。

### 8.4 截圖（不在 repo 內）

`/tmp/claude-0/-home-user/04d2ad10-f9b3-5958-aff8-f556061b5368/scratchpad/wo110/`：`login-1440.png`、`login-390.png`、`overview-1440.png`、`overview-390.png`、`add-dialog-1440.png`、`add-dialog-390.png`、`audit-1440.png`、`audit-390.png`（假資料、假身分「測試員工」）。


## 9. 審查紀錄（審查者填）

### 2026-11-16 審查（Claude）— 結論：通過（待 governance PR 合併後 CI 轉綠）

- CYID：login／resolve／logout 對照 consumer standard §7–§13 無缺漏；first-login fail closed、provider 回應驗證、cookie HttpOnly＋Secure＋SameSite=Lax＋Expires、resolve 失敗清 cookie、provider 故障 503 保留 cookie，皆符合。
- 寫入與 audit 同 batch；修改／刪除以 revision 條件更新，audit 以 `changes() > 0` 綁定，正確。
- 匯入與 POST 合併更新未帶 revision 條件，極少數同時編輯時後寫覆蓋；原型可接受，記錄於此。
- 偏離 2–9 同意。偏離 1（PROJECT_RULES 需採用 CYID standard）由審查者另開 `governance/cypricedesk-cloud-worker` PR 處理。
- 審查者直接修正：`GET /api/audit` 的 `limit` 限制在 1–200。
- 本機重跑：`npm test` 12/12、build、public-package scan 通過；截圖已檢視（登入頁、總覽、新增、操作紀錄）。
