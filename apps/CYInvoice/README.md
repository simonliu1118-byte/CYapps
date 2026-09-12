# CYInvoice

CY Windows x64 電子發票工具。原始碼與正式發行包的使用、修改與散布權利以 repository 根目錄 `LICENSE` 為準；Public 可見不代表開放原始碼。

Copyright © 2026 C.C. Liu, Chihyuan Co. All Rights Reserved.

## 目前狀態

- 目前 Go 正式版本：**V1.1.0**（2026/09/11）。
- `VERSION` 的正式穩定線目前為 `1.1.0`；後續小修正依 PATCH、完整功能階段依 MINOR、重大產品／相容性變更才推進 MAJOR。
- 早期遺失原始碼的舊 V1.1.0～V1.1.2 執行檔屬失敗歷史，不代表本 repository 的正式 Git 版本；目前 V1.1.0 是由可重建原始碼重新建立並驗證的正式基準。
- 正式 Release 必須由 `main` 的穩定 Go 線手動啟動 Release workflow，重新執行測試、封裝、SHA-256 與敏感資料檢查。

## C# / WinForms 測試線

- `cyinvoice/csharp-remake` 是獨立的 **C# / WinForms 重製測試線**，版本使用 `V1.1.0-cs.N` / `VERSION-CS`。
- C# 測試線不是 Go V1.1.0 的後續正式版本，也不取代 Go 正式基準。
- 未完成同等功能、Windows 實機驗收並取得使用者明確同意前，不得使用正式 `cyinvoice-vX.Y.Z` tag 或 CYInvoice 正式 Release workflow 發布。
- Go 正式線與 C# 測試線的 CI、版本身分與 Release 必須保持可辨識，不得混用。

## 已知技術基準

- 語言：Go（目前正式線）。
- 平台：Windows x64。
- GUI：Windows 原生 Win32 介面。
- 發行參數紀錄：`GOOS=windows`、`GOARCH=amd64`、`GOAMD64=v1`、`CGO_ENABLED=0`。
- 應用程式識別：`CYInvoice`，保留 INV icon 與 amd64 Windows manifest。

建置、資源嵌入、PE 與發行包結構已由 GitHub Windows CI 重複驗證；禁止以 PE 後處理方式直接修改正式 EXE。

發行 ZIP 解壓後固定為 `CYInvoice` 資料夾；根目錄保留 `CYInvoice.exe`、當版唯一的 `V版本號.txt` 與 `使用說明.txt`，不建立 `Version` 資料夾，也不放 `todo.txt`。

## 目錄規劃

```text
apps/CYInvoice/
├─ cmd/CYInvoice/       # Go 正式程式進入點
├─ internal/            # Go 正式內部功能模組
├─ assets/              # icon、manifest 等建置資源
├─ scripts/             # 可重複的建置、驗證與封裝腳本
└─ docs/                # 規格、版本與維護文件
```

C# 重製測試線若存在，使用 `src/`、`tests/`、`CYInvoice.CSharp.sln` 與 `VERSION-CS`，不得因此改變 Go 正式 `VERSION`。

## 文件

- [功能基準](docs/REQUIREMENTS.md)
- [開發規範](docs/DEVELOPMENT_RULES.md)
- [版本管理規則](docs/VERSIONING.md)
- [待辦與實機驗證](docs/TODO.md)
- [本機資料格式與安全規則](docs/DATA_FORMAT.md)
- [歷史版本紀錄](CHANGELOG.md)
