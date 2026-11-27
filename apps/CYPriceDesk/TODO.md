# CYPriceDesk TODO

> 本文件是唯一目前進度與待辦入口，不是永久規則來源。各版完成內容見 `CHANGELOG.md`，需求與驗收見 `docs/work-orders/`。

## 目前基準

- 正式站：**V1.5.0（Build 0）**（https://price.chihyuancm.com/），2026-11-23 由 CYapps #643 合併並從 `main` 部署；登入改經 CY Web 單一登入。D1 已套用 `0001`～`0004`。Vercel 抓價服務已連線（V1.3.2 時使用者實測 momo、飛比蝦皮查得到結果）。
- V1.4.0 內容：介面改版（統一查價、三平台售價比價、單張多線走勢圖、待確認頁、手動比價），見 [`WO-1.4.0.md`](docs/work-orders/WO-1.4.0.md)。專案規則目前為 governance 2.3.66。

## 待辦

### V1.5.0 經 CY Web 登入

- [x] 在 CY Web 登記登入回跳網址並部署 V1.5.0（2026-11-23，由 CY Web 對話完成；使用者 2026-11-27 確認已是單一登入）。
- [ ] 部署後實機驗證（正常登入已可用，其餘未逐項確認）：未登入開網址 → 到 CY Web 登入或確認 → 回到價格觀測室；錯誤的 state／重放交換碼顯示錯誤頁；登出後顯示「已登出」頁且不會自動再登入；`cypd_session` cookie 屬性不變（HttpOnly、Secure、SameSite=Lax、無 Domain）。

### V1.4.0 上線

- [x] 合併、從 `main` 手動部署、刪除該次部署 run 的 log（2026-11-17）。
- [ ] 實機驗證：查價「全部」三來源合併、設定規格、加入追蹤、三平台售價與調整（含留空停止比價）、手動比價、走勢圖、待確認頁、立即查價與排程一輪。

### 下一版（使用者 2026-11-17 定案方向）

- [ ] **從 CY Web 匯入追蹤商品**：價格觀測室以 Cloudflare Service Binding 讀 CY Web 商品（品號、品名、規格、單位）與網路平台售價（CY Web `item_online_prices`：蝦／MO／酷…）；CY Web 需新增只供內部呼叫的讀取接口（chihyuan-web 另開 PR）。
- [ ] **我的售價改以 CY Web 為準**：價格觀測室不再手動輸入，只負責市場價、比價與走勢；調價紀錄保存在哪一邊，開工時再與使用者確認。V1.4.0 的「CY Web 品號」欄位用來對應。
- 取代原規劃的 CSV／Excel 批次匯入（不做）。

### Grok Bot 建檔補完與候選規格判讀（設計中，見 [`docs/GROK_BOT_DESIGN.md`](docs/GROK_BOT_DESIGN.md)）

- [x] 使用者在 Grok 手動試 1530-1：可開 momo 與酷澎商品頁、選規格、讀到價格（2026-11-27）。
- [ ] 確認 Grok routine 的 webhook 觸發與帶憑證呼叫價格觀測室 API 可行（實作第一步先打通）。
- [ ] `governance/*` PR：`PROJECT_RULES.md` 寫明 Grok 機器憑證只能寫任務與待確認資料表、稽核方式、憑證保管與輪替（CYapps #650 待合併）。
- [ ] 設計定案後開工作單（試點：momo、酷澎；Grok 不自行搜尋）。
- [ ] 試點跑通後訂用量上限（每次任務筆數、每日任務數、再查次數；使用者 2026-11-27：先不設）。
- [ ] 蝦皮：使用者替 Bot 準備蝦皮帳號後加入判讀。
- [ ] 商品照片輔助建檔（需 R2）。

### 之後

- [ ] 價格異動提醒（某平台市場最低低於我的售價超過一定比例時通知，可接 CY Web 通知）；等資料累積後再定門檻。
- [ ] 部署流程改為與 CYAccountingWeb 一致（使用者 2026-11-16）：改用 `apps/CYAccountingWeb/scripts/resolve-cyid-production-runtime.mjs` 從正式 CYID 讀取並核對 service、Workspace ID，部署環境只保留本 App 自己的值；配合全站 CYID 部署命名統一一起處理。
- [x] **部署 log 識別值**（使用者 2026-11-16 選方案 2；governance 2.3.62 實作）：CYPriceDesk、CYAccountingWeb 部署已改為由公開主機名稱經 Cloudflare API 解析並先 `::add-mask::`（共用 `.github/scripts/resolve-deploy-runtime.mjs`），CYID D1 ID 改為 secret。CY Web 已於 chihyuan-web 完成。
  - [x] 2026-11-18 合併（#628）後首次 CYPriceDesk 部署：完整 log 不含 Worker／D1／Identity service／Application／Workspace／fetcher 網址／workers.dev 子網域。**自此停止「每次部署後刪除 run log」**。
  - [x] 同日首次 CYAccountingWeb 部署：log 乾淨（只出現 CI 假值）。
  - [ ] CYID（development／production）首次部署時同樣檢查（需先建立 secret，見下）。
  - [ ] 使用者可刪除 GitHub variables（CYPriceDesk Environment `cypricedesk-production`）：`CF_IDENTITY_SERVICE`、`CF_WORKER_NAME`、`CF_D1_DATABASE_NAME`、`CF_D1_DATABASE_ID`、`CF_CYID_APPLICATION_ID`、`CF_CYID_WORKSPACE_ID`、`CF_CYWEB_PORTAL_URL`、`CF_CYPD_FETCHER_URL`、`CF_CYPRICEDESK_CUSTOM_DOMAIN`。
  - [ ] CYAccountingWeb（Environment `development`）可刪除：`CF_WORKER_NAME`、`CF_D1_DATABASE_NAME`、`CF_D1_DATABASE_ID`、`CF_R2_BACKUP_BUCKET`、`CF_CYACCOUNTINGWEB_CUSTOM_DOMAIN`；`CF_BACKUP_TOPOLOGY` 是模式開關（`legacy_gcs`／`parallel_dual_provider`），不是識別碼，保留。
  - [x] CYID：開發版 CYID 已不存在（2026-11-18 確認），開發版部署已移除，`CYID_D1_DATABASE_ID`（secret 與 variable）都不再使用（governance 2.3.63）。
- [ ] `group_summaries` 自 V1.4.0 起前端不再使用（走勢圖改由 `price_observations` 計算），仍照常寫入；確認不需要後可移除。

### 待使用者手動處理

- [x] 刪除 Vercel 舊原型專案與 sandbox 兩個原型分支（使用者 2026-11-18 完成）。

## 已知限制

- 飛比為商品級第三方參考價，來源價格更新時間未確認；蝦皮多規格商品無法確認逐規格售價。
- 「最近查價」取自查價快取，只涵蓋最近一天。
- 無角色差異（有使用權者皆可寫入）、無 Backup／Restore、無資料匯出。
- 金額固定為新臺幣；優惠券、會員價記在備註，不自動扣抵。
