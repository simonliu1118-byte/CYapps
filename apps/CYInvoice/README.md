# CYInvoice

CY 公司內部專用的 Windows x64 電子發票工具，禁止對外散布。

## 目前狀態

- 可正常執行的行為參考基準：`V1.0.0`（2026/09/04）。
- 早期遺失原始碼的 `V1.1.0`～`V1.1.2` 執行檔僅是失敗歷史，不具 Git 標籤；目前 `V1.1.0` 以本儲存庫可重建原始碼重新建立。
- 目前正式參考基準：`V1.0.0`；下一個正式候選版本：`V1.1.0`。
- 後續細節修正依 PATCH 版本持續進行，不回寫或覆蓋既有正式發行內容。

## 已知技術基準

- 語言：Go。
- 平台：Windows x64。
- GUI：Windows 原生介面，曾使用 `user32.dll`／GDI。
- 發行參數紀錄：`GOOS=windows`、`GOARCH=amd64`、`GOAMD64=v1`、`CGO_ENABLED=0`。
- 應用程式識別：`CYInvoice`，保留 INV icon 與 amd64 Windows manifest。

建置、資源嵌入、PE 與發行包結構已由 GitHub Windows CI 重複驗證；禁止以 PE 後處理方式直接修改正式 EXE。

發行 ZIP 解壓後固定為 `CYInvoice` 資料夾；根目錄保留 `CYInvoice.exe`、當版唯一的 `V版本號.txt` 與 `使用說明.txt`，不建立 `Version` 資料夾，也不放 `todo.txt`。

## 目錄規劃

```text
apps/CYInvoice/
├─ cmd/CYInvoice/       # 程式進入點（重建後）
├─ internal/            # 內部功能模組（重建後）
├─ assets/              # icon、manifest 等建置資源（重建後）
├─ scripts/             # 可重複的建置與封裝腳本（重建後）
├─ tests/               # 測試與測試資料（不得含正式資料）
└─ docs/                # 規格、版本與復原紀錄
```

## 文件

- [功能基準](docs/REQUIREMENTS.md)
- [原始碼復原計畫](docs/RECOVERY_PLAN.md)
- [版本管理規則](docs/VERSIONING.md)
- [待辦與實機驗證](docs/TODO.md)
- [本機資料格式與安全規則](docs/DATA_FORMAT.md)
- [歷史版本紀錄](CHANGELOG.md)
