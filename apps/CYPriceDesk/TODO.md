# CYPriceDesk TODO

> 本文件是唯一目前進度與待辦入口，不是永久規則來源。各版完成內容見 `CHANGELOG.md`，需求與驗收見 `docs/work-orders/`。

## 目前基準

- 正式站：V1.3.2（https://price.chihyuancm.com/），2026-11-17 部署；Vercel fetcher `cypricedesk-fetcher` 已連線，使用者實測 momo、飛比蝦皮查得到結果。
- **V1.4.0（Build 0）**：介面改版（統一查價、三平台售價比價、單張多線走勢圖、待確認頁、手動比價），見 [`WO-1.4.0.md`](docs/work-orders/WO-1.4.0.md)。分支 `cypricedesk/v1.4.0-redesign`，本機測試與截圖通過、審查通過；**待使用者確認後合併、部署**。部署會套用 `0004_redesign.sql`（重建追蹤商品相關表、移除 `price_records`；2026-11-17 正式庫無業務資料）。

## 待辦

### V1.4.0 上線

- [ ] 合併、從 `main` 手動部署、刪除該次部署 run 的 log。
- [ ] 實機驗證：查價「全部」三來源合併、設定規格、加入追蹤、三平台售價與調整（含留空停止比價）、手動比價、走勢圖、待確認頁、立即查價與排程一輪。

### 下一版（使用者 2026-11-17 定案方向）

- [ ] **從 CY Web 匯入追蹤商品**：價格觀測室以 Cloudflare Service Binding 讀 CY Web 商品（品號、品名、規格、單位）與網路平台售價（CY Web `item_online_prices`：蝦／MO／酷…）；CY Web 需新增只供內部呼叫的讀取接口（chihyuan-web 另開 PR）。
- [ ] **我的售價改以 CY Web 為準**：價格觀測室不再手動輸入，只負責市場價、比價與走勢；調價紀錄保存在哪一邊，開工時再與使用者確認。V1.4.0 的「CY Web 品號」欄位用來對應。
- 取代原規劃的 CSV／Excel 批次匯入（不做）。

### 之後

- [ ] 價格異動提醒（某平台市場最低低於我的售價超過一定比例時通知，可接 CY Web 通知）；等資料累積後再定門檻。
- [ ] 部署流程改為與 CYAccountingWeb 一致（使用者 2026-11-16）：改用 `apps/CYAccountingWeb/scripts/resolve-cyid-production-runtime.mjs` 從正式 CYID 讀取並核對 service、Workspace ID，部署環境只保留本 App 自己的值；配合全站 CYID 部署命名統一一起處理。
- [ ] 部署 log 識別值（使用者 2026-11-16 選方案 2）：部署時向 Cloudflare／CYID 查詢 D1 ID、Workspace ID 等，查到先 `::add-mask::` 再使用，GitHub 只存權杖與名稱；完成前每次部署後刪除該次 run 的 log。CYACC、CY Web 有同樣問題，一起處理。
- [ ] `group_summaries` 自 V1.4.0 起前端不再使用（走勢圖改由 `price_observations` 計算），仍照常寫入；確認不需要後可移除。

### 待使用者手動處理

- [ ] 刪除 sandbox 分支 `governance/scrapling-probe-init`、`claude/upbeat-ramanujan-3fwh1c`（自動化推送刪除被斷線）。
- [ ] 刪除 Vercel 舊專案 `momo-keyword-price-prototype`（原型；正式抓價服務是 `cypricedesk-fetcher`，不要刪）。

## 已知限制

- 飛比為商品級第三方參考價，來源價格更新時間未確認；蝦皮多規格商品無法確認逐規格售價。
- 「最近查價」取自查價快取，只涵蓋最近一天。
- 無角色差異（有使用權者皆可寫入）、無 Backup／Restore、無資料匯出。
- 金額固定為新臺幣；優惠券、會員價記在備註，不自動扣抵。
