# CYAccounting TODO

## 目前狀態

CYAccounting **V1.3.0 已正式發布**，本階段工作已完成，專案目前暫時告一段落。

- 正式版本：`V1.3.0`
- `VERSION=1.3.0`
- `BUILD=0`
- 正式 Release：<https://github.com/simonliu1118-byte/CYapps/releases/tag/cyaccounting-v1.3.0>
- 目前沒有已授權、正在進行中的功能或 UI 修改。

## 已完成

- ACC Icon Family 正式導入並沿用。
- CY Desktop Visual Guide Phase 1 視覺整理完成並經使用者實機接受。
- 輸入頁採較大、較好輸入的主要欄位；資料表採高密度 23px 列高 / 24px 表頭。
- 收入／支出科目管理回到 V1.1.0 已驗證的原生 `QTabWidget`。
- 子視窗正常使用 ACC application icon，不再嘗試移除 Windows title-bar icon。
- 雙重 `DELETE` 本機帳本重置、防誤觸與清除前復原備份流程已保留。
- Windows x64 portable、CI、正式 Release workflow、source verification archive 與 SHA-256 發行流程已建立並完成 V1.3.0 正式發布。

## Deferred

目前只保留一項明確 deferred 工作：

- **125% / 150% DPI**：尚未做正式實機視覺驗收；未來若重新開工，再依當時 Windows / Qt 實機結果調整。

## 重新開工原則

若未來要繼續 CYAccounting：

1. 從當時最新 `main` 開始，不接續舊 Draft branch 或舊 Build workaround。
2. 先讀正式治理規則、`PROJECT_RULES.md`、`WORK_HANDOFF.md`、目前版本檔與 tests。
3. 新需求建立新的 branch / PR。
4. 不要把 V1.2.0 測試階段 Build 3～7 的 title-bar no-icon 或 category-manager 自訂 Tab 作法恢復回來。
5. 正式 Release 仍需使用者明確要求。
