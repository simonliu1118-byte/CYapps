# CYERPAutoQuery 工作單

工作單是 `REPOSITORY_RULES.md` §11.4 的交接文件，**只有在使用者指示 Claude 開立工作單轉交 GPT 製作時才使用**（使用者 2026-10-21）：Claude 與使用者討論定案後撰寫 → 使用者轉交 GPT 執行並填「完成報告」→ Claude 審查。一個版本一張，檔名 `WO-<版本>.md`（例如 `WO-0.10.3.md`；同一版本的後續 Build 沿用同一張，在完成報告與審查紀錄分 Build 記錄）。工作單描述需求與狀態，不是規則來源；規則仍以 `REPOSITORY_RULES.md`、`REPO_POLICY.md`、本專案 `PROJECT_RULES.md` 為準；本目錄只放 CYERPAutoQuery 的工作單格式與固定驗收項目。

## 格式

```markdown
# WO-<版本> <標題>

- 狀態：待執行 ／ 執行中 ／ 待審查 ／ 需修正 ／ 已完成
- 分支：cyerp-auto-query/<名稱>　PR：#<編號>（執行者填）
- 依據：PROJECT_RULES 條目、相關 CY Web 工作單或 contract 文件

## 0. 執行限制（必讀）
- 不合併、不發 Release、不推 CY Web；只在指定分支提交並開 PR（CI 會產生測試包）。
- 不新增或修改 `.github/workflows/*`，除非本單明列。
- 測試與文件只用假資料（PROJECT_RULES §5）；不得提交 ERP 匯出檔、LOG、截圖中的營運資料。
- 不改變已定案事項；確需偏離時在完成報告說明，交由審查決定。

## 1. 背景（使用者原話）
引用使用者的需求原文與附圖說明，說明要解決什麼問題。

## 2. 已定案事項
討論中已決定的做法與理由（含使用者確認的 ERP 操作步驟、畫面、欄位）。執行者不得更改。

## 3. 範圍
- 要做的項目（逐條，對到檔案或類別）。

## 4. 不做
- 明確排除的項目。

## 5. 實作指引
- 要沿用的流程與類別（例如 `RunReportAsync`、`ListByConditionAsync`、`ClickUntilFocusedAsync`、`GuardErpMessagesAsync`）、要改的檔案、ERP 操作順序、讀回驗證與 LOG 要求。
- 與 CY Web 協定有關時：欄位名稱、型別、上限，以及 CY Web 端對應的工作單或 PR。

## 6. 驗收清單
- [ ] 每一項都可具體檢查（ERP 操作步驟、讀回驗證、回報內容、錯誤訊息、LOG）。
- [ ] `dotnet test tests/CYERPAutoQuery.Tests` 通過，新邏輯有對應測試（純邏輯放可測的類別）。
- [ ] `dotnet build CYERPAutoQuery.csproj` 無錯誤；PR 的 CYERPAutoQuery Build 成功並產生測試包。
- [ ] `VERSION`／`BUILD`、`CHANGELOG.md`、`README.md`、`WORK_HANDOFF.md` 已更新；動到 `PROJECT_RULES.md` 時同步 `GOVERNANCE_VERSION` 與 `GOVERNANCE_CHANGELOG.md`，並在 repo 根目錄跑治理檢查。
- [ ] commit 身分使用 repo 擁有者（`simonliu1118-byte`）。

## 7. 實機驗證要求
- 列出要使用者在公司電腦測試的步驟，以及要附的 LOG 段落（`logs/` 中的類別名稱）。

## 8. 完成報告（執行者填）
- 驗收清單逐項結果。
- 與工作單不同之處與原因。
- 驗證指令輸出摘要、測試包連結。

## 9. 審查紀錄（審查者填）
- 日期、結論（通過／需修正）、意見（依嚴重度排序）、審查者直接修正的小項目。
```
