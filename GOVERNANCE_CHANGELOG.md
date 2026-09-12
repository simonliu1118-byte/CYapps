# CYApps Governance Changelog

## 2.0.0 — 2026/09/13

- 重新建立 CYApps 永久規則架構，固定為三層：共通 `REPOSITORY_RULES.md`、repo-specific `REPO_POLICY.md`、project-specific `PROJECT_RULES.md`。
- 規定 README、WORK_HANDOFF、PROJECT_STATUS、TODO、CHANGELOG、VERSIONING 等文件不得自行成為更高優先的永久規則來源。
- 規定永久規則只能透過 `governance/*` branch 修改，並同步更新 governance version / changelog。
- 建立 `RULES_INDEX.md` 白名單與 Governance Check 機械式防護方向，防止 AI 或一般功能修改任意增加規則檔。
- 統一版本號、正式 tag、Artifact、Release、SHA-256、CI、協作 AI token、portable Windows 發行與 copyright 基本原則。
- Commit metadata 改用 GitHub private noreply email：`286269326+simonliu1118-byte@users.noreply.github.com`。
- Public 與 Private repository 的差異改由各自 `REPO_POLICY.md` 表達，共通規則維持一致。

本版本依使用者 2026/09/13 的治理重整要求建立。尚待使用者定案的發行細節，會在合併前補入本次 2.0.0，不另行假設。
