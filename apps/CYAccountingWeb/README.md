# CYAccountingWeb

志遠記帳系統 Web 版；與 `apps/CYAccounting/` Windows 版分開維護。

更新：2026/10/30（台灣時間）。本文件只記錄現行專案入口、已部署能力與操作流程；永久規則只依根 `REPOSITORY_RULES.md`、`REPO_POLICY.md` 和本專案 `PROJECT_RULES.md`。使用者已確認正式 Major 升級至 V1.0.0，Build 歸零；只升版與文件，不重寫帳務邏輯。

## 正式基準

| 項目 | 已確認現況 |
| --- | --- |
| 正式網站 | [acc.chihyuancm.com](https://acc.chihyuancm.com) |
| 產品版本身分 | **V1.0.0（Build 0）**，`VERSION=1.0.0`、`BUILD=0`。此為使用者核准之 Major 發行，功能承接 V0.25.18 Build 2 |
| 已核對的上一代穩定部署基準 | V0.25.18 Build 2，[PR #584](https://github.com/simonliu1118-byte/CYapps/pull/584)，source `5b3442db6cd716cf002c8c1e278d526f978dd07b`；[Production Run 41079799154](https://github.com/simonliu1118-byte/CYapps/actions/runs/41079799154) 成功。**V1.0.0 實際是否已部署，以同 SHA 的新版 Deploy Run 為準** |
| D1 schema | **15**（0012 查詢索引／0013 期初快照／0014 交易螢光顏色／0015 科目或摘要標記位置）；V1.0.0 **沒有新增 migration** |
| CYID consumer | `CYID_CONSUMER_VERSION=1.0.6`；同 repo consumer contract 1.0.6、minimum 1.0.0；不得僅據 source 推定所有 provider runtime 版本 |
| 共通規則 | AITeam `main` 與 CYapps 同步版 **2.11.0**；本 repo 治理依根 `GOVERNANCE_VERSION` |
| 正式 Release 管理 | V1.0.0 發行目標 tag `cyaccountingweb-v1.0.0`，由獨立 [Stable Release workflow](https://github.com/simonliu1118-byte/CYapps/actions/workflows/cyaccountingweb-release.yml) 以**同 SHA 成功 production deploy** 為前提建立；歷史 V0.22.15 tag 保留。請以 [Releases](https://github.com/simonliu1118-byte/CYapps/releases) 查公開發行狀態 |
| 正式帳務／備份執行情況 | 公開文件僅記錄程式契約及已確認的 deploy 結果。實際帳務、最近排程健康／副本 catalog／恢復狀態須在受保護維運環境另行回讀，不從 CI 成功推定 |

正式網站部署與 GitHub Release 分離：日常更新只透過經審核的 `main` source 前進 `deploy/cyaccountingweb-production`，**不必每次建立 Release**。由 0.x.x 進入 1.0.0 的 Major 決策已由使用者批准；版本身分已設定為 Build 0。正式 Release 仍須以同 SHA 部署、完整 CI、安全掃描、SHA-256、`V1.0.0.txt` 驗證後建立；**不能把 Merge 或 PR 綠燈視為已發布 Release**。

首次登入、Session、CYID Application Access 仍以 CYID canonical consumer contract 為準；首次驗證／正式密碼建立依 CYWEB 路徑，CYACCweb 不自行接管 Identity authority。

## 文件入口

| 文件 | 用途 |
| --- | --- |
| [PROJECT_RULES.md](PROJECT_RULES.md) | 專案永久規則及架構邊界 |
| [WORK_HANDOFF.md](WORK_HANDOFF.md) | 正式基準、部署證據、owner、驗收及下一步 |
| [TODO.md](TODO.md) | 未完成工作及仍待真機驗收項目 |
| [CHANGELOG.md](CHANGELOG.md) | 版本歷史；不作目前操作指南 |
| [V1.0.0.txt](V1.0.0.txt) | V1.0.0 正式 Release 說明與相容界線 |
| [docs/TABLET_UI_DESIGN.md](docs/TABLET_UI_DESIGN.md) | 平板現行配置、交易標記、統計及真機驗收 |
| [BACKUP_ARCHITECTURE_HANDOFF.md](BACKUP_ARCHITECTURE_HANDOFF.md) | Backup 格式、schema 15、Phase D、Restore／DR 及正式切換邊界 |
| [docs/recovery-workspace.md](docs/recovery-workspace.md) | 按需建立復原工作區與部署選項；一般網站發布不操作獨立還原庫 |
| [docs/archive/](docs/archive/) | 歷史快照；不重播為現行基準 |

## 已上線能力

- 記帳新增、編輯、刪除；月份切換、摘要搜尋、月統計、逐筆及各帳戶餘額。V0.25.17 改善桌機期初視窗密度、輸入及成功定位到交易。V0.25.18 Build 2 精簡逐筆標記渲染；使用者回報月份切換體感改善，但未量測確切耗時。
- 帳戶新增、改名、預設、排序、封存／解封；SUPER_ADMIN 可永久刪除符合條件的帳戶。固定 `color_slot` 隨帳戶保留，刪除後才釋出；各裝置共用配色及 renderer。
- 收入／支出分類與科目、常用科目／摘要、分類排序及科目拖曳跨分類。
- 期初自動承接；人工調整須理由及 append-only audit。月份鎖帳與快速鎖帳共用邊界語意；**只有交易螢光標記**不受已鎖月份限制，原交易編輯／刪除仍禁止。
- Excel 完整月份匯出、空白模板下載、匯入預覽與驗證；桌機 SUPER_ADMIN 可指定期間替換，先完成 R2＋GCS paired backup 及回讀，再以 snapshot guard 原子寫入，保留期間外資料及稽核。
- Desktop SQLite 在瀏覽器本機解析，原始 `.db` 不上傳；SUPER_ADMIN 預覽及確認後寫入。
- CYID 共用登入、Session、App Access、Password Recovery；USER 唯讀並可匯出 Excel，**無權更改螢光標記**。Session 逾時／401 沿用共用登入導頁。登入頁與帳本固定淺色；深色模式及外觀切換已退場，列入 TODO，未授權自動恢復。
- 交易操作欄提供螢光橘／粉／黃／綠與取消，擇一標記**科目或摘要文字**，只渲染文字網底，不修改科目、摘要或金額。顏色／位置分別保存於 `transactions.marker_color`／`marker_target`；備份與還原保留，舊備份預設標記摘要。
- 單月分類／科目合計及收支圓餅圖、多月期間統計、單月左右式損益表。
- D1 匯出一次，R2／GCS 保存及驗證同一 logical backup；受控還原至獨立 D1，正式 GCS 重建及完整對帳已通過；原正式帳本保留、promotion 另循維運手冊。

帳戶名稱最多八個 Unicode 字元；手動摘要最多 40 weighted units（20 個全形／40 個 ASCII），金額 1～9,999,999。SUPER_ADMIN 刪除條件為已封存、無交易、最新有效期初為零；早期非零基準及稽核保留。

## 目前跨裝置呈現

| 裝置 | 記帳／看帳 | 統計 |
| --- | --- | --- |
| 桌機 | 鍵盤快速輸入、inline edit；常用項目為欄位附屬區。月份載入遮罩位於**不捲動的中間清單可視區**，spinner 固定置中，獨立捲軸／表頭維持。CYWEB 風格單一帳號選單在 Auth 完成後顯示，僅保留登出 | ≥1600px 三欄，較窄視窗置於看帳下方；統計共用整頁垂直捲動 |
| 手機 | 原生日期／月份、共用編輯取消與底部儲存操作、滑出後再點交易操作；同一交易標記 API／renderer | 底部第三個「統計」頁籤；圓餅標名稱／百分比，圖例另列金額 |
| 平板橫式 | 左記帳、右看帳；原生 touch picker、同一帳號選單及交易標記 renderer、共用分享 | 統計圖表入口，沿用共用期間報表 |
| 平板直式 | 看帳主區＋可收合記帳 rail；三列輸入、原生 touch picker | 與橫式相同入口及統計 owner |

手機測試用平板入口、返回手機版、session preview bootstrap／狀態／樣式已移除；正式平板依 viewport 與 touch presentation 判斷。實際裝置驗收清單見 TODO，自動測試不代表真機均已通過。

單月／多月皆可切換「圖表／統計」頁籤。桌機圓餅直接標名稱／金額／百分比，空間不足用引線；手機、平板標名稱／百分比並保留完整圖例。單月損益按鈕在多月按鈕左側，損益視窗只處理單月。

統計收入、支出、淨利損及圖表均排除「設定 → 統計／損益」指定的移轉科目；單月區顯示說明。損益表左收入、右支出，底部總收入／總支出、淨利或淨損，最下方另列移轉。期初不計入損益；看帳現金流仍包含移轉。科目太多時依實際可讀高度分頁，完整科目及整月合計保留，不使用報表內部捲軸。

## 架構與設定

前端採原生 HTML／CSS／JavaScript；Worker 入口 `src/app.js`，D1 binding `DB`、Identity private Service Binding `IDENTITY`、Backup `BACKUP_R2`。正式 resource identifiers、credentials、Session 及帳務資料不放 Public Git。

Owner 地圖見 WORK_HANDOFF；統計延伸 `src/statistics.js`／`public/statistics-ui.js`，資料寫入、權限及裝置適配仍共用原 owner。Identity 技術 contract 直接引用 [CYID consumer standard](../CYCloudIdentity/docs/CONSUMER_INTEGRATION_STANDARD.md)，不另建 authority。

一般低／中風險 mutation 採 optimistic＋rollback；一般新增記帳已採同一 optimistic create lifecycle，成功用實際 row 確認，失敗撤回並保留較新草稿。期初、刪除、鎖帳、migration、restore 等高風險動作等待 server 成功。

## 開發與驗證

```bash
npm ci --no-audit --no-fund
npm run dev
```

`.github/workflows/cyaccountingweb-deploy.yml` 採 **合併與發布分離**：PR 自動驗證，合併 `main` 不會上線；Work 可依下面操作發布。原有 syntax、application／contract／metadata、D1 local migration、Worker dry-run、CYID contract、secure login 與 semantic assets 檢查全部保留。

## 正式部署操作

| 事件 | 結果 |
| --- | --- |
| PR 更新 | 自動驗證；不取得部署 Secrets、不部署 |
| 合併／push 到 `main` | 更新正式 source；不觸發部署 |
| push 到 `deploy/cyaccountingweb-production` | 驗證通過後部署；發布來源須等於當前 `main` |
| 從 `main` 啟動 workflow_dispatch | 相同驗證／部署流程，供 Work 或 GitHub UI 作備援 |
| 從其他分支 dispatch | 不執行 deploy job |

**一般發布步驟（由 Work 執行即可）：**先完成 PR／必要 CI 並合併 `main`；收到當次發布指示後，在 CYapps repository 根目錄把發布分支前進到當前 main：

```bash
git fetch --no-tags origin main
git push origin refs/remotes/origin/main:refs/heads/deploy/cyaccountingweb-production
```

首次 push 會建立發布分支，不必先在 Dashboard 建 Worker、D1、Environment 或 Secret。這條分支只作發布訊號，不在其上開發或新增部署專屬 commit。使用一般 fast-forward push；若被拒絕，先核對歷史，不能直接 force-push。發布分支已指向相同 SHA、沒有新 push 可觸發時，使用下方 dispatch 備援。

```bash
gh workflow run cyaccountingweb-deploy.yml --ref main
```

GitHub UI 等效操作：CYapps → Actions → **CYAccountingWeb Validate and Deploy** → Run workflow → Branch 選 **main**。若目前授權不能 dispatch，可使用上面的發布分支方式；不要為此重建 Secret。

**確認結果：**找對應 SHA 的 run，確認 `validate` 與 `deploy` 兩個 job 均為 success，且 deploy 內 Identity／migration／登入／資產驗證步驟完成。PR 的綠燈、單獨 validate 成功或 deploy=skipped 都不算上線。

```bash
gh run list --workflow cyaccountingweb-deploy.yml --branch deploy/cyaccountingweb-production --event push --limit 5
gh run view <RUN_ID> --json status,conclusion,jobs,url
```

dispatch 的 run 請改查 `--branch main --event workflow_dispatch`。同目標發布共用 `cyaccountingweb-production` 鎖、不取消進行中的 deploy。若排隊期間 main 已前進，來源檢查會在遠端資源準備／migration 前停止；重新核對新版 CI 後，把發布分支前進到新 main 再發布。部署仍使用既有 GitHub `development` Environment 中的正式設定；此名稱是歷史設定 scope，不表示另建測試服務。

`.github/workflows/cyaccountingweb-release.yml` 仍只用於另外明確授權的 Stable Release；會重新驗證、掃描公開 source package 與 SHA-256，並要求同一 source SHA 已有成功的正式 deploy job。更新發布分支只部署網站，不建立 tag 或 GitHub Release。

## 歷史里程碑（不作現行版本／驗收狀態）

更早的歷史細節保留於此供追溯；新功能與返修以 [CHANGELOG.md](CHANGELOG.md) 為版本來源，正式部署以本頁上方的 Run／SHA 為準。

V0.23.2 修復桌機日期日／月／年選擇未同步 canonical input 的 HTML data attribute 大小寫問題；選取日期不再清空必填值，儲存使用所選日期。沿用同一 picker 與 transaction writer，手機／平板原生日期不變。GitHub 清理及未合併工作見 WORK_HANDOFF。

V0.23.4：桌機問號與收支同列，確認標題列右側顯示唯一存檔狀態，移除十筆說明；最新十筆及日期排序維持。桌機編輯從清單帶回左側原表單，「清空」切為「取消」，未儲存離開會復原原新增草稿。日期文字＋日曆按鈕共用原 canonical input，可輸入四碼月日／八碼日期及 Ctrl＋↑↓ ±1 天。儲存後直接焦點回摘要，移除金額／摘要焦點競爭；實際 Windows IME 模式仍待使用者確認。桌機單月統計移除括號路徑並新增右下回到頂端圓形按鈕；移轉排除計算及手機／平板原版面維持。

V0.24.0 新增桌機備份頁隔離還原驗證及 Phase D 啟用：R2 每日保留 30 天、GCS 台灣時間週三／週日保留 26 週（182 天），手動及匯入前仍雙副本。啟用須先通過正式 14 次排程與雙副本真實 SQLite 還原驗證，不因部署自動切換、不在切換當下刪舊備份。隔離驗證完整比對帳務欄列，支援來源 schema 6～10。V0.25.0 延伸為獨立 D1 受控還原，schema 10 新備份包含維運 context，可在原 D1 中斷時從 GCS 重建；原正式帳本保留，promotion 依維運手冊另行進行。詳見 BACKUP_ARCHITECTURE_HANDOFF。

本次正式部署、雙副本隔離還原及 Phase D 啟用回讀均已完成（2026/10/15）：R2 每日保留 30 天；GCS 台灣時間週三／週日 03:30 備份、保留 182 天。首次新排程結果仍待觀察，實際證據見 WORK_HANDOFF。

V0.24.1：Phase D 頁面退場 Phase C 驗收／測試用呈現；桌機在日期、摘要或金額按 Ctrl＋↑↓ 都能調整日期。統計、報表、看帳與 Excel 依正負顯示淨利／淨損，零元顯示損益。

V0.25.5：每日原備份排程後自動回讀 R2／GCS 最近應有副本，驗證 immutable bytes、隔離 SQLite 完整欄列／資料庫檢查。異常持續至檢查恢復，透過 CYID private `ConsumerNotifications` 通知目前超管並於超管進入主頁時警告；同事件去重，寄信失敗保留重試。正常介面移除手動驗證按鈕與結果說明，底部左還原操作／右緊急復原。統計／損益設定改緊密收入／支出雙欄，窄畫面單欄；手機預覽入口與舊本地 Session 表退場。
