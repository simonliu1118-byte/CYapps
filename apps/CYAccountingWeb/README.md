# CYAccountingWeb

志遠記帳系統 Web 版；與 `apps/CYAccounting/` Windows 版分開維護。

更新：2026/10/15（台灣時間）。本文件是專案入口與現況摘要；永久規則依根 `REPOSITORY_RULES.md`、`REPO_POLICY.md` 與本專案 `PROJECT_RULES.md`。

## 正式基準

| 項目 | 現況 |
| --- | --- |
| 正式網站 | [acc.chihyuancm.com](https://acc.chihyuancm.com) |
| 產品版本 | **V0.24.1**（正式備份頁／日期快捷／損益標籤） |
| 日期修復追蹤 | [PR #441](https://github.com/simonliu1118-byte/CYapps/pull/441) 及對應 main Actions；產品版本以 VERSION／BUILD 為準 |
| 前版部署證據 | 統計返修 PR #409～#411；[Build 2 部署](https://github.com/simonliu1118-byte/CYapps/actions/runs/38160506567)成功。共通 Provider Secret 入口改動後的[部署 #608](https://github.com/simonliu1118-byte/CYapps/actions/runs/38191534386)亦成功，產品版本未改 |
| 公開穩定 Release | **V0.22.15**，tag `cyaccountingweb-v0.22.15`；與持續更新的正式網站分開 |
| D1 schema | **8**；`0008_excel_import_runs.sql` 保存 append-only Excel 替換稽核 |
| CYID consumer | 本 App `CYID_CONSUMER_VERSION` 為 1.0.1；同 repo 已發布 contract 1.0.4、minimum 1.0.0，仍在支援範圍內；不據此推定所有 Provider 環境皆已部署最新版 |
| 治理 | Common Rules 2.8.0；repo 治理版本由根 `GOVERNANCE_VERSION` 讀取 |
| 帳務與備份現況 | 依私人檢查點及正式 catalog 確認；公開文件不保存交易、來源檔、備份識別或資料操作紀錄 |

首次登入引導 PR **#435** 尚未合併；原工作線標示 V0.23.2，合併前需依最新 main 重新基底及升版，不能覆蓋正式版本身分。它仍需先核對正式 CY Web 入口與 CYID authority 相同的 gate。

## 文件入口

| 文件 | 用途 |
| --- | --- |
| [PROJECT_RULES.md](PROJECT_RULES.md) | 專案永久規則及架構邊界 |
| [WORK_HANDOFF.md](WORK_HANDOFF.md) | 正式基準、部署證據、owner、驗收及下一步 |
| [TODO.md](TODO.md) | 未完成工作及仍待真機驗收項目 |
| [CHANGELOG.md](CHANGELOG.md) | 版本歷史；不作目前操作指南 |
| [docs/TABLET_UI_DESIGN.md](docs/TABLET_UI_DESIGN.md) | 平板既有配置、統計入口及真機驗收 |
| [BACKUP_ARCHITECTURE_HANDOFF.md](BACKUP_ARCHITECTURE_HANDOFF.md) | Backup 格式、schema、Phase C、Restore／DR 邊界 |
| [docs/archive/](docs/archive/) | 歷史快照；不重播為現行基準 |

## 已上線能力

- 記帳新增、編輯、刪除；月份切換、摘要搜尋、月統計、逐筆及各帳戶餘額。
- 帳戶新增、改名、預設、排序、封存／解封；SUPER_ADMIN 可永久刪除符合條件的帳戶。固定 `color_slot` 隨帳戶保留，刪除後才釋出；各裝置共用配色及 renderer。
- 收入／支出分類與科目、常用科目／摘要、分類排序及科目拖曳跨分類。
- 期初自動承接；人工調整須理由及 append-only audit。月份鎖帳與快速鎖帳共用邊界語意。
- Excel 完整月份匯出、空白模板下載、匯入預覽與驗證；桌機 SUPER_ADMIN 可指定期間替換，先完成 R2＋GCS paired backup 及回讀，再以 snapshot guard 原子寫入，保留期間外資料及稽核。
- Desktop SQLite 在瀏覽器本機解析，原始 `.db` 不上傳；SUPER_ADMIN 預覽及確認後寫入。
- CYID 共用登入、Session、App Access、Password Recovery；USER 唯讀並可匯出 Excel。Session 逾時／401 沿用共用登入導頁。
- 單月分類／科目合計及收支圓餅圖、多月期間統計、單月左右式損益表。
- D1 匯出一次，R2／GCS 保存及驗證同一 logical backup；Restore／DR 尚未完成。

帳戶名稱最多八個 Unicode 字元；手動摘要最多 40 weighted units（20 個全形／40 個 ASCII），金額 1～9,999,999。SUPER_ADMIN 刪除條件為已封存、無交易、最新有效期初為零；早期非零基準及稽核保留。

## 目前跨裝置呈現

| 裝置 | 記帳／看帳 | 統計 |
| --- | --- | --- |
| 桌機 | 鍵盤快速輸入、inline edit；常用項目為欄位附屬區，月份載入用清單遮罩及 spinner；帳號列保留角色填色、半形 `[]`，Auth 完成後才顯示 | ≥1600px 三欄，較窄視窗置於看帳下方；統計共用整頁垂直捲動 |
| 手機 | 原生日期／月份、共用編輯取消與底部儲存操作、滑出後再點交易操作 | 底部第三個「統計」頁籤；圓餅標名稱／百分比，圖例另列金額 |
| 平板橫式 | 左記帳、右看帳；原生 touch picker、同行帳號角色、共用分享 | 統計圖表入口，沿用共用期間報表 |
| 平板直式 | 看帳主區＋可收合記帳 rail；三列輸入、原生 touch picker | 與橫式相同入口及統計 owner |

手機仍有 session-scoped「測試用平板版」，不是第三套業務流程；下一次 Stable Release 前移除測試入口。實際裝置驗收清單見 TODO，自動測試不代表真機均已通過。

單月／多月皆可切換「圖表／統計」頁籤。桌機圓餅直接標名稱／金額／百分比，空間不足用引線；手機、平板標名稱／百分比並保留完整圖例。單月損益按鈕在多月按鈕左側，損益視窗只處理單月。

統計收入、支出、淨利損及圖表均排除「設定 → 統計／損益」指定的移轉科目；單月區顯示說明。損益表左收入、右支出，底部總收入／總支出、淨利或淨損，最下方另列移轉。期初不計入損益；看帳現金流仍包含移轉。科目太多時依實際可讀高度分頁，完整科目及整月合計保留，不使用報表內部捲軸。

## 架構與設定

前端採原生 HTML／CSS／JavaScript；Worker 入口 `src/app.js`，D1 binding `DB`、Identity private Service Binding `IDENTITY`、Backup `BACKUP_R2`。正式 resource identifiers、credentials、Session 及帳務資料不放 Public Git。

Owner 地圖見 WORK_HANDOFF；統計延伸 `src/statistics.js`／`public/statistics-ui.js`，資料寫入、權限及裝置適配仍共用原 owner。Identity 技術 contract 直接引用 [CYID consumer standard](../CYCloudIdentity/docs/CONSUMER_INTEGRATION_STANDARD.md)，不另建 authority。

一般低／中風險 mutation 採 optimistic＋rollback；一般新增記帳目前仍等待送出，optimistic create 尚未完成。期初、刪除、鎖帳、migration、restore 等高風險動作等待 server 成功。

## 開發與驗證

```bash
npm ci --no-audit --no-fund
npm run dev
```

`.github/workflows/cyaccountingweb-deploy.yml` 為現行驗證／部署流程：PR 自動驗證、人工 dispatch 備援，main push 執行正式部署；包含 syntax、32 項 application／contract／metadata 檢查、D1 local migration、Worker dry-run、CYID contract、secure login 與 semantic assets 驗證。`.github/workflows/cyaccountingweb-release.yml` 只用於明確授權的 Stable Release，重新驗證、掃描公開 source package 及 SHA-256。

V0.23.2 修復桌機日期日／月／年選擇未同步 canonical input 的 HTML data attribute 大小寫問題；選取日期不再清空必填值，儲存使用所選日期。沿用同一 picker 與 transaction writer，手機／平板原生日期不變。GitHub 清理及未合併工作見 WORK_HANDOFF。

V0.23.4：桌機問號與收支同列，確認標題列右側顯示唯一存檔狀態，移除十筆說明；最新十筆及日期排序維持。桌機編輯從清單帶回左側原表單，「清空」切為「取消」，未儲存離開會復原原新增草稿。日期文字＋日曆按鈕共用原 canonical input，可輸入四碼月日／八碼日期及 Ctrl＋↑↓ ±1 天。儲存後直接焦點回摘要，移除金額／摘要焦點競爭；實際 Windows IME 模式仍待使用者確認。桌機單月統計移除括號路徑並新增右下回到頂端圓形按鈕；移轉排除計算及手機／平板原版面維持。

V0.24.0 新增桌機備份頁隔離還原驗證及 Phase D 啟用：R2 每日保留 30 天、GCS 台灣時間週三／週日保留 26 週（182 天），手動及匯入前仍雙副本。啟用須先通過正式 14 次排程與雙副本真實 SQLite 還原驗證，不因部署自動切換、不在切換當下刪舊備份。隔離驗證完整比對帳務欄列，支援來源 schema 8／9；正式帳本 Restore／DR 尚未開放。詳見 BACKUP_ARCHITECTURE_HANDOFF。

本次正式部署、雙副本隔離還原及 Phase D 啟用回讀均已完成（2026/10/15）：R2 每日保留 30 天；GCS 台灣時間週三／週日 03:30 備份、保留 182 天。首次新排程結果仍待觀察，實際證據見 WORK_HANDOFF。

V0.24.1：Phase D 頁面退場 Phase C 驗收／測試用呈現；桌機在日期、摘要或金額按 Ctrl＋↑↓ 都能調整日期。統計、報表、看帳與 Excel 依正負顯示淨利／淨損，零元顯示損益。
