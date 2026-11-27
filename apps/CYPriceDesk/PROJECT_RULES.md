# CYPriceDesk Project Rules

本文件是 CYPriceDesk 唯一的專案規則入口，只記錄 `apps/CYPriceDesk/**` 的補充與例外。共通規則依根 `REPOSITORY_RULES.md`，repository-specific 政策依根 `REPO_POLICY.md`。原型現況、需求、部署紀錄與後續方向記錄於 `README.md`、`TODO.md`、`WORK_HANDOFF.md` 與 `docs/work-orders/`，這些文件不新增永久規則。

## 1. 專案定位

- 價格觀測室：查價與追蹤商品整合的價格比較工具，目標是以追蹤商品的定期查價結果作為調整定價的依據（使用者 2026-11-16）。
- 已上線（https://price.chihyuancm.com/），仍在內部使用階段；功能範圍依工作單逐步擴充。

## 2. 技術架構

- 後端採 Cloudflare Workers，靜態網站由 Workers Static Assets 提供；前端採瀏覽器原生 HTML／CSS／JavaScript。
- 資料庫採 Cloudflare D1，binding 固定為 `DB`；schema 以 `migrations/` 為唯一版本來源，不得只在 Dashboard 手動修改。
- 比價、注意度排序、手動比價計入與走勢圖資料整理以 `public/compare.js` 為唯一實作（規格解析為 `public/catalog-core.js`），Worker 與瀏覽器共用，不建立第二套。

## 3. CYCloud Identity consumer contract

- 採用同 repository 的 `apps/CYCloudIdentity/docs/CONSUMER_INTEGRATION_STANDARD.md` 作為唯一 Shared Identity technical contract，並以 `CYID_CONSUMER_VERSION` 宣告實作的 consumer revision；不得另建 app-local Role／Session／App Access 語意。
- CYPriceDesk 是 non-core consumer：first-login-required 一律 fail closed 並引導至 CY Web，不建立 Session；登入一律經 CY Web 入口（`/auth/sso/callback` 以交換碼兌換，本 App 不提供自己的登入頁），resolve、logout 與兌換都只透過 private `IDENTITY` Service Binding。
- provider Session token 只存在 HttpOnly＋Secure cookie，不得進 localStorage、sessionStorage、URL、log 或 audit。

## 4. 業務授權與資料

- 追蹤商品、比價與價格資料全站只有一份，不分使用者（使用者 2026-11-16）。
- 有 CYPriceDesk Application Access 的人都可以查價、新增與修改追蹤商品、調整售價、手動比價與判斷，不依 Workspace Role 區分（使用者 2026-11-16）。
- 所有寫入都必須寫 append-only audit，與資料變更在同一個 D1 batch 內；操作者只取自已解析的 CYID principal。
- 例外（使用者 2026-11-27）：Grok Bot 以專屬機器憑證呼叫 `/api/grok/*`，只能寫入 Grok 任務、進度、提問與待確認回填等 Grok 專用資料表，並限該任務範圍；不得直接寫入追蹤商品、人工判斷、價格觀測或售價等正式資料。正式資料一律由使用者同意或修正後寫入，操作者為該使用者的 CYID principal。Grok 的寫入同樣在同一 batch 寫 audit，操作者記為固定機器身分，不得冒用使用者。機器憑證不是 CYID Session 或 App Access，不得用於其他路徑。

## 5. 公開安全與部署

- 合併與發布分離：PR 只驗證，合併 `main` 不部署；只有從 `main` 手動啟動的部署 workflow 才發布，且部署前核對 commit 為遠端 `main`。
- D1 database ID／name、Identity service 名稱、Workspace／Application ID、自訂網域等 deployment-specific 值只由 GitHub Deployment Environment 注入；source 只放 placeholder。
- Cloudflare API token 只存在 GitHub Environment secret，不得寫入 source、log 或 artifact。
- Grok 機器憑證與 Grok routine webhook 金鑰只存在 Worker secret（由 GitHub Environment secret 於部署時寫入），不得寫入 source、log、audit 或 Grok 回填資料；輪替以更換 secret 並重新部署為之。
