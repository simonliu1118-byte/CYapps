# 志遠記帳系統 / CYAccounting

## 目前正式版本

- 正式版本：**V1.3.0**
- `VERSION=1.3.0`
- `BUILD=0`
- 平台：Windows 10/11 x64
- 發行形式：Portable ZIP，解壓縮後直接執行 `CYAccounting.exe`，不需另外安裝 Python。
- 正式 Release：<https://github.com/simonliu1118-byte/CYapps/releases/tag/cyaccounting-v1.3.0>
- 正式版本紀錄：`V1.3.0.txt`

CYAccounting V1.3.0 已完成使用者實機驗收與正式 Release；目前專案狀態為**暫停／無進行中開發工作**。

## V1.3.0 重點

- 完成 CY Desktop Visual Guide Phase 1 視覺整理，主 UI 使用 Blue Theme；ACC 綠色保留為程式 Icon identity。
- 輸入記帳頁保留較大的主要輸入欄位與字體，優先支援快速輸入。
- 記帳資料表維持高密度閱讀：資料列 23px、表頭 24px。
- Basic Information、Income、Expense、Input Confirmation、Settings、Import 的 section 視覺、間距與字級完成統一。
- 一般 ComboBox 與 Ledger 內嵌 ComboBox 使用同一家族 chevron；Ledger 保留窄版 geometry。
- 收入／支出科目管理採 V1.1.0 已驗證的原生 `QTabWidget` 作法，不再使用專案自訂 Tab 幾何。
- 子視窗正常繼承 ACC application icon；不再做 Windows title-bar icon suppression。
- 既有記帳流程、Enter/Tab/Esc、SQLite schema、歷史交易文字快照、備份／還原、Google Drive、Excel 匯入、鎖帳、7 位數金額限制與雙重 `DELETE` 清除流程均維持原有行為。
- 本版正式驗收基準為 100% / 96 DPI；125% / 150% DPI 尚未納入完成範圍。

## 資料與更新注意事項

- 預設帳本位於可攜程式資料夾的 `Data`。
- 只刪除 `CYAccounting.exe` 不會刪除帳本。
- 更新前先備份原本 `Data`；不要直接把新版整個資料夾覆蓋正在使用的舊版資料夾。
- 若曾自訂資料庫位置，資料仍保存在該位置；Google Drive 既有備份也不會因移除本機程式而自動刪除。
- 正式發行包不得包含使用者帳本、設定、OAuth client JSON、token、log 或既有備份。

## 本機帳本重置

設定頁提供「清除所有記帳資料與期初餘額」。必須先後兩次各自輸入完全相同的大寫 `DELETE` 才會執行；取消或任何一次輸入錯誤都不會清除。執行前必須先建立可驗證的本機復原備份，既有本機及雲端備份保留。`DELETE` 只是防誤觸確認文字，不是管理密碼。

## Google Drive 首次設定

1. 在 Google Cloud 建立「桌面應用程式」OAuth 用戶端並啟用 Google Drive API。
2. 下載 OAuth client JSON。
3. 志遠記帳系統 → 設定 → Google Drive →「匯入 OAuth 憑證」→「連結 Google Drive」。
4. 連結後可啟用自動雲端同步，也可從「匯入」選取 Google Drive 的 `.xlsx` 或 Google 試算表。
5. 舊式 `.xls` 請先另存為 `.xlsx` 後再匯入。

## 開發與治理

開始任何新修改前，依序確認：

1. 根 `REPOSITORY_RULES.md`
2. 根 `REPO_POLICY.md`
3. `apps/CYAccounting/PROJECT_RULES.md`
4. 最新 `main`
5. `WORK_HANDOFF.md`、`TODO.md`、版本檔、tests 與 workflows

主要技術：PySide6、SQLite、Go Windows GUI launcher。正式 Windows 發行使用 `.github/workflows/cyaccounting-release.yml` 從 `main` 重新建置、測試、掃描並發布。

歷史版本細節保留於各 `Vx.y.z.txt`；不要把舊測試 Build 的暫行 UI 作法當成目前正式規格。
