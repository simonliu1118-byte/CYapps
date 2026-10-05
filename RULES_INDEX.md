# CYApps Rules Index

本檔案是永久規則與治理檔案白名單。未列在此處、但意圖扮演永久開發規則／政策／AI 指令的文件視為未授權規則來源。

## 永久規則層級

- `/REPOSITORY_RULES.md` — 三 repo 共通規則的同步副本；唯一母本位於 `simonliu1118-byte/AITeam` main。
- `/REPO_POLICY.md` — 目前 repository 的專屬政策。
- `/apps/*/PROJECT_RULES.md` — 個別專案補充與例外；每個 active project 最多一份。

## 共通規則同步基礎設施

- `/COMMON_RULES_VERSION`
- `/COMMON_RULES_CHANGELOG.md`
- `/.github/workflows/sync-common-rules.yml`

上述三個共通規則檔的內容以 AITeam main 為準；CYapps / CYapps_pvt 不得自行分叉。

## Repository 治理基礎設施

- `/GOVERNANCE_VERSION`
- `/GOVERNANCE_CHANGELOG.md`
- `/RULES_INDEX.md`
- `/AGENTS.md` — repository 唯一 AI 規則入口，只能指向正式規則。
- `/.github/workflows/governance-check.yml`
- `/.github/scripts/scan-public-package.py` — Public Artifact／Release 發布前的秘密與 production binding 安全閘門；由 Governance Check 強制所有公開發行 workflow 接入。

## 非規則文件

README、WORK_HANDOFF、PROJECT_STATUS、TODO、CHANGELOG、REQUIREMENTS、RC_TEST、RECOVERY_PLAN、DATA_FORMAT、版本紀錄與使用說明均可存在，但只具有其文件本身用途，不得凌駕上述三層永久規則。

不再保留 project-level `AGENTS.md`、獨立 `VERSIONING.md`、`DEVELOPMENT_RULES.md`、`TEAM_RULES.md` 等平行規則入口；若其內容仍有價值，應先整理到正式三層規則或狀態／歷史文件後刪除舊檔。

若文件內容看起來正在新增永久規則，必須先搬入正確的 `PROJECT_RULES.md`／`REPO_POLICY.md`／`REPOSITORY_RULES.md`，並依治理流程處理；共通規則變更必須先修改 AITeam 母本。
