# CYApps Repository Rules

本文件定義 CYApps 系列儲存庫的共通開發與維護規則。除非個別專案文件另有更嚴格規定，所有 CYApps 儲存庫均遵循本文件。

## 1. 正式基準與變更流程

- `main` 僅保存可追溯、可重建、已完成必要檢查的正式狀態。
- 日常修改原則上使用獨立分支；完成檢查後再以 Pull Request 合併至 `main`。
- 不得為處理單一專案而任意改動其他專案；跨專案共通調整必須明確記錄影響範圍。
- 版本、文件、測試與實際原始碼必須保持一致；不得只改版本號而未同步對應內容。
- 拆分、搬移或重建 repository 時，不得把 Private repository 的敏感 Git ancestry、金鑰或機密資料帶入可公開 repository；需要保留開發線時，應以乾淨基準重建該分支的有效內容。

## 2. 原始碼、建置產物與執行資料

- 原始碼、建置設定、測試、必要資源與維護文件應納入 Git。
- EXE、ZIP、LOG、Cache、暫存檔、正式執行資料與本機設定原則上不納入 Git。
- Windows x64 發行檔應透過 GitHub Releases 或 Actions artifact 保存，並在適用時提供 SHA-256。
- 工程測試 Artifact 原則上保留 14 天；正式發行檔應使用 GitHub Release 保存，不以長期 Artifact 取代正式 Release。
- 若個別專案需要例外追蹤特殊檔案，必須由該專案文件明確說明原因與範圍。

## 3. 機密與憑證

- 密碼、正式 API 金鑰、token、正式資料或其他機密資訊不得進入可公開的儲存庫與其 Git 歷史。
- 測試憑證只有在其來源本身已明確公開、且專案文件清楚標示用途時，才可納入公開原始碼；不得以測試憑證名義混入正式秘密。
- 私有儲存庫若因備份或產品設計需要刻意追蹤敏感檔案，必須由專案文件明確標示，且該儲存庫不得改為 Public，除非已先完成歷史清理與機密移轉。
- 不得僅靠 `.gitignore` 視為已移除曾經提交過的敏感內容；若敏感內容曾進入 Git，必須另外處理歷史紀錄。
- 任何可能公開的程式不得依賴寫死在原始碼中的正式管理密碼、清除密碼或其他固定秘密；應使用安全的本機設定、雜湊或平台安全儲存機制。

## 4. 命名與羅馬拼音

- 中文名稱的羅馬拼音一律使用 Wade–Giles。
- 志遠使用 `CY`、`Chihyuan` 或 `Chih-yuan`；不得使用 `Zhiyuan`。
- 專案、資料夾、檔名與程式識別應優先使用既有正式名稱，避免無必要更名造成相容性或追蹤問題。

## 5. 測試、CI 與 GitHub Actions

### 5.1 共通原則

- CI 是合併正式程式碼前的品質檢查，不應因每一次開發 branch push 而無限制重跑。
- 所有主要 Build / Test workflow 應保留 `workflow_dispatch`，需要時可手動驗證。
- 一般開發 branch 的 `push` 原則上不自動觸發完整 CI；若個別專案確有必要，必須在 workflow 或專案文件說明原因。
- Pull Request 進入 `main` 前應自動執行與該專案相關的必要測試；應使用 `paths` / `paths-ignore` 避免不相關專案被一起執行。
- Draft Pull Request 原則上不自動執行完整 CI；轉為 Ready for review 後才進入自動 PR 驗證流程。
- 同一 PR 有新 commit 時，應使用 `concurrency` 取消仍在進行的舊 run，避免重複消耗資源。
- Build/Test workflow 原則上只需要 `contents: read`；只有確實需要建立 Release、tag 或寫入 repository 的 workflow 才授予 `contents: write`。
- GitHub 官方 Actions 應使用目前仍受支援的穩定 major 版本；升級 major 版前需確認 runner 與輸入行為相容，不追求無意義的頻繁更新。

### 5.2 Public / Private 使用差異

- Public 與 Private repository 遵循相同的品質門檻與 PR 原則，不因可見性不同而降低測試要求。
- Private repository 應更謹慎使用 Actions 分鐘：Draft 開發期以本機／人工檢查及必要的手動 workflow 為主，Ready for review 後才自動跑必要 PR CI。
- Private repository 的自動 PR CI 應優先執行必要測試與可重建性檢查；耗時封裝、完整發行包或其他昂貴工作應留到手動驗證或正式 Release。
- Public repository 可在 Ready PR 上執行完整必要 Build/Test，但仍不得對每次無關 push 或所有專案濫跑矩陣工作。

### 5.3 Release

- 正式 Release 一律由 `workflow_dispatch` 或其他明確的人工作業啟動；不得僅因 push 到某個 release branch 就自動對外發布正式版本。
- Release workflow 必須再次驗證版本號、測試、封裝、SHA-256 與敏感資料規則，不能只依賴先前某次 CI 成功。
- 發行版本必須能由儲存庫中的正式原始碼與建置設定重建。
- 若專案有 Release、RC 或里程碑規則，依該專案自己的版本文件執行。

## 6. Copyright 與授權標示

- CYApps 正式 Release 的標準 copyright notice 為：`Copyright © <YEAR> C.C. Liu, Chihyuan Co. All Rights Reserved.`
- `<YEAR>` 必須使用該正式 Release 實際發布的西元年份，不得因沿用舊版本檔案而保留錯誤年份。
- Repository 根目錄 `LICENSE` 以授權基準起始年份標示；跨年度持續維護時可更新為年份區間，例如 `2026–2027`。
- Public / Private repository 可使用不同授權內容；Public 可公開檢視不等於開放原始碼，Private 則可另有機密與存取限制。
- 個別發行包、README 或使用說明不得寫出與根 `LICENSE` 相衝突的散布或使用限制。

## 7. 文件優先順序

若規則發生衝突，依以下順序處理：

1. 使用者當次明確指示。
2. 個別專案的 `AGENTS.md`、`WORK_HANDOFF.md`、`PROJECT_STATUS.md` 或其他專案專屬規則。
3. 本 `REPOSITORY_RULES.md`。
4. 一般 README 說明。

遇到規則差異、資料不明確或可能造成不可逆影響時，不得自行猜測；應先提出差異、風險與建議方案後再處理。

## 8. Repository-specific policy

儲存庫是否 Public／Private、授權條款及特定機密政策，以各儲存庫根目錄的 `README.md` 與 `LICENSE` 為準。這些項目可因儲存庫用途不同而不同，但開發與維護規則應維持本文件所定義的一致原則。
