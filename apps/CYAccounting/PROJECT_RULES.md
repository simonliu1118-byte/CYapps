# CYAccounting Project Rules

本文件只記錄 `apps/CYAccounting/**` 的專案補充與例外。共通規則依根 `REPOSITORY_RULES.md`，Public repo 規則依根 `REPO_POLICY.md`。

## 1. 正式基準

- 專案：志遠記帳系統／CYAccounting。
- 目前正式版本基準：`1.0.26`，唯一正式版本來源為本目錄 `VERSION`。
- 使用者資料以 SQLite 為核心；實際帳務資料、備份、Google 授權資訊與本機設定不得提交至 Public Git。
- 正式 Windows 發行以 x64 portable package 為原則；不得要求一般使用者另外安裝 Python 或手動配置 runtime 才能使用。

## 2. P0 安全待辦

- **下一個 Public 正式 Release 前，必須移除原始碼中的固定「清除全部記帳資料／期初餘額」密碼。**
- 改為由使用者自行設定管理密碼；本機只保存 salted password hash 或等效安全表示，不在 source 內存在固定可操作密碼。
- 此項目前只列為 P0；Governance 2.0.0 本身不得順便改動實際記帳功能。由 CYAccounting 負責 AI 在下一輪程式開發優先處理、測試與遷移。

## 3. 既有產品行為不得誤改

- 金額欄位上限維持 7 位數，除非使用者另行變更。
- 從設定頁繞過一般鎖帳流程屬既有刻意設計，不得因一般安全重構擅自移除。
- SQLite schema、交易資料、期初餘額、鎖帳與備份修改必須兼顧既有資料相容性；不得為了簡化程式直接要求使用者重建資料庫。
- Google Drive OAuth 憑證由使用者自行匯入；refresh token 等授權資料只保存在本機安全儲存，不得加入 repo、Issue、PR 或 Release。

## 4. 發行與可攜版

- `cyaccounting/v1.0.26-portable-release` 為已遷移的可攜版／Release 工作線，由 CYAccounting 負責 AI 評估後決定合併、調整或淘汰，不由治理工作猜測。
- 正式 package 必須包含程式實際所需 runtime，但不得包含使用者 Data、設定、Google OAuth client JSON、token、log 或既有備份。
- 正式 Release 前除共通檢查外，必須驗證全新 Windows x64 環境可啟動、既有資料庫可讀、備份／還原與鎖帳核心流程沒有回歸。
