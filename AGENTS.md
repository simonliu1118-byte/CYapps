# CYApps AI Entry Point

本檔案不是第四層規則，只是 AI／自動化工具的入口索引。

接手任何工作前，依序閱讀：

1. `/REPOSITORY_RULES.md`
2. `/REPO_POLICY.md`
3. 目標專案 `/apps/<Project>/PROJECT_RULES.md`
4. 之後才閱讀 README、WORK_HANDOFF、PROJECT_STATUS、TODO、CHANGELOG、REQUIREMENTS 等狀態／參考文件。

永久規則只能存在於上述前三層。不得自行新增其他 `*RULES*`、`*POLICY*`、`*INSTRUCTION*` 或同等用途的規則檔。

若需要新增或修改永久規則，停止一般功能修改流程，改用 `governance/*` branch，更新 `GOVERNANCE_VERSION` 與 `GOVERNANCE_CHANGELOG.md`，再依治理流程處理。

若當次使用者明確指示與既有規則不同，以使用者當次指示優先，但一次性例外不得自動升格為永久規則。
