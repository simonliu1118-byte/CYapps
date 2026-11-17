# CYPriceDesk TODO

> 本文件是唯一目前進度與待辦入口，不是永久規則來源。

## 目前基準 — V1.4.0（Build 0）

- WO-1.4.0 已實作於分支 `cypricedesk/v1.4.0-redesign`：統一查價、三平台售價比價、單張多線走勢圖、待確認佇列、手動比價（見 `docs/work-orders/WO-1.4.0.md`）。本機測試與截圖已完成；**尚未部署、未對 momo／飛比做實機測試**。
- **下一版：CY Web 商品匯入與我的售價改以 CY Web 為準**（取代 CSV 批次匯入；本版「CY Web 品號」欄位只儲存與顯示，供下一版連結）。價格異動提醒留待之後版本。

## V1.3.0 基準

- WO-1.3.0 已實作於分支 `cypricedesk/feature-tracked-products`（建立於 V1.2.0 之上）：追蹤商品、Cron 定期查價、自動分組與人工判斷、自己的售價紀錄、CY Web 風格介面。本機 `npm test`、Python unittest、build、scan、`wrangler deploy --dry-run`（含 crons）通過；**尚未部署、未對 momo／飛比做實機測試**。細節見 `docs/work-orders/WO-1.3.0.md` §8。

### V1.2.0 基準

- WO-1.2.0 已實作於分支 `cypricedesk/feature-auto-query`：`fetcher/`（Vercel Python）、Worker 快取／間隔／冷卻、自動查價頁、D1 人工分類、一鍵加入價格紀錄。本機 `npm test`、Python unittest、build、scan、`wrangler deploy --dry-run` 通過；**尚未部署、未對 momo／飛比做實機測試**（測試一律離線 fixture）。細節見 `docs/work-orders/WO-1.2.0.md` §8。

### V1.1.0 基準

- WO-1.1.0 已實作於分支 `cypricedesk/feature-cloud-worker`：手動查價搬到 Cloudflare Worker＋D1＋CYID，價格紀錄全站一份，所有寫入留 audit。細節見 `docs/work-orders/WO-1.1.0.md` §8 完成報告。
- 本機驗證（假 IDENTITY＋node:sqlite D1 替身）：`npm test`、`npm run build`、public-package scan、`wrangler deploy --dry-run`（placeholder 設定）通過；Playwright 於真實 Chromium 通過。
- **尚未部署、尚未實機驗證**；PR 審查與使用者授權前不合併、不部署。

## 待使用者完成（部署前）

- [ ] 在 CY Web／CYID 註冊 CYPriceDesk Application 並授權使用者 Application Access。
- [ ] 建立 Cloudflare D1、GitHub Environment `cypricedesk-production`（名稱清單見 README）與自訂網域。
- [ ] 授權後從 `main` 手動觸發 `CYPriceDesk Deploy`，再做實機驗證：登入、首次登入導向 CY Web、停用帳號後 401、新增／修改／刪除／匯入、操作紀錄、舊站 CSV 匯出後匯入。
- [ ] 決定舊 Sites 站何時關閉（本專案不處理）。
- [ ] Vercel 建立 fetcher 專案（Root Directory `apps/CYPriceDesk/fetcher`）並設 `CYPD_FETCHER_SECRET`；GitHub Environment 加 secret `CYPD_FETCHER_SECRET` 與 variable `CF_CYPD_FETCHER_URL`；部署後各來源（momo、飛比蝦皮、飛比酷澎）單次實測。

## 下一步

- WO-1.3.0 部署後以 1～3 個追蹤商品觀察一輪排程（各來源單次請求、間隔、分組結果）；之後依使用結果再開下一張（例如 Grok BOT 串接判斷 API）。
- [ ] 部署流程改為與 CYAccountingWeb 一致（使用者 2026-11-16）：首次上線先用手填的 `CF_IDENTITY_SERVICE`／`CF_CYID_WORKSPACE_ID`；之後改用 `apps/CYAccountingWeb/scripts/resolve-cyid-production-runtime.mjs` 從正式 CYID 讀取並核對 service、Workspace ID，部署環境只保留本 App 自己的值。配合全站 CYID 部署命名統一一起處理。
- 整體路線見 `docs/work-orders/README.md`。

## 已知限制

- 尚無通知、圖表、追蹤資料匯出，也未串接蝦皮賣家 API 或 Grok BOT。飛比為商品級第三方參考價，來源價格更新時間未確認。
- sandbox `ScraplingProbe` 於本單合併後由審查者刪除。
- 無角色差異（有使用權者皆可寫入）、無 Backup／Restore、無 Excel。
- 金額固定為新臺幣；優惠券、會員價記在備註，不自動扣抵。
