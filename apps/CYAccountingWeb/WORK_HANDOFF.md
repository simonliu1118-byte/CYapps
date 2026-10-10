# CYAccountingWeb 工作交接

更新：2026/10/10（台灣時間）。本文件描述工作現況；永久規則仍依正式三層規則。

## 目前工作

- 正式程式已部署 V0.22.32 Build 0（PR #382；Production run `38062352888` 成功，含 D1 migration、Worker/static assets 與登入／semantic checks）；穩定 Release 仍為 V0.22.15。
- V0.22.32 指定期間替換與大型 Excel 解析修正已合併／部署，25 組本地測試與遠端 validate/deploy 成功；程式部署不等於正式交易替換完成。
- 使用者授權以私人 Excel 歷史帳簿替換指定月份，保留後續月份。來源、交易內容、核對結果、備份識別及執行紀錄只保留於私人工作檢查點，不放 Public Git。
- 正式歷史交易是否已完成替換，必須先以授權工作階段核對 `excel_import_runs`、交易期間與完整筆數；本次獨立瀏覽器遇 CYID 未登入（`/api/auth/me` 401），尚未取得新的正式資料完成證據，不得盲目重複提交。最初各帳戶期初餘額仍待使用者提供，不建立猜測基準。
- 暫時診斷與清理 workflow 已移除；正式部署維持最小權限，沒有長期資料查詢或日誌清理入口。

## V0.22.32 範圍與驗證

- 沿用 `src/excel-import.js` 的 inspect／preview／commit。桌機 SUPER_ADMIN 可以指定起訖月份及理由；一般追加模式及其他裝置呈現保持既有行為。
- 鎖帳月份不得匯入或替換。需由既有鎖帳設定解除，再重新預覽；匯入不修改鎖帳、人工期初或其 audit。
- 來源內相同交易的多次發生逐筆保留；缺少的來源科目需明確勾選建立於「歷史科目」分類，既有科目不改名、不重分組。
- 指定期間替換必須先完成 R2 與 GCS paired backup，兩份回讀驗證成功才進入寫入。
- 以單一 snapshot 讀取完整交易、帳戶、分類、科目、期初與鎖帳。預覽 token 綁定來源與目標；D1 batch 內再以 snapshot guard 保護稽核、主檔建立、刪除與寫入，避免預覽／備份期間的競爭修改。
- schema 8 的 `excel_import_runs` 是 append-only 稽核，記錄期間、理由、來源摘要、備份關聯、筆數、CYID actor 與時間；Backup package 同時保留此 audit。既有 Backup format 2 可帶 additive audit 欄位，不改寫舊備份。
- 大型壓縮 XML 曾觸發 parser 的巢狀執行緒；inspect owner 改為有展開容量／entry 上限的同步解壓，再交同一 XLSX parser 解析，沒有第二套資料解析／寫入 authority。
- 本地 25 組測試及大型 Excel canonical roundtrip 通過；新增回歸涵蓋角色、期間界線、鎖帳、備份失敗、D1 原子回滾、競爭修改、重複交易保留、期間外資料原樣保留與稽核備份。
- 指定期間替換等待備份的 API deadline 延長；其他 API deadline 保留既有預設。

## 現行 canonical owner

### Backend / domain

| Concern | Canonical owner |
| --- | --- |
| CYID transport、Session/login/logout/recovery adapter | `src/identity-adapter.js` |
| Worker route、accounting authorization、API orchestration | `src/app.js` |
| 期初餘額 carry-forward / override snapshot | `src/opening-balances.js` |
| 帳戶封存／解封／永久刪除規則 | `src/account-lifecycle.js` |
| Backup package / topology | `src/backup-package.js` → `src/backup-service.js` → provider adapters |
| Desktop SQLite migration | `src/desktop-migration-core.js` + `src/desktop-migration.js` |

### Frontend

| Concern | Canonical owner |
| --- | --- |
| Transaction state、month load、canonical transaction mutation、optimistic rollback | `public/app.js` |
| Ledger transaction rows、row lifecycle、Ledger Toolbar | `public/ledger-tools.js` |
| Inline edit presentation | `public/ledger-inline-edit.js`，寫入仍委派 canonical mutation |
| Quick-entry / frequent presentation | `public/quick-entry.js` |
| Settings lifecycle / renderer / action surface | `public/adaptive-ui.js` 的 `window.cySettingsManager` |
| RWD / native picker / gesture / device presentation | `public/adaptive-ui.js` |
| Input confirmation drawer | `public/input-confirmation.js` |

目前沒有核准永久第二套 business/data owner。修 Bug 先找上述 owner；若開始需要第二 renderer、internal MutationObserver、retry bootstrap、DOM relocation、compatibility wrapper、duplicate device business component 或新的 override chain，套用 Architecture Review Trigger，不直接疊下一層。


## 接續執行

1. 正式 CI／部署已完成；先經具權限 CYID Session 唯讀核對現有交易、`excel_import_runs`、鎖帳與備份狀態，確認前一次是否已提交，避免重複執行。
2. 如仍未提交，重新檢查私人來源 Excel、完整預覽、期間界線、科目／期初，確認鎖帳設定允許此操作；不得只依舊預覽提交。
3. 運用 canonical commit 路徑取得新的 R2＋GCS paired backup 並回讀驗證後再原子替換；執行後核對來源月份、保留期間外交易及恢復原本鎖帳界線。
4. 使用者提供最初各帳戶期初餘額後，透過 canonical opening-balance API 及 append-only audit 設定。
5. 真實帳務、來源檔、token、正式 resource identifiers、執行日誌及私人備份不放 Git、PR 或 CI artifact。

## 其他待辦

待辦以 `TODO.md` 為目前清單；真機 RWD／鍵盤／原生 picker／分享驗收、optimistic create、Backup Restore／DR 與 CYID acceptance 仍依正式文件處理。不得把未合併工作線當成已部署基準。
