# CYAccountingWeb

志遠記帳系統 Web 版；與 `apps/CYAccounting/` Windows 版分開維護。

更新：2026/10/10（台灣時間）。本文件是專案入口與現況摘要；永久規則只依根 `REPOSITORY_RULES.md`、`REPO_POLICY.md` 與本專案 `PROJECT_RULES.md`。

**V0.22.32（待部署）**：桌機超級管理員 Excel 匯入新增指定期間替換，沿用既有 parser、preview 與 commit owner；鎖帳月份仍阻擋，缺少的科目需明確同意保留原名並建立於歷史分類。提交前完成 R2＋GCS 成對備份及回讀驗證，再以帳本／主檔／期初／鎖帳 snapshot guard 執行單一 D1 transaction；包含 append-only 替換稽核，失敗回滾，期間外交易與期初基準保留。修正大型壓縮工作表在 Worker 啟動巢狀執行緒造成解析失敗。25 組本地測試通過；來源 Excel 已私人核對，正式資料替換尚未執行。手機／平板 presentation 未改。Stable Release 維持 V0.22.15。

## 目前狀態

| 項目 | 狀態與證據 |
| --- | --- |
| 正式網站 | [acc.chihyuancm.com](https://acc.chihyuancm.com) |
| 正式版本 | **V0.22.32 待部署**；正式執行仍為 V0.22.31 Build 3，歷史資料替換未執行 |
| 正式部署 | [CYAccountingWeb Validate and Deploy #581（run `37965124293`）](https://github.com/simonliu1118-byte/CYapps/actions/runs/37965124293) 成功；validate、D1 migration、Worker/static assets、secure login 與 semantic frontend assets 全部成功 |
| 公開穩定 Release | **V0.22.15**，tag `cyaccountingweb-v0.22.15`；Stable Release **#2**（run `37254391804`）成功；舊 V0.22.0 Release/tag 已移除 |
| D1 schema | **8 待套用**；新增 `0008_excel_import_runs.sql`，正式目前仍為 7 |
| CYID | consumer 1.0.1；provider contract 1.0.2；minimum compatible 1.0.0 |
| Governance | Common Rules 2.8.0；CYapps Governance 2.3.34 |
| 備份 | Phase C；R2＋GCS 手動 paired 驗收完成，scheduled `x/14` 仍需讀正式 catalog，不按日期推算 |

電腦版仍沿用既有 Desktop UI/UX 設計（PR #115 為歷史設計里程碑），與手機／平板共用原本資料及帳務 owner。V0.22.29 以 `cyacc:confirmation-ready` 明確 lifecycle 恢復桌機左右分欄，修正 `.desktopUi-date-*` 樣式對應；部署驗證通過不代替使用者於真實電腦瀏覽器的視覺驗收。

V0.22.30 Desktop 呈現依使用者 10 項實機要求調整：移除記帳／帳本標題；收支以金額加減號及顏色識別；桌機帳戶選擇下方改日期、科目＋小型常用科目、摘要＋小型常用摘要、金額／儲存／清空；日期日曆圖示改為單一 SVG；月份 loading 明示；期初餘額調整 audit 於寬視窗右側展開；五格月摘要與平板同序放大；修正 inline edit 欄寬；桌機帳號角色以平板同款括號文字、無 pill 背景。手機／平板保留原版面及既有共用 writer；UI 視覺仍待實機再確認。

## 文件入口

| 文件 | 用途 |
| --- | --- |
| [PROJECT_RULES.md](PROJECT_RULES.md) | 專案唯一永久規則補充與 canonical owner map |
| [WORK_HANDOFF.md](WORK_HANDOFF.md) | 最新正式基準、部署、架構 owner、驗證證據與交接 |
| [TODO.md](TODO.md) | 尚未完成的工作與驗收清單 |
| [docs/TABLET_UI_DESIGN.md](docs/TABLET_UI_DESIGN.md) | 平板雙方向設計、已合併實作與剩餘真機驗收 |
| [BACKUP_ARCHITECTURE_HANDOFF.md](BACKUP_ARCHITECTURE_HANDOFF.md) | 備份模組、格式、Phase C 與復原邊界 |
| [CHANGELOG.md](CHANGELOG.md) | 版本里程碑；歷史變更不作目前操作指南 |
| [docs/archive/](docs/archive/) | 歷史快照；接手不需逐份重播 |

## 已上線功能

- 記帳新增、編輯、刪除；月份切換、摘要搜尋、月統計、逐筆及各帳戶餘額。
- 帳戶新增、改名、預設、排序、封存／解封；SUPER_ADMIN 永久刪除符合條件的帳戶。
- 帳戶固定色號：`accounts.color_slot` 永久跟隨帳戶；改名／排序／封存／解封不換色，永久刪除後才釋出，新帳戶優先補最小空 slot。
- 帳戶色盤：1–20 使用高差異淡色＋深字，21–40 使用對應深色＋淺字，41 起每 40 個循環；手機／平板／桌機共用同一 Ledger renderer，色塊不改欄寬。
- 收入／支出大分類與科目、常用科目、分類上下排序及科目拖曳跨分類。
- 自動承接期初餘額；人工例外需理由及 append-only 稽核，記錄 CYID 操作者、時間及實際前／後值。
- 月份鎖帳；Excel 匯出共用期初計算；Excel 匯入有預覽、驗證與重複略過。
- Desktop SQLite 帳本在瀏覽器本機解析，原始 `.db` 不上傳；SUPER_ADMIN 預覽、確認後原子寫入。
- CYID 共用登入、Session、App Access 與 Password Recovery contract；USER 唯讀並可匯出 Excel。
- D1 匯出一次，R2／GCS 分別保存與驗證同一 logical backup；Restore／DR 尚未完成。

帳戶名稱最多八個 Unicode 字元；手動摘要最多 40 weighted units（20 個全形／40 個 ASCII 字元），金額 1～9,999,999。當月有效期初覆寫顯示「調整」，已清除覆寫則不顯示。

SUPER_ADMIN 永久刪除條件為已封存、無交易、最新有效期初為零；早期非零基準及稽核保留，不能重用已稽核帳戶名稱。

## 跨裝置介面

### 手機

- 新增記帳登入後預設為**收入**。
- 日期使用瀏覽器／作業系統原生 `type=date` 顯示與 picker；不再疊加自製 `YYYY/MM/DD` 顯示遮罩。API／DB 資料仍為 `YYYY-MM-DD`。
- 新增狀態保留「儲存／清空」；從看帳進入編輯後改為「儲存修改／取消」，取消會回到原月份與原看帳位置。操作區固定在底部頁籤上方：清空／取消只留小間距貼近頁籤，儲存緊鄰其上；金額與儲存之間使用彈性空間，會隨手機可用高度自然伸縮。
- 右上角帳號按鈕不再先顯示固定「帳號」placeholder；等 CYID Session 準備完成且已有真實 `currentUser` 後才一次顯示員工編號與姓名。
- 常用科目與常用摘要透過明確 bootstrap/lifecycle 準備，不再靠連線文字 MutationObserver 延遲補載。
- 看帳交易維持單列高密度；帳戶色塊與科目保留小幅視覺間距，空月份只顯示置中的「本月尚無記帳資料。」。
- 「更多」中的備份資訊只對 SUPER_ADMIN 顯示，且為唯讀狀態資訊；不提供手機端備份／復原 mutation。
- 保留新增／看帳兩頁、滑出後再點的編輯／刪除、共用彈窗及 `餘額／更多`。
- 收支畫布維持純色收入 `#f4fbf6`、支出 `#fff6f5`。
- 登入頁持續使用 `visualViewport` 同步真正可視高度，touch 裝置不自動 autofocus，鍵盤關閉後不應殘留 Safari 的超長 body 捲軸。V0.22.17 起手機固定使用同一套 compact 尺寸；V0.22.18 起登入 shell 也同步 `visualViewport.offsetTop`，避免 iPad Safari 第一次在橫式叫出鍵盤時把表單再往上推一次。

### 平板

目前正式 main 已包含 V0.22.2～V0.22.3 Build 3 的平板主介面收斂：橫向左記帳／右看帳，直向看帳主區＋底部記帳 rail。手機帳號選單仍暫時提供「測試用平板版」，只在目前分頁 session 內啟用，直接重用正式平板 presentation；直式以 820px、橫式以 1194px reference viewport 呈現，旋轉手機可檢查兩個方向。V0.22.21 起平板直式與橫式都明確由 canonical `.cy-account-cluster` 擁有右上 identity，格式維持 `員工編號 姓名［角色］｜登出`；角色是姓名後方的同行純文字，不是獨立 pill，SUPER_ADMIN 金色、ADMIN 銅色、USER 中性色與手機色票一致，設定按鈕也與帳號列等高。手機 dropdown trigger/menu 在真正 Tablet 一律隱藏；「返回手機版」只有手機 tablet preview 啟用時才出現，真實 iPad 不顯示。直式底部記帳 rail 仍預設展開，唯一把手放大並顯示「展開記帳／收起記帳」，展開內容壓縮為兩列；直式摘要搜尋使用原生 `type=search`，不顯示額外「搜尋／清除」按鈕。V0.22.22 起直式切月份共用手機成熟的 busy lifecycle，會顯示「載入中…」並暫停當月相關控制項；搜尋 Enter 後收鍵盤、存檔成功訊息淡出，期初餘額與月份鎖帳共用 compact touch utility。年月旁的快速鎖帳只移動既有 `lockedThrough` 邊界一個月：下一個月份才能快速上鎖，只有最新鎖帳月份能快速解鎖並退回前一月；歷史已鎖月份、跳月未鎖月份與沒有初始 `lockedThrough` 時都顯示狀態但停用，需使用既有月份鎖帳設定。V0.22.23 再把直式年月選擇器收斂為有邊界與下拉提示的原生 touch capsule；搜尋欄固定 16px 並避免鍵盤開啟時用 visualViewport 壓縮整個 Tablet workspace，修正 iPhone Safari focus zoom／灰色空區。Excel 按鈕移到期初餘額右側，Tablet 直／橫式都沿用手機 native share owner。直式明細隱藏獨立「收支」欄，金額直接以綠色 `+`／紅色 `−` 顯示，操作欄改筆／垃圾桶圖示。V0.22.24 起直式月摘要直接共用橫式的單列同行樣式與分隔線，移除「記帳資料」標題；平板直／橫式的「操作」表頭統一靠左。V0.22.25 將直式新增區改為左側垂直收入／支出與右側三層輸入配置。V0.22.26 修正該版返修：月摘要恢復為年月／工具列下方獨立單列；新增把手依實機示意改成面板上緣中央向上凸出的標籤式拉耳，收合為「↑ 展開新增」、展開為「↓ 收合隱藏」，不再佔用面板內一整列；收合時僅留下薄面板邊線與凸起標籤。V0.22.27 再依實機收斂：交易表在空月份與有資料月份使用固定欄寬；平板直／橫式「操作」表頭置中；月摘要改為固定五格並放大，數值長短不再影響位置。Excel 直／橫式與手機 preview 明確共用同一 native file share 路徑；凸起把手固定白色，收合時不保留整條 rail；左側收入／支出文字改為直向排列。V0.22.28 再把凸起把手移到記帳 rail 上緣 owner，展開時從面板上邊線向上凸出、收合只留把手；直式收入／支出回到 canonical segmented-control 視覺，只做 42px 窄、132px 高的直向排列。右側固定三列為「帳戶／科目／常用科目」、「日期／摘要／常用摘要」、「金額／儲存／清空（編輯時取消）」；平板 Settings 縮至 640px，月份鎖帳恢復穩定單列並收斂逐月控制，備份標題列與重新整理按鈕同步縮整。資料管理與資料移轉只在 Desktop interaction workspace 建立，平板直／橫式不顯示。日期／月份仍與手機共用原生 touch owner，直式與橫式共用 `ledgerMonthDisplay`＋原生 `type=month`；沒有平板第二套 writer、kind、month、lock、export、identity 或 business flow。

未合併的 PR #297 / V0.22.2 Build 4 不是目前基準，後續修正已由 V0.22.3 系列取代。

### 桌機

保留鍵盤快速輸入、inline edit 與桌面配置。

所有裝置共用 Worker、CYID、權限、API、D1、期初計算、transaction mutation 與帳戶色號。一般低／中風險 mutation 採 optimistic + rollback；期初、永久刪除、鎖帳、migration、restore 等高風險操作等待 server 成功。一般**新增記帳**仍沿用共用等待送出流程，optimistic create 尚在 TODO。

## Canonical architecture

目前前端主要 owner：

- `public/app.js`：transaction state、month load、canonical transaction mutation／rollback。
- `public/ledger-tools.js`：唯一 Ledger transaction row renderer、row lifecycle、Ledger Toolbar owner。
- `public/ledger-inline-edit.js`：inline-edit presentation。
- `public/quick-entry.js`：quick-entry／常用項目 presentation。
- `public/adaptive-ui.js`：RWD、native picker、gesture、Settings surface；裝置差異限 presentation。
- `public/input-confirmation.js`：Input Confirmation drawer。

Backend 主要 owner：

- `src/app.js`：Worker route、accounting authorization、API orchestration。
- `src/identity-adapter.js`：CYID transport。
- `src/opening-balances.js`：期初餘額。
- `src/account-lifecycle.js`：帳戶生命週期。
- Backup 與 Desktop Migration 各自維持單一正式 owner。

V0.22.4～V0.22.8 已移除歷史 retry patch、重複 renderer/lifecycle、toolbar relocation 及 V06/V09/V19/V20/V21／Build 式 runtime 殼。真正的 API、D1 schema、backup/file-format、SQLite source schema 與正式 VERSION/BUILD contract 繼續保留。

## 架構與執行設定

前端使用原生 HTML／CSS／JavaScript；Worker 入口為 `src/app.js`。D1 binding 為 `DB`，CYID private Service Binding 為 `IDENTITY`；備份使用 `BACKUP_R2` 與 GCS provider。正式 resource identifiers、帳務資料、憑證及 Session 不放入 Public Git。

Identity 技術規範直接引用 [CYID consumer standard](../CYCloudIdentity/docs/CONSUMER_INTEGRATION_STANDARD.md)。CYACC 採用 1.0.1，仍在 provider 支援的 1.0.0–1.0.2 window 內。登入走獨立 `/login`、navigation-safe server redirect 與 provider Session；沒有裝置專屬登入 authority。舊 `web_sessions` 已不是 runtime fallback，實體表仍待後續 forward migration 退休。

正式部署由 GitHub Actions 配合 `wrangler.template.jsonc` 及 Deployment Environment 暫時產生 config；`workers_dev: true` 備援仍保留。開帳自動計算起始年月仍待使用者補齊資料後提供，目前沒有修改正式帳務基準。

## 開發與驗證

```bash
npm ci --no-audit --no-fund
npm run dev
```

正式 CI 由 `.github/workflows/cyaccountingweb-deploy.yml` 維護，包含 JavaScript syntax、application tests、D1 local migrations、Worker dry-run 與 CYID contract 檢查。Production main push 另執行 D1 migration、Worker/static deployment、secure login 與 semantic asset 驗證。正式公開 Release 由 `.github/workflows/cyaccountingweb-release.yml` 負責，重新驗證 source、執行公開套件安全掃描，並只發布可重建 source package 與 SHA-256。

真實 Password Recovery Email/browser、所有平板裝置組合、Backup Phase C `x/14`、Restore／DR 仍以 `TODO.md` 為準。
