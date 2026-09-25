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
