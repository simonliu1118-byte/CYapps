# CYAccountingWeb

志遠記帳系統 Web 版；與 `apps/CYAccounting/` Windows 版分開維護。

更新：2026/10/05（日本時間）。本文件是專案入口與現況摘要；永久規則只依根 `REPOSITORY_RULES.md`、`REPO_POLICY.md` 與本專案 `PROJECT_RULES.md`。

## 目前狀態

| 項目 | 狀態與證據 |
| --- | --- |
| 正式網站 | [acc.chihyuancm.com](https://acc.chihyuancm.com) |
| 正式版本 | **V0.22.18 Build 2**；PR #329 已合併；同一 iPad 橫式登入工作項目依共同版本規則繼續升 Build，不另升 Patch |
| 正式部署 | CYAccountingWeb Validate and Deploy **#483**（run `37324654496`）成功；application tests、schema 7 migration、Worker/static assets、secure login、semantic assets 均通過 |
| 公開穩定 Release | **V0.22.15**，tag `cyaccountingweb-v0.22.15`；Stable Release **#2**（run `37254391804`）成功；舊 V0.22.0 Release/tag 已移除 |
| D1 schema | **7**；最新 migration `0007_account_color_slots.sql` |
| CYID | consumer 1.0.1；provider contract 1.0.2；minimum compatible 1.0.0 |
| Governance | Common Rules 2.8.0；CYapps Governance 2.3.28 |
| 備份 | Phase C；R2＋GCS 手動 paired 驗收完成，scheduled `x/14` 仍需讀正式 catalog，不按日期推算 |

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

目前正式 main 已包含 V0.22.2～V0.22.3 Build 3 的平板主介面收斂：橫向左記帳／右看帳，直向看帳主區＋底部記帳 rail。日期／月份改與手機共用原生 touch owner，平板只保留 layout / gesture presentation；沒有平板第二套 writer、month state 或 business flow。平板登入仍與手機共用同一 keyboard-aware login owner：直式保留較大的固定尺寸；橫式使用 touch-tablet 雙欄 presentation。V0.22.18 修正 iPad Safari 首次鍵盤的 visual viewport offset／雙重捲動問題，旋轉時也會重建 resting viewport baseline；Build 1 依實機畫面微調橫式可視區內置中、卡片總寬與欄間距；Build 2 再將「員工帳號登入」移到左側「志遠記帳系統」下方，讓右側專注於登入欄位與操作；不另造第二套登入流程。

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
