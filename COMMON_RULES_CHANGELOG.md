# CY Common Rules Changelog

## 2.0.0 — 2026/09/13

- 建立 AITeam / CYapps / CYapps_pvt 三個 repository 共用的治理母本。
- 永久規則固定為：共通 `REPOSITORY_RULES.md`、repo-specific `REPO_POLICY.md`、project-specific `PROJECT_RULES.md`。
- 共通規則的唯一母本改由 `simonliu1118-byte/AITeam` 的 `main` 維護；CYapps / CYapps_pvt 保存同步副本，不得自行分叉修改。
- 建立 `COMMON_RULES_VERSION`，共通規則每次變更必須同步升版並記錄本檔。
- 版本制度固定為 X.Y.Z + Build：X 只由使用者決定；Y 可由 AI 依明顯功能階段判斷；Z 為日常新工作項目；Build N 僅用於同一項目未完成的返修。
- 統一 branch / PR、CI、Artifact、Release、SHA-256、portable Windows 發行、noreply commit identity、機密處理與治理檔白名單原則。
