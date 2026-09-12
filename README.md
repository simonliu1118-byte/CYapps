# CYapps

志遠（CY／Chihyuan）Windows 工具原始碼儲存庫。

本儲存庫預計公開原始碼供檢視；授權條款以根目錄 `LICENSE` 為準。公開可見不代表開放原始碼，也不授予未經許可的使用、修改、散布或商業利用權利。

Copyright © 2026 C.C. Liu, Chihyuan Co. All Rights Reserved.

## 專案

- `apps/CYAccounting`：志遠記帳系統
- `apps/CYEnvelope`：CY 信封列印工具
- `apps/CYInvoice`：CY 電子發票工具

## CI / Build

- 主要 Build/Test workflow 保留手動執行（`workflow_dispatch`）。
- 一般開發 branch push 不自動跑完整 CI。
- Ready for review 的 Pull Request 會依專案路徑自動執行必要 CI；Draft PR 原則上不自動跑。
- 同一 PR 的舊 run 會在新 commit 到來時取消，避免重複耗用資源。
- 正式 GitHub Release 必須由人工明確啟動，不因 push 到 release branch 自動發布。

## Repository rules

所有開發、分支、CI、發行、命名、copyright 及敏感資料規則請見 `REPOSITORY_RULES.md`。
