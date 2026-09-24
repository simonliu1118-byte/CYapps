# CYAccounting Project Rules

本文件只記錄 `apps/CYAccounting/**` 的專案補充與例外。共通規則依根 `REPOSITORY_RULES.md`，Public repo 規則依根 `REPO_POLICY.md`。

## 1. 正式基準

- 專案：志遠記帳系統／CYAccounting。
- 目前正式版本基準以 `main` 的本目錄 `VERSION` 為準；測試用 branch 與 Artifact 不等同正式 Release。
- 使用者資料以 SQLite 為核心；實際帳務資料、備份、Google 授權資訊與本機設定不得提交至 Public Git。
- 正式 Windows 發行以 x64 portable package 為原則；不得要求一般使用者另外安裝 Python 或手動配置 runtime 才能使用。

## 2. 清除資料與密碼

- 依使用者決定，單機版移除「清除所有記帳資料與期初餘額」操作及原始碼中對應的固定密碼；不另增管理密碼設定流程。
- 發行版本不得留有可透過 UI 或內部呼叫一次清除全部交易及期初餘額的產品入口。調整功能時不得自動變更既有帳本或備份。
- 可攜程式預設帳本位於 `Data`，僅刪除 EXE 不會刪除帳本；自訂資料庫位置和 Google Drive 備份需分別處理。使用者說明不得讓人誤以為移除程式必然刪掉所有資料。

## 3. 既有產品行為不得誤改

- 金額欄位上限維持 7 位數，除非使用者另行變更。
- 從設定頁繞過一般鎖帳流程屬既有刻意設計，不得因一般安全重構擅自移除。
- SQLite schema、交易資料、期初餘額、鎖帳與備份修改必須兼顧既有資料相容性；不得為了簡化程式直接要求使用者重建資料庫。
- Google Drive OAuth 憑證由使用者自行匯入；refresh token 等授權資料只保存在本機安全儲存，不得加入 repo、Issue、PR 或 Release。

## 4. 發行與可攜版

- `cyaccounting/v1.0.26-portable-release` 為已遷移的可攜版／Release 工作線，由 CYAccounting 負責 AI 評估後決定合併、調整或淘汰，不由治理工作猜測。
- 正式 package 必須包含程式實際所需 runtime，但不得包含使用者 Data、設定、Google OAuth client JSON、token、log 或既有備份。
- 正式 Release 前除共通檢查外，必須驗證全新 Windows x64 環境可啟動、既有資料庫可讀、備份／還原與鎖帳核心流程沒有回歸。
