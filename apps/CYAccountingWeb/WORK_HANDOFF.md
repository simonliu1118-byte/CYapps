# CYAccountingWeb 工作交接

更新：2026/10/14（台灣時間）。描述正式狀態與驗收，永久規則依正式三層來源。

## 正式基準及驗證

| 項目 | 已確認狀態 |
| --- | --- |
| 產品 | **V0.23.3**；VERSION `0.23.3`、BUILD `0`，桌機確認清單及表頭排序 |
| 日期修復 | [PR #441](https://github.com/simonliu1118-byte/CYapps/pull/441)；本版 merge／main Actions 與正式瀏覽器回讀為驗收依據 |
| 統計返修 | PR #409、#410、#411 已合併；Build 2 merge `19e5318446ce2295d1993f41c5b187a1ac22ea63` |
| 統計部署 | [run 38160506567](https://github.com/simonliu1118-byte/CYapps/actions/runs/38160506567)，validate／deploy 成功 |
| 後續部署設定 | Provider base-name 改讀既有共通 Secret；[main run 38191534386／#608](https://github.com/simonliu1118-byte/CYapps/actions/runs/38191534386)成功。只改部署 input，產品 VERSION／BUILD、Identity authority 與帳務 runtime 不變 |
| 公開 Release | **V0.22.15**，tag `cyaccountingweb-v0.22.15`；不與正式網站版本混用 |
| D1 | schema **8**，Excel 指定期間替換 audit 已納入備份 |
| CYID | consumer **1.0.1**；canonical 已發布 contract **1.0.4**、minimum **1.0.0**。實際環境部署另核對，不以原始碼版本推定 |
| 共通規則 | AITeam main 與本 repo Common Rules **2.8.0** 已直接核對；repo 治理以根 VERSION／CHANGELOG 為準 |

本次為獨立桌機 UI 修改，Patch 升至 V0.23.3、Build 歸零。沒有 schema、帳務資料、Session、手機／平板 UI 或 Stable Release 變更。main 其他 App 的合併不表示本 App 升版。

## V0.23.3 提示、確認清單與排序

- 存檔提示仍由 `app.js showMessage` 寫入唯一 `#saveMessage`；既有 Adaptive placement owner 於 confirmation-ready／裝置切換，把同一 node 放到桌機確認標題列中央、手機／平板原位置，不複製第二個訊息或新增 observer。
- Input Confirmation 改消費 `cyacc:entry-message` 明確 lifecycle，移除存檔訊息與自身 list 的兩個 MutationObserver；render 自行更新筆數及 drawer。每次 submit 收集一筆結果，最新在前，第十一筆淘汰最舊；資料仍只存在本次頁面記憶體。
- 桌機改 23px 單行清單、11.5px 文字、成功／失敗標記，長內容可查看完整文字；取代 17 個舊 confirmation 字級／卡片樣式規則，集中為一個 Desktop presentation owner，不增加 override chain。
- 日期表頭為 native button，切換 ascending／descending；日期比較只影響 displayed rows，相同日期及 balance calculation 沿用 chronological owner。分組同時遵守日期方向；桌機帳戶標籤改「帳戶 [分組]」，觸控保留原標籤。
- 裝置呈現切換由既有 Adaptive lifecycle 呼叫 Ledger render owner，避免桌機倒序／表頭控件帶到手機和平板；transaction snapshot、每筆餘額、月合計、搜尋及 API 不改。
- 行為回歸涵蓋最新十筆淘汰、成功／失敗、逃脫文字、唯一訊息節點 placement，及日期順序／同日收支／分組／搜尋下的正確餘額與裝置切換。正式驗收追蹤本次 PR 與 main Actions。

## V0.23.2 日期修復

- 原 picker markup 的 `data-desktopUi-date-*` 經 HTML parser 全部轉小寫，dataset key 與 handler 不符；選日把 native `#txDate` 清空，label fallback 今天，造成必填驗證阻擋儲存，日／月／年導覽也讀不到數值。正式舊版已在瀏覽器重現此資料／畫面落差。
- 在原 Adaptive date presentation owner 統一改為 lowercase kebab-case `data-desktop-ui-date-*`，不新增 renderer、writer、observer 或 fallback。既有 `change` lifecycle、lock 判斷及 submit 讀同一 input。
- 行為回歸涵蓋選日後 canonical value／label、單次 change、儲存 payload、前後月與跨年、指定年月、閏日、今天、外部變更及重複初始化。舊 source 會使該測試失敗；修復後通過。
- Adaptive script cache revision 改為 `desktop-date-dataset`，正式 semantic verification 使用同一 URL 並確認新的日期 attribute。本版部署與正式瀏覽器驗收證據追蹤 [PR #441](https://github.com/simonliu1118-byte/CYapps/pull/441) 及對應 main Actions，不把舊版重現當成新版通過。

## 已交付內容與實際驗收界線

- 統計以完整期間 snapshot 彙總，不受清單筆數限制；保留封存帳戶及歷史未匹配科目。移轉科目按 `categories.id` 保存於既有 settings，隨備份保存，不依名稱猜測。
- 單月與多月共用「圖表／統計」頁籤。桌機統計共用整頁垂直捲動，寬視窗三欄、窄視窗接在看帳下方；手機第三頁籤、平板雙方向同一入口。
- 圓餅直接標名稱／金額／百分比；小區段引線排列。手機／平板不在餅上放金額，完整圖例仍保留；不使用 tooltip。
- 統計合計、圖表、分類／科目與損益皆排除指定移轉，單月區明示；看帳現金流包含移轉。期初不計入損益。
- 損益表左右並排收入／支出，合計與淨利損在底部，移轉最後另列。依可讀高度必要時分頁，所有科目及整月合計保留；沒有報表內部捲軸。
- 移除 Adaptive identity 重畫及 role observer；`public/auth.js` 為唯一帳號 renderer。等待資料時的隱藏規則已置於真正載入的 `adaptive-ui.css`，保留既有角色填色及對齊。
- 26 組本地 application tests、PR CI／governance 及正式 deploy 通過。正式桌機瀏覽器 **1363×936** 已驗證：帳號由 pending hidden 到 ready visible、圖表／統計互斥、排除移轉說明、單月只顯示一個月份，抽查月份完整一頁且無水平／垂直捲軸。
- 上述抽查不等於所有月份／視窗尺寸均一頁，也不等於手機／平板真機驗收。完整剩餘 gate 見 TODO。

Excel V0.22.32 沿用 inspect／preview／commit owner；指定期間替換先完成 R2＋GCS paired backup 及回讀，再以 snapshot guard 執行原子 D1 batch，保留重複發生交易、期間外資料與 append-only audit。功能部署證據 [run 38062352888](https://github.com/simonliu1118-byte/CYapps/actions/runs/38062352888)不代表可以推定實際匯入狀態。

## 未合併工作與下一步

| 工作 | 現況及接續條件 |
| --- | --- |
| PR **#435** 首次登入引導 | **保留、尚未合併**。原分支 V0.23.2 已不是可直接沿用的版本；合併前依最新 main 重新基底及升版。CI 成功不代替正式 CY Web portal 與 CYID authority 相同的 live binding gate；待該工作確認後再合併部署。不要因清理順便合併 |
| `cyaccountingweb/optimistic-transaction-create` | 暫停的未合併實驗；保留供檢視，不是部署基準。續作從 main 重建，不直接延續舊架構 |
| 真機統計／記帳驗收 | 手機、平板雙方向、鍵盤／picker／分享及大科目報表仍有待驗證項目 |
| Backup／Restore／DR | 依正式 catalog 確認 Phase C，未取得最新 `x/14`；不依日期猜進度，Restore／DR 未完成 |
| 實際帳務操作 | 先讀私人檢查點與當次指示，再唯讀核對；公開文件不宣告尚未或已完成某次匯入，不據舊 TODO 重跑 |

## GitHub 清理 — 2026/10/13

- PR **#436** 已合併；[清理 run 38383025033](https://github.com/simonliu1118-byte/CYapps/actions/runs/38383025033) completed／success，作業回讀確認 **34 個過期 branch、16 個已退場流程的 22 筆 completed runs** 已清理。候選 branch／run／到期 Artifact 殘留均為 **0**；沒有需要處理的到期 Artifact，共 56 次刪除。
- 舊文件 PR **#268、#291、#383** 已關閉，現行文件已涵蓋有效內容；PR 歷史保留。已完成分支清單與盤點 SHA 可由 #436 commit 追溯。
- **#435 首次登入引導**與 **optimistic create** 未合併工作線保留；執行中 run、現行部署／Release 全部歷史及 Stable Release／tag／assets、其他 App／共通流程保留。
- 一次性 maintenance workflow、script 及 manifest 已從本次收尾 source 移除；主線僅保留正式 `cyaccountingweb-deploy.yml` 與 `cyaccountingweb-release.yml`。本次清理的驗證／執行紀錄保留作維護證據，不留下長期清理權限入口。
- 文件、Backup schema、平板現況與 TODO 已彙整；產品維持 **V0.23.1 Build 2／schema 8**。部署驗證不代表更動帳務資料，也不合併未完成的 #435。

## Canonical owner

| Concern | 正式 owner |
| --- | --- |
| Identity transport／Session | `src/identity-adapter.js`；前端帳號 render `public/auth.js` |
| Worker route／帳務權限／API orchestration | `src/app.js` |
| 期初 carry-forward／人工調整 | `src/opening-balances.js` |
| 帳戶 lifecycle | `src/account-lifecycle.js` |
| Backup package／topology／provider | `src/backup-package.js` → `src/backup-service.js` → provider adapters |
| 統計 snapshot／report settings | `src/statistics.js` |
| SQLite normalization／migration | `src/desktop-migration-core.js`／`src/desktop-migration.js` |
| Transaction state／month load／mutation／rollback | `public/app.js` |
| Ledger rows／row lifecycle／Toolbar | `public/ledger-tools.js` |
| Inline edit presentation | `public/ledger-inline-edit.js`，寫入委派既有 mutation |
| Quick-entry presentation | `public/quick-entry.js` |
| Settings lifecycle／render／action | `public/adaptive-ui.js` 的 `window.cySettingsManager` |
| RWD／native picker／gesture | `public/adaptive-ui.js` |
| Statistics render／report | `public/statistics-ui.js`；裝置 placement 仍由 Adaptive UI owner |
| Input confirmation drawer | `public/input-confirmation.js` |

## 接續資料操作

真實交易、金額、來源帳本、備份識別、token、正式 resource identifiers 及執行紀錄由私人檢查點保存，不放 Public Git／PR／CI Artifact。本交接只記錄程式能力與驗證，不能作為是否重跑匯入、調整期初、恢復鎖帳或重做備份的依據。

CYID 共通參數及舊 Variable 退場已於 2026/10/12 完成驗證；詳細共通 rollout 見 CYID canonical 文件及相關 App 現行交接。本 App production scope 不借給其他 App；中央 development 更新不自動改變本 App production authority。
