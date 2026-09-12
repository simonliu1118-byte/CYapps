# CYApps Rules Index

本檔案是永久規則檔案的白名單。未列在此處、但意圖扮演永久開發規則／政策／AI 指令的文件視為未授權規則來源。

## 永久規則層級

- `/REPOSITORY_RULES.md` — CYApps 全系列共通規則；兩個 repo 必須一致。
- `/REPO_POLICY.md` — 目前 repository 的專屬政策。
- `/apps/*/PROJECT_RULES.md` — 個別專案補充與例外；每個 active project 最多一份。

## 治理基礎設施

下列檔案管理規則，但本身不得建立第四層產品規則：

- `/GOVERNANCE_VERSION`
- `/GOVERNANCE_CHANGELOG.md`
- `/RULES_INDEX.md`
- `/AGENTS.md` — AI 入口，只能指向正式規則。
- `/apps/*/AGENTS.md` — 若專案確有需要，可作 AI 入口，但只能索引正式規則，不得複製或新增規則。
- `/.github/workflows/governance-check.yml`

## 非規則文件

README、WORK_HANDOFF、PROJECT_STATUS、TODO、CHANGELOG、REQUIREMENTS、VERSIONING、RC_TEST、RECOVERY_PLAN、DATA_FORMAT、版本紀錄與使用說明均可存在，但只具有其文件本身用途，不得凌駕上述三層永久規則。

若文件內容看起來正在新增永久規則，必須先搬入正確的 `PROJECT_RULES.md`／`REPO_POLICY.md`／`REPOSITORY_RULES.md`，並依治理流程更新版本與變更紀錄。
