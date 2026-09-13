# CYApps Governance Changelog

## 2.0.1 — 2026/09/13

- 新增正式維護專案 `TriINVCalc`，顯示名稱固定為「三聯式發票開立計算機」。
- 新增 `apps/TriINVCalc/PROJECT_RULES.md`，定義 Windows x64 portable、公開安全、發票計算核心與發行驗證要求。
- `REPO_POLICY.md` 的正式維護專案清單加入 `TriINVCalc`。

## 2.0.0 — 2026/09/13

- 永久規則固定為三層：共通 `REPOSITORY_RULES.md`、repo-specific `REPO_POLICY.md`、project-specific `apps/<Project>/PROJECT_RULES.md`。
- AITeam 成為三個 repository 共通規則唯一母本；本 repo 新增 `COMMON_RULES_VERSION`、`COMMON_RULES_CHANGELOG.md` 與自動同步 workflow。
- Governance Check 會逐字比對 AITeam `main` 的共通母本；只要本 repo 落後，其他 PR 就不能通過治理檢查。
- 每日輕量 sync workflow 發現母本更新時會建立／刷新 `governance/sync-common-rules-*` PR，不會修改 `REPO_POLICY.md` 或各 APP 的 `PROJECT_RULES.md`。
- 根 `AGENTS.md` 成為唯一 AI 規則入口；刪除 CYInvoice project-level `AGENTS.md` 與舊 `docs/VERSIONING.md`，既有 `DEVELOPMENT_RULES.md` 已移除。
- README、WORK_HANDOFF、PROJECT_STATUS、TODO、CHANGELOG、REQUIREMENTS、RC_TEST 等只保存狀態／需求／測試／歷史，不再具有永久規則優先權。
- Governance Check 會阻擋新的 VERSIONING / TEAM_RULES / project-level AGENTS / 其他未授權平行規則入口。
- 導入 `X.Y.Z + Build N` 版本制度：Major 只由使用者決定；Minor 可由 AI 依明顯功能階段判斷；Patch 為日常新工作項目；Build 僅用於同一項目未完成的返修。
- CYAccounting、CYEnvelope、CYInvoice 均新增 `BUILD`；Build workflow 從 `VERSION` 與 `BUILD` 讀取完整版本身分，不再硬編碼版號，工程 Artifact 可追蹤 Build 與 workflow run。
- CYInvoice 正式 Release 在 `BUILD > 0` 時會停止，避免未經使用者確認就讓 Build 身分消失；工程包與程式標題則會顯示 `Build N`。
- CI 採 Ready PR 自動驗證 + manual dispatch、path filter、concurrency cancellation；正式 Release 與一般 Build/Test 分離。
- Commit metadata 固定使用 GitHub private noreply：`286269326+simonliu1118-byte@users.noreply.github.com`。
- Public repo 的正式秘密、API key、token、runtime data 禁止進入 source/history；CYAccounting 固定清除資料密碼列為下一個 Public 正式 Release 前的 P0 修正。
- Copyright notice 統一為 `Copyright © <YEAR> C.C. Liu, Chihyuan Co. All Rights Reserved.`。
