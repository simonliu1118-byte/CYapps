# CYAccountingWeb 工作交接

更新：2026/10/15（台灣時間）。描述正式狀態與驗收，永久規則依正式三層來源。

## 正式基準及驗證

| 項目 | 已確認狀態 |
| --- | --- |
| 產品 | **V0.25.0 Build 1**；VERSION `0.25.0`、BUILD `1`，獨立 D1 受控還原；[PR #450](https://github.com/simonliu1118-byte/CYapps/pull/450)、[main run 38671076691](https://github.com/simonliu1118-byte/CYapps/actions/runs/38671076691) 完整成功；Build 1 [PR #451](https://github.com/simonliu1118-byte/CYapps/pull/451)／[main run 38671618879](https://github.com/simonliu1118-byte/CYapps/actions/runs/38671618879) 成功，正式頁面已回讀 |
| 日期修復 | [PR #441](https://github.com/simonliu1118-byte/CYapps/pull/441)；本版 merge／main Actions 與正式瀏覽器回讀為驗收依據 |
| 統計返修 | PR #409、#410、#411 已合併；Build 2 merge `19e5318446ce2295d1993f41c5b187a1ac22ea63` |
| 統計部署 | [run 38160506567](https://github.com/simonliu1118-byte/CYapps/actions/runs/38160506567)，validate／deploy 成功 |
| 後續部署設定 | Provider base-name 改讀既有共通 Secret；[main run 38191534386／#608](https://github.com/simonliu1118-byte/CYapps/actions/runs/38191534386)成功。只改部署 input，產品 VERSION／BUILD、Identity authority 與帳務 runtime 不變 |
| 公開 Release | **V0.22.15**，tag `cyaccountingweb-v0.22.15`；不與正式網站版本混用 |
| D1 | schema **10**，新增 append-only 受控復原預覽／執行／結果 audit；既有帳務 payload 不變 |
| CYID | consumer **1.0.1**；canonical 已發布 contract **1.0.4**、minimum **1.0.0**。實際環境部署另核對，不以原始碼版本推定 |
| 共通規則 | AITeam main 與本 repo Common Rules **2.8.0** 已直接核對；repo 治理以根 VERSION／CHANGELOG 為準 |

本次 V0.25.0 新增獨立 D1 受控復原，Minor 升版、Build 歸零；PR #450 已合併（da0c618a9ff6cc2da91ab3d99033f4b75b69e5c6），main run 38671076691 validate／deploy／migration／正式 semantic probes 全部成功。真實 GCS → 獨立 D1 演練已完成，完整欄列／稽核／政策／資料庫檢查通過。不建立 Stable Release、不切換正式帳本、不更動手機／平板原版面。

## V0.25.0 受控復原

- Backup service 仍是唯一 HTTP／orchestration owner；`backup-recovery.js` 是空白目標 writer，沿用 `backup-restore.js` 的 accounting mapping，`backup-recovery-context.js` 集中維運表 allowlist。復原頁只有 presentation，呼叫相同 API、採相同 CYID SUPER_ADMIN guard。
- schema 10 新備份的 manifest 內含 catalog、copies、restore checks、policy events 與舊 backup runs；保留完整帳務及期初／匯入 audit。排除 Session／OAuth／機密及復原工作區自身的新預覽／執行紀錄，不宣稱是所有 D1 表的物理複製。
- 空白工作區原子 claim／種子快照 guard／完整寫入；失敗 rollback。還原後逐欄逐列及 context 比對、D1 quick_check／FK；來源 SQLite 仍 integrity_check。目標帶 recovery_hold，不自動成為正式帳本。
- 部署以既有 protected job 準備獨立 D1、注入 RECOVERY_DB；原 DB 不清空。GCS 復原入口及預覽不依賴原 D1，可處理原帳本中斷；傳統 schema 8／9 沒有 context 時仍需原 D1。
- 資料庫正式 promotion 需要維運依 BACKUP_ARCHITECTURE_HANDOFF 的切換步驟進行。本次僅授權演練，保留原帳本與所有 audit，不執行流量切換。

### 2026/10/15 正式驗收

- PR #450 main run 38671076691、返修 PR #451 main run 38671618879 的 validate／deploy／migration／semantic probes 全成功，正式主站回讀 V0.25.0 Build 1。
- 真實 schema 10 paired backup 已回讀 R2／GCS 雙成功；以該份 GCS 備份經獨立入口預覽、勾選及文字確認，成功重建已 provision 的獨立 D1。正式結果確認完整欄列、稽核、備份政策及資料庫檢查通過，目標 append-only verified audit 已由主站備份頁回讀。
- 原主站仍可登入／讀月帳，演練前後原月份合計一致；沒有切換正式 binding 或修改財務列。還原後目標不可再預覽／覆寫，Phase C 完成進度及啟用控制仍隱藏。
- 原 D1 關閉／僅 GCS、約束失敗 rollback、實際 workerd／全新 D1 及重建後帳戶／交易讀取屬本地驗收。正式環境沒有故意停用 R2／原 D1，也沒有進行 production traffic promotion；不能將本次演練寫成正式切換已驗證。
- 本地 77 項 syntax／application 檢查與 CYID／build metadata 檢查通過，返修相關回歸及 PR／main CI 亦通過。未建立 Release、未修改其他 App；實際帳務、備份識別／digest、resource metadata 不進 Public Git。

## V0.24.1 正式狀態與記帳快捷

- Phase D 之後隱藏已完成的 Phase C 驗收進度與啟用控制，備份按鈕改為正式用詞；原 server gate／歷史紀錄保留，不把 UI 退場當成刪除稽核。
- 日期 Ctrl＋↑↓ 改由既有 Quick Entry form keydown owner 統一處理。桌機日期文字、摘要、金額共用 canonical date setter、只發一次 change、保留原焦點；移除 Adaptive picker 內重複 Ctrl handler。IME／其他修飾鍵／非 entry 欄位不攔截，手機／平板原生日期快捷及原版面保留。
- 單／多月統計、損益表、看帳及 Excel 按金額正負顯示淨利／淨損，零元損益；不改移轉排除或任何財務計算。
- 三項修正已由 PR #449 合併，main run 38613728651 成功；正式 V0.24.1 已確認 Phase C UI 退場、摘要 Ctrl＋↑／金額 Ctrl＋↓ 改日期且保留焦點、零元「損益」。完整正負零規則有行為測試。未儲存測試資料。

## V0.24.0 備份政策與隔離還原

- 正式舊版備份頁已回讀 Phase C **14／14**；不再依日期推測。新增 Phase D R2 每日 30 天、GCS 台灣時間週三／週日 03:30 保留 182 天；手動及 Excel 匯入前仍雙副本，GCS 未排程日不記失敗。
- `backup-service.js` 沿用唯一 orchestration，`backup_policy_events` 是生效 policy authority 與 append-only 操作者稽核。啟用前重新檢查 14 次、同一備份雙 provider 隔離還原且證據未逾 24 小時；切換不清物件，後續 cleanup 採新 retention。保留原 topology 與 credentials／bindings，不另建 workflow 或擴張 IAM。
- 隔離 SQLite 使用實際 canonical migrations 與 exporter，驗證所有帳務欄列、重複交易、封存帳戶、設定、期初及匯入 audit，檢查 integrity／FK。V0.24.0 當時支援來源 schema 8／9；結果 metadata append-only、來源為 server principal。V0.25.0 新增獨立 D1 writer 及 schema 10，現況見上節；legacy normalization 仍待辦。
- 版本 stamp 改由 VERSION／migrations 產生 `src/build-metadata.json` 並在 CI check，修正舊新備份固定寫 0.22.2；不回寫歷史備份。server WASM 固定 sql.js 1.14.2，真實 workerd 重複請求確認每次新 DB。
- 本地 **32 項檢查**（30 application suites、CYID contract 與 build metadata）通過；CI 另含 syntax、D1 migration、bundle 及正式 semantic probes。正式 R2／GCS 副本已分別完成隔離還原，備份頁顯示完整資料比對與資料庫檢查通過、正式帳本未更動；啟用按鈕由 server gate 開放。使用者於另一瀏覽器完成啟用後，2026/10/15 已重新整理正式備份頁，回讀 **Phase D 已啟用**：R2 每日 30 天；GCS 台灣時間週三／週日 03:30、182 天。啟用結果由 server policy 回讀確認，不以口頭回報或部署狀態推定。首次 R2-only 新排程已於下方記錄；GCS 新政策週期排程仍待觀察。
- 新增操作只在桌機備份頁；手機／平板維持既有版面與只讀備份資訊，狀態文字反映實際保留政策。

## V0.23.4 桌機記帳輸入、編輯及日期

- 收支及問號同列，field width 讓出問號空間；唯一 `#saveMessage` 改在桌機確認標題列右側，原十筆說明只在桌機隱藏。十筆 retention 與排列不變。
- 原存檔先 `amount.focus()`、ledger-rendered 的 timer 再 `summary.focus()`，形成兩個 post-save focus owner；移除 timer、pending focus flag 及 ledger result 猜測，改由 canonical successful-save lifecycle 呼叫 Quick Entry `cyAfterEntrySaved`，直接回摘要並刷新常用摘要。不能以 Linux／Chromium 驗證冒稱 Windows IME 模式通過；待使用者確認。
- 既有 Adaptive entry-edit owner 改 semantic `setupEntryTransactionEditing`，涵蓋桌機並保持原手機／平板導航與提示。桌機帶回左側共用表單、維持原筆 kind，清空變取消；取消／Escape／換筆／切月份／開其他工作視窗／pagehide／裝置切換均不自動寫入並回復原新增草稿。儲存修改一律 `persistTransactionUpdate` PUT／optimistic／rollback；App 的既有 row action 直接呼叫 entry editor API；刪除被取代的 inline editor module、modal edit state／dialog、CSS、listener 及 asset probe。
- Desktop date owner 直接建立可編輯文字＋日曆按鈕，兩者寫回原 `#txDate`；四碼月日採今年、八碼完整日期、YYYY/MM/DD、Ctrl＋↑↓ 跨月／年、Enter 到摘要、無效日期不得留下舊值。共用 Quick Entry parser／setter；移除 owned DOM observer／synthetic keyboard retry bridge。RWD picker teardown abort 舊 input／document listeners，touch native 日期保留。
- 桌機單月統計只移除說明括號設定路徑，touch／report 說明及移轉排除計算不改。新增桌機右下角圓形回到頂端按鈕，向下捲動後出現；只捲動整頁，遵循 reduced-motion。
- 本地及 PR CI 新增 `entry-workflow.mjs`，驗證取消不寫入、原草稿及歷史 options 復原、原筆 PUT／失敗 rollback、成功只 focus 摘要；日期回歸涵蓋手輸四／八碼、閏日、非法日期、Ctrl 跨年與日曆同步。共 28 組 application tests；正式部署及瀏覽器 gate 追蹤本次 PR／main Actions，不以舊版驗收代替新版。

## V0.23.3 提示、確認清單與排序

- 存檔提示仍由 `app.js showMessage` 寫入唯一 `#saveMessage`；既有 Adaptive placement owner 於 confirmation-ready／裝置切換，把同一 node 放到桌機確認標題列中央、手機／平板原位置，不複製第二個訊息或新增 observer。
- Input Confirmation 改消費 `cyacc:entry-message` 明確 lifecycle，移除存檔訊息與自身 list 的兩個 MutationObserver；render 自行更新筆數及 drawer。每次 submit 收集一筆結果，最新在前，第十一筆淘汰最舊；資料仍只存在本次頁面記憶體。
- 桌機改 23px 單行清單、11.5px 文字、成功／失敗標記，長內容可查看完整文字；取代 17 個舊 confirmation 字級／卡片樣式規則，集中為一個 Desktop presentation owner，不增加 override chain。
- 日期表頭為 native button，切換 ascending／descending；日期比較只影響 displayed rows，相同日期及 balance calculation 沿用 chronological owner。分組同時遵守日期方向；桌機帳戶標籤改「帳戶 [分組]」，觸控保留原標籤。
- 裝置呈現切換由既有 Adaptive lifecycle 呼叫 Ledger render owner，避免桌機倒序／表頭控件帶到手機和平板；transaction snapshot、每筆餘額、月合計、搜尋及 API 不改。
- 行為回歸涵蓋最新十筆淘汰、成功／失敗、逃脫文字、唯一訊息節點 placement，及日期順序／同日收支／分組／搜尋下的正確餘額與裝置切換。正式驗收見 PR #442：唯一提示置中、最新十筆淘汰、日期倒序與分組／每筆餘額一致已回讀。Build 1 移除舊 grid gap 及符號 baseline 留白；密度驗收追蹤收尾 PR 與 main Actions。

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
| Backup／Restore／DR | Phase D 生效、首次 R2-only 自動排程與手動雙副本成功；正式 GCS → 獨立 D1 完整還原／對帳完成，原正式帳本未切換。GCS 新政策週期排程、schema 6／7 normalization 與 budget／lifecycle 仍待辦 |
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
| Backup package／topology／provider | `src/backup-package.js` → `src/backup-service.js` → provider adapters；隔離 SQLite domain `src/backup-restore.js`、WASM host `src/backup-restore-runtime.js` |
| 統計 snapshot／report settings | `src/statistics.js` |
| SQLite normalization／migration | `src/desktop-migration-core.js`／`src/desktop-migration.js` |
| Transaction state／month load／mutation／rollback | `public/app.js` |
| Ledger rows／row lifecycle／Toolbar | `public/ledger-tools.js` |
| Entry edit presentation／draft／cancel | `public/adaptive-ui.js setupEntryTransactionEditing`，寫入委派既有 mutation |
| Quick-entry presentation | `public/quick-entry.js` |
| Settings lifecycle／render／action | `public/adaptive-ui.js` 的 `window.cySettingsManager` |
| RWD／native picker／gesture | `public/adaptive-ui.js` |
| Statistics render／report | `public/statistics-ui.js`；裝置 placement 仍由 Adaptive UI owner |
| Input confirmation drawer | `public/input-confirmation.js` |

## 接續資料操作

真實交易、金額、來源帳本、備份識別、token、正式 resource identifiers 及執行紀錄由私人檢查點保存，不放 Public Git／PR／CI Artifact。本交接只記錄程式能力與驗證，不能作為是否重跑匯入、調整期初、恢復鎖帳或重做備份的依據。

CYID 共通參數及舊 Variable 退場已於 2026/10/12 完成驗證；詳細共通 rollout 見 CYID canonical 文件及相關 App 現行交接。本 App production scope 不借給其他 App；中央 development 更新不自動改變本 App production authority。

2026/10/15 正式備份頁已回讀 Phase D 首次 R2-only 自動排程成功，GCS 正確顯示本日未排程；GCS 在 Phase D 後的首次週三／週日排程仍待實際觀察。V0.25.0 真實手動 paired backup 已回讀 R2／GCS 雙成功，新備份含 schema 10 recoveryContext。實際識別、筆數與 digest 不放 Public Git。

V0.25.0 正式 native 復原表單因 no-referrer 產生空 Origin 而被既有 guard 拒絕，未進入預覽／寫入；Build 1 將頁面 referrer-policy 改為 same-origin，保留同來源 guard 與 CSP，補自動測試。Build 1 main run 38671618879 完整成功後，正式 native 表單 preview／雙重確認／commit 已實際通過。
