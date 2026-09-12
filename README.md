# CYapps

志遠（CY／Chihyuan）Windows 工具原始碼儲存庫。

本儲存庫預計公開原始碼供檢視；授權條款以根目錄 `LICENSE` 為準。公開可見不代表開放原始碼，也不授予未經許可的使用、修改、散布或商業利用權利。

Copyright © 2026 C.C. Liu, Chihyuan Co. All Rights Reserved.

## 專案

- `apps/CYAccounting`：志遠記帳系統
- `apps/CYEnvelope`：CY 信封列印工具
- `apps/CYInvoice`：CY 電子發票工具

## CI / Build

Public repository 可以正常使用自動 CI；目標是避免濫用，而不是把正常驗證全部改成手動。

- Ready for review 的 Pull Request 依專案路徑自動執行必要 Build/Test。
- Draft PR 可略過昂貴的完整驗證；是否使用 Draft 由負責開發的 AI／維護者依成熟度決定。
- branch push 是否自動 CI 依實際效益決定，避免與 PR CI 重複執行相同昂貴工作。
- 使用 path filter、concurrency 與分層測試避免不相關專案或舊 run 重複耗用資源。
- 純程式 CI 與 Codex／Claude 等計量式協作 AI 流程盡量解耦；AI 深度審查不因每個小 commit 重新啟動。
- 正式 GitHub Release 由人工明確啟動並重新驗證正式發行包。

## Repository rules

所有開發、分支、CI、發行、命名、copyright 及敏感資料規則請見 `REPOSITORY_RULES.md`。
