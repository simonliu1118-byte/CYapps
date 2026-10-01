# CYAccountingWeb Project Rules

本文件只記錄 `apps/CYAccountingWeb/**` 的專案補充與例外。共通規則依根目錄 `REPOSITORY_RULES.md`，Public repo 規則依根 `REPO_POLICY.md`。

## 1. 專案定位

- 專案：志遠記帳系統 Web／CYAccountingWeb。
- CYAccountingWeb 是 `CYAccounting` 的獨立 Web 版專案；既有 `apps/CYAccounting/` Windows 版維持獨立正式產品線，除非使用者另行決定，不得因 Web 開發而停止、覆蓋或破壞桌面版。
- 桌面版 CYAccounting 可作為既有功能、資料模型與操作規則的參考來源；Web 版可採不同技術實作與 UI，但核心帳務語意不得無故偏離。

## 2. 正式技術架構

- 前端採瀏覽器原生 HTML／CSS／JavaScript 為基礎；若日後導入前端框架，需以實際維護需求為理由，不為框架而重寫。
- 後端採 Cloudflare Workers。
- 正式資料庫採 Cloudflare D1；D1 binding 固定使用 `DB`。
- 靜態網站由 Cloudflare Workers Static Assets 提供。
- D1 schema 以專案 `migrations/` 內 migration 檔為正式版本來源；正式資料庫結構變更不得只在 Dashboard 手動修改而不留下 migration。

## 3. 帳務核心

- 金額欄位上限維持 7 位數，即 1～9,999,999，除非使用者另行變更。
- 帳戶、收入／支出科目、交易、期初餘額、月份鎖定等核心概念沿用 CYAccounting 既有帳務語意；移植時不得為簡化 Web 實作而破壞既有資料關係或計算邏輯。
- Web 版應以鍵盤高效率輸入為主要桌面操作目標，包含合理的 Enter／Tab 流程與快速輸入；不得因改成網頁而強迫高頻記帳操作大量依賴滑鼠。
- 多使用者與網路環境下的資料一致性、權限及伺服器端驗證必須由 Worker／D1 保證，不得只依賴前端驗證。

## 4. 公開安全與部署

- CYapps 為 Public repository；任何正式帳務資料、D1 export、Cloudflare API token、session secret、登入密碼／雜湊、公司內部資料或 runtime log 均不得提交至 Git。
- Cloudflare API token 與 Account ID 由 GitHub Environment／Secrets 提供；workflow 不得把 secret 值寫入 source、log 或 artifact。
- 正式資料庫 migration 與 Worker 部署優先由可追蹤的 CI/CD 流程執行；若因故需 Dashboard 手動操作，必須確保 Git 中仍有可重建的設定與 migration。
- 未經使用者明確要求，不自動建立對外公開 Release；Web 部署與 GitHub Release 視為不同流程。
- `DB`、`IDENTITY` 等 binding **名稱／contract** 可存在 source；正式 D1 database ID、D1 database name、Identity service 實際名稱與其他 deployment-specific Cloudflare resource identifiers 應由 GitHub Deployment Environment 在正式部署時注入，不得作為公開 Release／Artifact 的固定內容。
- Public source 若需提供 Wrangler／Cloudflare 設定範例，應使用 placeholder／template；Production Deploy 可在 Runner 暫時產生正式 deploy config，但不得 commit、上傳 Artifact 或發布 Release。
- Google OAuth Client Secret、Google Drive token encryption key 等執行期機密只可存在 Cloudflare Secrets；source 只可引用 `env.*` 名稱。OAuth refresh token 只能以受保護形式保存於 runtime storage，不得進 Git、build package、Artifact 或 Release。
- CYAccountingWeb 的 Production Deploy workflow 與任何 Public Release／Artifact build 不得共用「把正式 Secret／resource metadata 烘焙進產物」的流程；公開產物若未通過 repository public-package safety scan，不得發布。

## 5. CYCloud Identity consumer contract

- CYAccountingWeb 採用同 repository 的 `apps/CYCloudIdentity/docs/CONSUMER_INTEGRATION_STANDARD.md` 作為唯一 Shared Identity technical contract，並以 `CYID_CONSUMER_VERSION` 宣告目前實作的 consumer revision；不得另建 app-local Role／Session／App Access／Recovery 語意。
- CYID 是 Workspace identity、permanent credential、Workspace Role、Identity Admin capability、Application Access 與 Identity Session authority。CYAccountingWeb 只保留帳務／domain authorization，不直接讀寫 CYID D1。
- Workspace Role 直接採 `SUPER_ADMIN / ADMIN / USER`；Identity Admin 是 ADMIN capability，不是第四 Role。Role 與 CYACC Application Access 是獨立維度。
- CYAccountingWeb 是 non-core consumer：不得接受首次登入密碼建立一般 Session、不得接收或保存 first-login ticket、不得建立平行「啟用帳號」流程。若 CYID 回傳 first-login-required，必須 fail closed 並引導使用者先到 CY Web 完成 Email 驗證／正式密碼設定。
- 正常登入、Session resolve、logout 與 password recovery 一律透過 private `IDENTITY` Service Binding 呼叫 CYID canonical endpoint；raw provider Session token 只存在 HttpOnly + Secure cookie／受控 server transport，不得進 localStorage、sessionStorage、URL、log、Audit payload 或 CYAccountingWeb business tables。
- CYAccountingWeb 不再 mint 第二個 Identity Session；CYID cutover 後既有 `web_sessions` 不得作 fallback authority。實體 table 的 DROP 必須等新 CYID runtime 完成 development／production acceptance 後，另以後續 forward migration 執行，不得和首次 authority cutover 綁在同一次「migration 先於 Worker deploy」流程。
- CYID Application ID、Workspace ID、Identity provider service target 等 deployment-specific 值只由 Deployment Environment 注入；Public source 只保存 env name／placeholder。
- 既有 Tablet Safari 相容條件保留：CYACC provider Session cookie 採 `SameSite=Lax`、明確 `Expires` 與 navigation-safe login completion，直到真實裝置驗收證明更嚴格 policy 安全為止；這只屬 browser transport presentation，不得形成 Tablet-specific Identity authority。
- permanent password input 固定遵循 CYID 8–16 Unicode 字元邊界；password recovery 必須保持 non-enumerating，不得為顯示 masked Email 而重新洩漏帳號是否存在。

## 6. CYACC business authorization

- CYID Application Access 只回答 Employee 是否可進入 CYAccountingWeb；進入後的帳務能力由 CYAccountingWeb server-side authorization 決定。
- `SUPER_ADMIN` 與 `ADMIN` 保留現有一般帳務操作能力；既有僅限 `SUPER_ADMIN` 的桌面帳本 migration、Backup／Restore 等高權限功能維持原限制。
- `USER` 若有 CYACC Application Access，**允許登入 CYAccountingWeb，但只可檢視帳務資料與匯出 Excel**。
- `USER` 不得新增、修改、刪除交易，不得修改帳戶、科目、常用摘要／快速輸入設定、期初餘額、月份鎖帳，不得執行 Excel import、SQLite migration、Backup／Restore 或其他會修改／管理帳務資料的操作。
- USER read-only 必須在 Worker/API server-side 強制；UI 隱藏、disable 或 RWD presentation 只能作輔助，不得成為授權來源。
- 同一 business capability 必須由共用判斷產生並跨 Desktop／Tablet／Mobile 一致使用，不得為不同 breakpoint 建立不同權限語意。

## 7. 登入入口與 Adaptive UI 邊界

- CYAccountingWeb 使用**獨立登入入口**取代主 App 內 auth overlay。未建立有效 CYID Session 時，主入口不得先載入完整記帳工作區再以 overlay 遮住。
- PC／Tablet／Mobile 共用同一登入頁、同一 CYID login contract 與同一 Session；RWD 只改 presentation，不得建立三套登入流程。
- 一般登入優先採 navigation-safe server flow：登入表單送至 Worker，Worker 完成 CYID permanent-password login、寫入 provider Session cookie 後，以 redirect 進入主 App；不得再以「登入成功後立即 fetch /me」作為 Tablet 成功的必要條件。
- 主 App 入口由 server 先判斷目前 provider Session；沒有有效 Session 則導向獨立登入頁，有有效 Session 才提供主 App。
- 登出必須先要求 CYID revoke provider Session，再清除 CYACC browser cookie；只刪 cookie 不算完整登出。

