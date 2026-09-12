# CYapps

志遠（CY／Chihyuan）Windows 工具原始碼儲存庫。

本儲存庫預計公開原始碼供檢視；授權條款以根目錄 `LICENSE` 為準。公開可見不代表開放原始碼，也不授予未經許可的使用、修改、散布或商業利用權利。

## 專案

- `apps/CYAccounting`：志遠記帳系統
- `apps/CYEnvelope`：CY 信封列印工具
- `apps/CYInvoice`：CY 電子發票工具

## CI / Build

目前 Build workflow 一律採手動執行（`workflow_dispatch`），避免開發期間因 push 或 pull request 自動消耗 Actions 資源。

## Repository rules

所有開發、分支、發行、命名及敏感資料規則請見 `REPOSITORY_RULES.md`。
