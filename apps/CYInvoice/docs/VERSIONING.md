# 版本管理規則

## 儲存庫與分支

- 儲存庫：`simonliu1118-byte/CYapps`（預計 Public；source-available proprietary，實際權利依根 `LICENSE`）。
- 穩定主線：`main`。
- CYInvoice Go 正式功能分支：`cyinvoice/feature-簡短名稱`。
- CYInvoice Go 正式修正分支：`cyinvoice/fix-簡短名稱`。
- C# / WinForms 重製測試分支：`cyinvoice/csharp-remake`；此線是獨立實驗線，不是 Go 正式版的直接後續。
- 每次修改以 Pull Request 合併；PR 說明需包含變更、驗證結果及是否影響發票資料/API。

## Go 正式版本號

Go 正式線採 `MAJOR.MINOR.PATCH`，不再細分大量 rc 尾碼：

- `PATCH`：小版修正與不改變主要功能的調整，例如 `1.1.1`、`1.1.2`。
- `MINOR`：一個正式開發階段完成並通過驗證，例如 `1.1.x` 完成後推進 `1.2.0`。
- `MAJOR`：只有非常大的產品、主要流程、資料格式或相容性變更才推進。

Go 正式 CYInvoice 的標籤格式為 `cyinvoice-vX.Y.Z`。

目前正式 Go 基準為 **V1.1.0**。

## C# / WinForms 測試線

- C# 重製使用獨立 `VERSION-CS`，格式為 `1.1.0-cs.N`。
- C# 測試版本不得修改 Go 正式 `VERSION` 來偽裝成正式後續版。
- C# 測試線不得建立 `cyinvoice-vX.Y.Z` 正式 tag，也不得使用 Go 正式 Release workflow。
- 未完成 Go 正式版同等功能、Windows 實機驗收及使用者明確同意前，C# 線只能視為 engineering preview / remake test。
- 若未來決定由 C# 取代 Go，必須另行定案正式版本銜接、資料相容、Release tag 與 migration 規則，不得自動推定。

## 正式版本成立條件

1. 原始碼、依賴與資源完整納入 Git。
2. 可由乾淨環境使用單一建置腳本產生 Windows x64 EXE。
3. EXE 能在 Windows 10/11 x64 實機啟動。
4. icon、manifest、版本顯示與 GUI 子系統正確。
5. 核心功能及本次變更完成測試。
6. 更新 `CHANGELOG.md` 與發行資料夾根目錄的 `VX.Y.Z.txt`。
7. 由 `main` 的正式 Go 線手動啟動 Release workflow。
8. Release workflow 再次核對 `VERSION`、測試、機密掃描、PE、封裝與 SHA-256。
9. 建立 `cyinvoice-vX.Y.Z` 標籤及 GitHub Release，附上 `CYInvoice_VX.Y.Z.zip` 與 SHA-256。

## 正式 Release 啟動規則

- 正式 Release **不得**因 push 到 `cyinvoice-release/**` 或其他 branch 自動發布。
- Release workflow 一律使用 `workflow_dispatch` 人工明確啟動。
- Go 正式 Release 只允許以 `main` 為來源；若在其他 branch 執行必須直接拒絕。
- 手動輸入／確認的版本必須與 `apps/CYInvoice/VERSION` 完全一致。
- Release workflow 發現 `VERSION-CS` 或其他 C# 測試版身分被當作 Go 正式版本時，必須拒絕發布。
- 已存在的正式 tag / Release 不覆寫；需要修正時推進新版本號。

## 發行包

- 解壓縮後資料夾固定為 `CYInvoice`，資料夾名稱不含版號。
- ZIP 檔名為 `CYInvoice_VX.Y.Z.zip`。
- GitHub Actions 的工程測試 Artifact 直接包含 `CYInvoice` 資料夾，不再內包一個 ZIP；下載後只需解壓縮一次。
- 正式 GitHub Release 附上 `CYInvoice_VX.Y.Z.zip` 與 `.sha256`；公開下載不代表授予根 `LICENSE` 以外的權利。
- 發行資料夾根目錄放當次版本紀錄 `VX.Y.Z.txt` 與 `使用說明.txt`；版本紀錄第一行為 `Version: VX.Y.Z`，並含 `Date: YYYY/MM/DD`。
- 正式 Release 的 copyright notice 使用該版本實際發布年份：`Copyright © <YEAR> C.C. Liu, Chihyuan Co. All Rights Reserved.`
- 不再建立 `Version` 資料夾；舊版歷史由 Git commit、tag、CHANGELOG 與 GitHub Release 保存。
- `todo.txt` 不放入發行 ZIP；開發待辦由 GitHub Issue 與 `docs/TODO.md` 管理。
- 會影響當版使用的未解事項，直接列在當版 `VX.Y.Z.txt` 的「已知問題」。
- `Data`、`Cache`、`Logs` 可由發行包建立空目錄，但其執行內容不得提交至 Git。
- API Key、正式公司資料、發票歷史、員工密碼雜湊與 MO店+ 密碼不得出現在 Git、Issue、PR 或 Release。

## 舊版本處理

- V1.0.0 為已知歷史正式基準之一；其歷史 Release 可保留作追溯，但新 Public repository 的正式發行以目前可重建的 `main` 為來源。
- 早期遺失原始碼的 V1.1.0～V1.1.2 執行檔仍記為失敗歷史且不偽造舊 commit/tag。
- 目前 **V1.1.0** 已由本儲存庫現有可重建 Go 原始碼重新建立並完成正式驗證；搬遷至新 `CYapps` 後，應由新 Public repository 的 `main` 再建置一次並建立新的 Public V1.1.0 Release 基準。
