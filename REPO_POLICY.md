# CYapps Public Repository Policy

本文件只定義 `simonliu1118-byte/CYapps` 的 repository-specific 規則。共通規則以根目錄 `REPOSITORY_RULES.md` 為準；個別程式例外以各專案 `PROJECT_RULES.md` 為準。

## 1. Repository 身分

- 本 repository 為 **Public**。
- 原始碼公開可見，但並非開放原始碼；權利與使用限制依根目錄 `LICENSE`。
- 目前正式維護專案為：`CYAccounting`、`CYEnvelope`、`CYInvoice`、`TriINVCalc`、`SMARTCOPIConverter`。
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

## 5. Public Git 歷史

- 新 commit 必須使用共通規則指定的 GitHub noreply email。
- 不得從 Private repo 直接 mirror／merge 含敏感 ancestry 的歷史進來；需要遷移工作線時，以 Public 乾淨基準重建有效內容。
- 歷史清理屬例外維護操作，必須由使用者明確同意並在完成後重新掃描 branch／tag／PR refs。
