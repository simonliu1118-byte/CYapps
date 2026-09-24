# CYapps Public Repository Policy

本文件只定義 `simonliu1118-byte/CYapps` 的 repository-specific 規則。共通規則以根目錄 `REPOSITORY_RULES.md` 為準；個別程式例外以各專案 `PROJECT_RULES.md` 為準。

## 1. Repository 身分

- 本 repository 為 **Public**。
- 原始碼公開可見，但並非開放原始碼；權利與使用限制依根目錄 `LICENSE`。
- 目前正式維護專案為：`CYAccounting`、`CYAccountingWeb`、`CYEnvelope`、`CYInvoice`、`CYERPAutoInput`、`TriINVCalc`、`SMARTCOPIConverter`。
- 任何提交、Issue、PR、Actions log、Artifact metadata、Release note 與 Release asset 都應視為可能被外部看見。

## 2. 公開安全

- 不得提交正式 API Key、OAuth client secret、access/refresh token、private key、固定正式管理密碼、公司內部帳密或其他秘密。
- 不得提交真實客戶資料、發票資料、帳務資料、公司內部 runtime database、實際匯入 Excel/PDF、log、local settings 或其他營運資料。
- 供應商已公開的測試憑證只有在該專案 `PROJECT_RULES.md` 明確記錄來源與用途時才可保留。
- 若發現疑似秘密已進入 Git history，先停止擴散並評估 rotation／history cleanup；不得只刪目前檔案後宣稱完成。

## 3. Public CI / Artifact / Release

- Public GitHub Actions 可正常使用，不為節省 minutes 而犧牲必要自動驗證；仍須避免無關專案、重複 build 與無意義高頻執行。
- Codex、Claude 或其他計量式協作 AI 流程不得綁定每次 push 自動重做完整審查；應和一般測試 CI 解耦。
- 開發測試包依共通規則使用 Actions Artifact 或經使用者同意的 Pre-release。
- 正式 Windows x64 EXE／ZIP 可以公開放在 GitHub Releases 供下載。
- 公開下載不改變根 `LICENSE` 的 source-available proprietary 性質。

## 4. 共通規則同步

- 本 repo 的 `REPOSITORY_RULES.md`、`COMMON_RULES_VERSION`、`COMMON_RULES_CHANGELOG.md` 必須與 AITeam `main` 母本一致。
- AITeam 共通規則變更後，同一輪治理工作應直接以 Git／GitHub API／治理 PR 同步這三個檔，不等待排程 workflow。
- sync workflow 與 Governance Check 只作第二道保險；Actions 不可用時不得因此延後或略過共通規則同步。
- 任何 AI 接手 APP 前，先直接比對本 repo `COMMON_RULES_VERSION` 與 AITeam `main`；若不同或內容有疑義，先同步再開發。
- 同步只可更新三個共通母本副本，不得覆蓋本 repo `REPO_POLICY.md` 或任何 APP 的 `PROJECT_RULES.md`。

## 5. CY 共用視覺準則

AITeam `main` 是 CY 共用桌面視覺與 Icon Family 的唯一 canonical source：

- Desktop Visual Guide：`shared/cy-visual/desktop/CY_DESKTOP_VISUAL_GUIDE.md`
- Icon Family：`shared/cy-visual/icon-family/`

本 repo 中下列 Windows 桌面專案正式採用上述 canonical source：

- `CYAccounting`
- `CYEnvelope`
- `CYInvoice`
- `CYERPAutoInput`
- `TriINVCalc`
- `SMARTCOPIConverter`

`CYAccountingWeb` 是 Web 專案，不自動套用 Windows Desktop Visual Guide；若未來需要共用 Web 視覺規範，必須另由正式治理來源明確定義。

採用規則：

- AI／開發者在上述桌面專案進行新 UI、UI 重構、視覺調整、控制項樣式、Theme、Table/List、Dialog、Shell、Icon 或相關視覺工作前，必須先讀取 AITeam `main` 的 canonical visual source，再依目前專案 `PROJECT_RULES.md` 與實際 framework 實作。
- 不得在 CYapps 另外維護第二套完整 Desktop Visual Guide 或 Icon Family 家族規格；family-wide／guide-wide 變更先回 AITeam canonical source 處理。
- 個別 App 如有必要永久例外，只能寫入該 App 唯一 `PROJECT_RULES.md`；不得新增平行視覺規則檔。
- 既有穩定 UI 不因本規則立即要求全面重製；新增畫面、被修改的視覺區域或使用者明確要求的 UI 整理應優先向 canonical direction 收斂，並遵守 native-first / complexity guardrails，避免為了外觀破壞穩定性。
- Icon 導入只帶回該 App 自己的正式 SVG／PNG／ICO 資產，不 mirror 整套 family 文件；family-level source 永遠以 AITeam `main` 為準。
- AITeam Visual Guide 已確認的 100% / 96 DPI 狀態可引用；125% / 150% 仍屬 Deferred，未實際驗證前不得宣稱已通過。

## 6. Public Git 歷史

- 新 commit 必須使用共通規則指定的 GitHub noreply email。
- 不得從 Private repo 直接 mirror／merge 含敏感 ancestry 的歷史進來；需要遷移工作線時，以 Public 乾淨基準重建有效內容。
- 歷史清理屬例外維護操作，必須由使用者明確同意並在完成後重新掃描 branch／tag／PR refs。

## 7. 公司命名與 Copyright

- 中文名稱需要轉寫羅馬拼音時依共通規則一律採 Wade–Giles（威妥瑪）。
- 志遠固定使用 `Chihyuan`／`Chih-yuan`，縮寫固定為 `CY`；不得使用 `Zhiyuan`。
- 與志遠相關的資料夾、檔名、程式識別或新英文名稱優先使用 `CY`／`Chihyuan`，但既有正式專案名稱不得無理由改名。
- 正式 Release 的公司 copyright notice 使用：`Copyright © <YEAR> C.C. Liu, Chihyuan Co. All Rights Reserved.`；年份依實際發布年份。
