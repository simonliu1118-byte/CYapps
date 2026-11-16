# CYPriceDesk TODO

> 本文件是唯一目前進度與待辦入口，不是永久規則來源。

## 目前基準 — V1.1.0（Build 0）

- WO-1.1.0 已實作於分支 `cypricedesk/feature-cloud-worker`：手動查價搬到 Cloudflare Worker＋D1＋CYID，價格紀錄全站一份，所有寫入留 audit。細節見 `docs/work-orders/WO-1.1.0.md` §8 完成報告。
- 本機驗證（假 IDENTITY＋node:sqlite D1 替身）：`npm test`、`npm run build`、public-package scan、`wrangler deploy --dry-run`（placeholder 設定）通過；Playwright 於真實 Chromium 通過。
- **尚未部署、尚未實機驗證**；PR 審查與使用者授權前不合併、不部署。

## 待使用者完成（部署前）

- [ ] 在 CY Web／CYID 註冊 CYPriceDesk Application 並授權使用者 Application Access。
- [ ] 建立 Cloudflare D1、GitHub Environment `cypricedesk-production`（名稱清單見 README）與自訂網域。
- [ ] 授權後從 `main` 手動觸發 `CYPriceDesk Deploy`，再做實機驗證：登入、首次登入導向 CY Web、停用帳號後 401、新增／修改／刪除／匯入、操作紀錄、舊站 CSV 匯出後匯入。
- [ ] 決定舊 Sites 站何時關閉（本專案不處理）。

## 下一步

- WO-1.2.0（待開）：自動查價——ScraplingProbe 的 Python 抓價程式移入 `fetcher/`（Vercel），Worker 端查詢畫面、規格分類、查到的結果一鍵加入價格紀錄。
- WO-1.3.0（待開）：追蹤商品、Cron 定期查價、同規格比較與定價參考。
- 整體路線見 `docs/work-orders/README.md`。

## 已知限制

- 手動查價；尚無自動擷取、排程或通知。
- 無角色差異（有使用權者皆可寫入）、無 Backup／Restore、無 Excel。
- 金額固定為新臺幣；優惠券、會員價記在備註，不自動扣抵。
