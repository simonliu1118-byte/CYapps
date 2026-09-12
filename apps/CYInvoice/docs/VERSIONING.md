# CYInvoice 版本與發行參考

> 本文件不是永久規則來源。正式規則請閱讀：
> `/REPOSITORY_RULES.md` → `/REPO_POLICY.md` → `/apps/CYInvoice/PROJECT_RULES.md`。

## 目前版本身分

- Go / Win32 正式產品線：`VERSION = 1.1.0`。
- C# / WinForms remake：獨立 preview／工程測試線，使用 `VERSION-CS`，不得與 Go 正式版混淆。
- Go 正式 tag 採 `cyinvoice-vX.Y.Z`；詳細 Release 條件與 package 規則以 `PROJECT_RULES.md` 為準。

## 歷史備註

- V1.0.0 為已知歷史正式基準之一。
- 早期曾有原始碼／執行檔歷史不完整的 V1.1.x 工作；不得為了補齊視覺上的版本連續性而偽造 commit、tag 或 Release。
- 目前 V1.1.0 已有可重建 Go source；遷移到新的 Public `CYapps` 後，應由 Public `main` 重新建置並建立正式 Public Release 基準。
- C# remake 是否未來取代 Go，需要使用者另外定案；在那之前一律視為獨立 preview。

## 文件用途

此檔只保留版本身分與歷史背景，避免舊工作脈絡遺失。任何新的版號、tag、Release、Artifact 或 branch 永久規則都不得寫在這裡，必須走 governance 流程更新 `PROJECT_RULES.md` 或共通規則。
