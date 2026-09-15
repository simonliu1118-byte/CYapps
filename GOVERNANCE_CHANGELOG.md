# CYApps Governance Changelog

## 2.3.5 — 2026/09/15

- 依使用者最新規範取消 CYInvoice 自動正式 Release 例外；只有使用者於當次工作明確要求 `release` 後，才可從 `main` 啟動正式 Release workflow。
- `VERSION`／`BUILD` 仍依既有規則推進，但一般 PR 驗證與合併只產生 preview／engineering 測試包，不得自動建立 tag 或公開 Release，也不要求每個小版本都正式發布。
- 已依舊規則發布的 CYInvoice V2.0.1 保留為既有正式歷史，不刪除、不覆寫；後續未正式發布的版本持續累積，直到使用者明確要求 Release。

## 2.3.4 — 2026/09/15

- 補齊 CYInvoice Go／Win32 V1.1.0 的歷史公開回退 Release：repository 當時尚未建立 CYInvoice Release，允許由 V2 合併前的固定 `main` commit `4c2335e00173368540fe10a641fccffcb251f999` 一次性重建、驗證並發布 `cyinvoice-v1.1.0`。
- 歷史 Release 建立後不得更新、覆寫或再將 Go source 加回現行 `main`；V2.0.0 仍是唯一正式產品線與 latest Release。

## 2.3.3 — 2026/09/15

- CYInvoice 依使用者新規範明列正式 Release 自動化例外：含 `VERSION` 變更的 PR 經必要 CI 通過並合併 `main` 後，由 Release workflow 自動重新驗證、建置、打包、建立 tag 與公開 Release。
- 使用者不需要手動按 Merge 或 Run workflow；AI 仍須先確認 PR 驗證結果再代為合併，Release 仍只允許從 `main` 建置。
- `workflow_dispatch` 不再是 CYInvoice 正式發布的必要入口；既有正式 tag／Release 仍不得覆寫。

## 2.3.2 — 2026/09/15

- 使用者完成 Windows 實機驗收並明確批准 Major 升級：CYInvoice 自 `V2.0.0` 起改以 C#／WinForms 為唯一正式產品線，直接由 `main` 維護。
- `cyinvoice/csharp-remake` 完成合併後停止使用；後續不得再以永久 C# 分支或 `VERSION-CS` 建立平行版本身分。
- Go／Win32 `V1.1.0` 固定為上一個可回退的公開版本，不再保留於 `main` 的現行 source；既有 tag、Release、commit 與下載檔不得覆寫。
- CYInvoice 正式 Build、測試、打包及 Release 規則改以 .NET／WinForms Windows x64 實作為準，版本唯一來源維持 `apps/CYInvoice/VERSION`。

## 2.3.1 — 2026/09/14

- 新增 `apps/SMARTCOPIConverter/PROJECT_RULES.md`，補齊 SMARTCOPIConverter 的 project-specific 永久規則層。
- 同步建立專案 `VERSION=1.0.0`、`BUILD=0` 基礎 metadata，讓治理檢查在正式 source 匯入前即可辨識為有效專案。
- 本次不修改共通 `REPOSITORY_RULES.md`，也不重複既有 Public repo 或版本共通規則。

## 2.3.0 — 2026/09/13

- 共通規則同步至 2.4.0：一般 Build／Test workflow 統一採 `pull_request` + `workflow_dispatch`；Draft PR 也可正常驗收，不再把 Draft／Ready 當 CI 開關。
- CYAccounting、CYEnvelope、CYInvoice Go 的 Windows Build workflow 移除 Draft 阻擋；TriINVCalc 移除多餘的 `ready_for_review` 觸發，避免單純切換 PR 狀態重跑 CI。
- CYInvoice Go workflow 收斂 path filter，只在 Go source／module／scripts／assets／VERSION／BUILD 或 workflow 本身變更時執行，避免 C# preview 變更同時浪費一次 Go Windows CI。
- `cyinvoice-csharp-build.yml` 正式加入 `main`，以 Draft PR #2 的 C# solution／source／tests／VERSION-CS 變更自動觸發 Windows 驗收；`workflow_dispatch` 保留人工備援。
- 正式 Release workflow 不變，仍維持明確人工啟動；本次只調整開發 Build／Test 驗收方式。

## 2.2.0 — 2026/09/13

- 共通規則同步至 2.3.0：母本改為公司／個人 repository 可共用的中性規則，並把 Wade–Giles（威妥瑪）定為全域羅馬拼音規則。
- 志遠固定英文名與縮寫改由本 repo `REPO_POLICY.md` 保存：`Chihyuan`／`Chih-yuan`、`CY`，不得使用 `Zhiyuan`。
- 公司正式 copyright notice 改由本 repo policy 保存：`Copyright © <YEAR> C.C. Liu, Chihyuan Co. All Rights Reserved.`。
- 共通母本仍由 AITeam 維護；本次沒有修改任何 APP 的功能規則或 source。

## 2.1.1 — 2026/09/13

- 新增正式維護專案 `SMARTCOPIConverter`（SMART 銷貨單格式轉換工具）至 `CYapps` 專案清單。
- 本次只調整 repository-specific 專案登錄；共通規則與其他專案規則不重複、不變更。

## 2.1.0 — 2026/09/13

- 共通規則同步不再依賴每日 GitHub Actions 排程；AITeam 母本變更後，同一輪治理工作直接以 Git／GitHub API／治理 PR 同步本 repo。
- `sync-common-rules.yml` 改為 manual fallback；Actions 不可用時仍必須直接比對／同步，不得把 workflow 當成唯一一致性來源。
- `REPO_POLICY.md` 新增共通規則同步責任；任何 AI 接手 APP 前須先比對本 repo `COMMON_RULES_VERSION` 與 AITeam `main`。
- 保留 Governance 2.0.1 新增的 `TriINVCalc` 正式專案與其 project rules，不因本次治理整合倒退。

## 2.0.1 — 2026/09/13

- 新增正式維護專案 `TriINVCalc`，顯示名稱固定為「三聯式發票開立計算機」。
- 新增 `apps/TriINVCalc/PROJECT_RULES.md`，定義 Windows x64 portable、公開安全、發票計算核心與發行驗證要求。
- `REPO_POLICY.md` 的正式維護專案清單加入 `TriINVCalc`。

## 2.0.0 — 2026/09/13

- 永久規則固定為三層：共通 `REPOSITORY_RULES.md`、repo-specific `REPO_POLICY.md`、project-specific `apps/<Project>/PROJECT_RULES.md`。
- AITeam 成為共通規則唯一母本；本 repo 新增 `COMMON_RULES_VERSION`、`COMMON_RULES_CHANGELOG.md` 與自動同步 workflow。
- Governance Check 會逐字比對 AITeam `main` 的共通母本；只要本 repo 落後，其他 PR 就不能通過治理檢查。
- 根 `AGENTS.md` 成為唯一 AI 規則入口；舊平行規則入口已清理。
- README、WORK_HANDOFF、PROJECT_STATUS、TODO、CHANGELOG、REQUIREMENTS、RC_TEST 等只保存狀態／需求／測試／歷史，不再具有永久規則優先權。
- 導入 `X.Y.Z + Build N` 版本制度：Major 只由使用者決定；Minor 可由 AI 依明顯功能階段判斷；Patch 為日常新工作項目；Build 僅用於同一項目未完成的返修。
- CI 採 Ready PR 自動驗證 + manual dispatch、path filter、concurrency cancellation；正式 Release 與一般 Build/Test 分離。
- Commit metadata 固定使用 GitHub private noreply。
- Public repo 的正式秘密、API key、token、runtime data 禁止進入 source/history。
