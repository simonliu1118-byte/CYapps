# 版本管理規則

## 儲存庫與分支

- 儲存庫：`simonliu1118-byte/CYapps`（private）。
- 穩定主線：`main`。
- CYInvoice 功能分支：`cyinvoice/feature-簡短名稱`。
- CYInvoice 修正分支：`cyinvoice/fix-簡短名稱`。
- 每次修改以 Pull Request 合併；PR 說明需包含變更、驗證結果及是否影響發票資料/API。

## 版本號

採 `MAJOR.MINOR.PATCH`，不再細分大量 rc 尾碼：

- `PATCH`：測試版、小版修正與不改變主要功能的調整，例如 `1.1.1`、`1.1.2`。
- `MINOR`：一個正式開發階段完成並通過驗證，例如 `1.1.x` 完成後推進 `1.2.0`。
- `MAJOR`：只有非常大的產品、主要流程、資料格式或相容性變更才推進。

CYInvoice 的標籤格式為 `cyinvoice-vX.Y.Z`。

## 正式版本成立條件

1. 原始碼、依賴與資源完整納入 Git。
2. 可由乾淨環境使用單一建置腳本產生 Windows x64 EXE。
3. EXE 能在 Windows 10/11 x64 實機啟動。
4. icon、manifest、版本顯示與 GUI 子系統正確。
5. 核心功能及本次變更完成測試。
6. 更新 `CHANGELOG.md` 與發行資料夾根目錄的 `VX.Y.Z.txt`。
7. 建立 `cyinvoice-vX.Y.Z` 標籤。
8. GitHub Release 附上 `CYInvoice_VX.Y.Z.zip` 與 SHA-256。

## 發行包

- 解壓縮後資料夾固定為 `CYInvoice`，資料夾名稱不含版號。
- ZIP 檔名為 `CYInvoice_VX.Y.Z.zip`。
- GitHub Actions 的工程測試 Artifact 直接包含 `CYInvoice` 資料夾，不再內包一個 ZIP；下載後只需解壓縮一次。
- 正式 GitHub Release 仍附上 `CYInvoice_VX.Y.Z.zip`，使用者下載的就是發行 ZIP，不會再由 GitHub 額外包一層 Artifact ZIP。
- 發行資料夾根目錄放當次版本紀錄 `VX.Y.Z.txt` 與 `使用說明.txt`；版本紀錄第一行為 `Version: VX.Y.Z`，並含 `Date: YYYY/MM/DD`。
- 不再建立 `Version` 資料夾；舊版歷史由 Git commit、tag、CHANGELOG 與 GitHub Release 保存。
- `todo.txt` 不放入發行 ZIP；開發待辦由 GitHub Issue 與 `docs/TODO.md` 管理。
- 會影響當版使用的未解事項，直接列在當版 `VX.Y.Z.txt` 的「已知問題」。
- `Data`、`Cache`、`Logs` 可由發行包建立空目錄，但其執行內容不得提交至 Git。
- API Key、正式公司資料、發票歷史、員工密碼雜湊與 MO店+ 密碼不得出現在 Git、Issue、PR 或 Release。

## 舊版本處理

- V1.0.0 僅為行為參考，不因檔名相同而直接建立正式 Git 標籤。
- 早期遺失原始碼的 V1.1.0～V1.1.2 執行檔仍記為失敗歷史且不補建標籤；目前的 V1.1.0 由本儲存庫現有可重建原始碼重新建立，其正式身分以新 commit、CI、實機驗證與 Git 標籤為準。
- 重建完成後使用新的正式版本號，不偽造已遺失的歷史 commit。
