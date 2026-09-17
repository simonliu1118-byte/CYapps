# CYInvoice

CY Windows 10/11 x64 電子發票工具。Public 可見不代表開放原始碼；使用、修改與散布權利以 repository 根目錄 `LICENSE` 為準。

Copyright © 2026 C.C. Liu, Chihyuan Co. All Rights Reserved.

## 版本狀態

- 現行 source／測試版本：**V2.3.0 Build 2**。
- 最新公開正式 Release：**V2.0.1**。
- C#／WinForms 自 V2.0.0 起為唯一正式產品線，source 直接由 `main` 維護。
- 唯一版本來源為 `VERSION`；正式 Release 使用 `BUILD=0`。
- Go／Win32 V1.1.0 只保留為上一個公開回退版本，不再位於 `main` 現行 source。

日常 `VERSION`／`BUILD` 變更經 PR 通過並合併後，只提供測試 Artifact，版本標題不另加「工程測試包」。套件仍以 `Channel: engineering` 中繼資料及 Artifact 名稱與正式包區分。只有使用者於當次工作明確要求 `release` 時，才從 `main` 啟動正式 Release workflow；workflow 會重新執行核心測試、WinForms 啟動 smoke test、公開安全掃描、Windows x64 self-contained single-file publish、PE／Icon／manifest、ZIP 結構與 SHA-256 驗證。

## 技術基準

- 語言／UI：C#、.NET 10、Windows Forms。
- 平台：Windows 10/11 x64。
- Solution：`CYInvoice.sln`。
- 正式執行方式：self-contained 可攜資料夾內的 `CYInvoice.exe`；WebView2 必要組件集中於 `Runtime/WebView2`。
- 發行 ZIP 解壓後根資料夾固定為 `CYInvoice`。

## 目錄

```text
apps/CYInvoice/
├─ src/CYInvoice.Core/          # 發票、安全、匯入與本機資料核心
├─ src/CYInvoice.WinForms/      # Windows Forms 正式 UI
├─ tests/CYInvoice.Core.Tests/  # parity 與回歸測試 runner
├─ assets/                      # icon 等 Windows 建置資源
├─ scripts/                     # 建置、打包與驗證腳本
└─ docs/                        # 規格、測試、歷史與待辦文件
```

## 文件

- [功能基準](docs/REQUIREMENTS.md)
- [CYInvoice 永久規則](PROJECT_RULES.md)
- [待辦與實機驗證](docs/TODO.md)
- [RC／實機測試](docs/RC_TEST.md)
- [本機資料格式與安全規則](docs/DATA_FORMAT.md)
- [V2 遷移紀錄](docs/MIGRATION_HISTORY.md)
- [歷史版本紀錄](CHANGELOG.md)
