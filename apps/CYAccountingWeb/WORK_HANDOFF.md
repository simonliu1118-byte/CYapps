# CYAccountingWeb 工作交接

更新：2026/10/11（台灣時間）。本文件描述工作現況；永久規則仍依正式三層規則。

## 目前工作

- V0.23.1 Build 1 統計返修：圖表／統計頁籤共用於單月與多月；圓餅圖直接標示，較小區段引線排字，手機平板少顯示金額但保留圖例清單。左右式損益表依實際視窗高度分頁，保留完整科目及整月合計；不使用內部捲軸。帳號重畫與 role observer 已移除，Auth 為唯一來源。
- V0.23.0 新增統計；桌機 ≥1600px 三欄、較窄桌機統計置於看帳下方；手機第三頁籤、平板雙方向同一入口。原帳務 writer、native picker、Settings lifecycle 與 Identity owner 保留。
- 移轉科目以 categories.id 儲存於既有 app_settings，隨既有備份保存；不猜測或自動標記正式科目。分類依現行主檔，未匹配歷史科目以未分類保留。
- 報表不包含期初餘額；看帳收支包含移轉，統計合計／圖表／分類科目及損益均排除指定移轉科目，損益表最下方另列移轉。

- 正式程式版本為 V0.23.1 Build 1；schema 8 已部署，穩定 Release 為 V0.22.15。
- V0.22.32 指定期間替換與大型 Excel 解析修正已合併、通過 CI 並部署。25 組本地測試通過，公開文件只描述程式能力與建置狀態。
- 實際帳務來源、交易內容、核對結果、鎖帳／期初現況、備份識別及執行紀錄由私人工作檢查點維護，不放 Public Git。接續資料操作前先讀取私人檢查點；不可從本文件推定是否已匯入或決定重跑。
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
| Statistics snapshot / report settings | `src/statistics.js`；路由與角色檢查仍由 `src/app.js` |
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
| Statistics charts / income statement | `public/statistics-ui.js`；頁籤與裝置 placement 仍由 Adaptive UI owner |
| Input confirmation drawer | `public/input-confirmation.js` |

目前沒有核准永久第二套 business/data owner。修 Bug 先找上述 owner；若開始需要第二 renderer、internal MutationObserver、retry bootstrap、DOM relocation、compatibility wrapper、duplicate device business component 或新的 override chain，套用 Architecture Review Trigger，不直接疊下一層。


## 接續執行

1. 資料操作的完成狀態與待辦以私人檢查點及使用者當次指示為準，不從公開交接推定或重跑匯入。
2. 後續期初設定仍使用 canonical opening-balance API 及 append-only audit；不建立未獲來源支持的猜測基準。
3. 鎖帳狀態依當次使用者指示處理，不從舊交接自動恢復界線。
4. 真實帳務、來源檔、token、正式 resource identifiers、執行日誌及私人備份不放 Git、PR 或 CI artifact。

## 其他待辦

待辦以 `TODO.md` 為目前清單；真機 RWD／鍵盤／原生 picker／分享驗收、optimistic create、Backup Restore／DR 與 CYID acceptance 仍依正式文件處理。不得把未合併工作線當成已部署基準。
