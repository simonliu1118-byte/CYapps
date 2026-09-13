# CY Shared Repository Rules

本文件是 AITeam / CYapps / CYapps_pvt 的**共通永久規則母本**。

- 唯一母本：`simonliu1118-byte/AITeam` 的 `main:/REPOSITORY_RULES.md`。
- 共通規則版本：`main:/COMMON_RULES_VERSION`。
- 共通變更紀錄：`main:/COMMON_RULES_CHANGELOG.md`。
- `CYapps` 與 `CYapps_pvt` 只保存同步副本，不得自行分叉修改共通規則。
- Public／Private 或 repository 個別差異寫在各 repo 的 `REPO_POLICY.md`。
- 個別產品／工具的永久例外寫在唯一 `PROJECT_RULES.md`：單一專案 repo 放根目錄；monorepo 放 `apps/<Project>/PROJECT_RULES.md`。

## 1. 規則層級與唯一來源

永久規則只允許存在於下列三層：

1. `REPOSITORY_RULES.md`：三個 repository 共通規則，AITeam 為母本。
2. `REPO_POLICY.md`：目前 repository 的 Public／Private、機密、CI 成本等 repo-specific 規則。
3. `PROJECT_RULES.md` 或 `apps/<Project>/PROJECT_RULES.md`：個別專案必要的補充或例外。

規則衝突時依下列優先順序：

1. 使用者當次明確指示。
2. 目標專案 `PROJECT_RULES.md`。
3. 目前 repository 的 `REPO_POLICY.md`。
4. 本 `REPOSITORY_RULES.md`。
5. 其他文件。

`README.md`、`WORK_HANDOFF.md`、`PROJECT_STATUS.md`、`TODO.md`、`CHANGELOG.md`、`VERSIONING.md`、版本紀錄與設計文件只描述用途、狀態、歷史、需求或待辦，**不得自行成為新的永久規則來源**。`AGENTS.md` 只可作為 AI 入口與規則索引，不得重複或新增另一套規則。

## 2. 共通規則與治理規則的變更

- 共通規則不得直接在 CYapps / CYapps_pvt 修改。要改共通規則，必須先在 AITeam 的 `governance/*` branch 修改母本，更新 `COMMON_RULES_VERSION` 與 `COMMON_RULES_CHANGELOG.md`，完成檢查後合併 AITeam `main`，再同步到其他 repo。
- 各 repo 自己的 `REPO_POLICY.md`、`PROJECT_RULES.md`、治理基礎設施變更仍使用該 repo 的 `governance/*` branch，更新該 repo 的 `GOVERNANCE_VERSION` 與 `GOVERNANCE_CHANGELOG.md`。
- `RULES_INDEX.md` 列出的檔案才是允許存在的治理／規則檔；新增第四層規則檔視為錯誤。
- 如果只是一次性任務例外，應寫在該次 PR／Issue／工作說明，不應直接變成永久規則。只有會反覆適用、且使用者同意的內容才升格為規則。
- 同步 workflow 可自動建立共通規則同步 PR，但不得自動修改 repo-specific policy 或 project rules。
- 治理 CI 應檢查未授權的 `*RULES*`、`*POLICY*`、`*INSTRUCTION*`、`*GOVERNANCE*` 類規則檔、版本檔與母本同步狀態。

## 3. 正式基準、branch 與 Pull Request

- `main` 是該 repository 的正式基準，只保存可追溯、可重建、已完成必要檢查的狀態。
- 日常開發使用獨立 branch；一般命名建議為 `<project>/<type>-<summary>`，例如 `cyinvoice/fix-order-status`、`cyaccounting/feature-backup`。治理工作使用 `governance/<summary>`。
- 一個 PR 原則上只處理一個專案或一個明確主題；不得順手修改無關專案。
- 合併前 PR 說明至少包含：變更目的、主要影響、測試／驗證結果、已知風險；高風險資料或 API 流程需另外說明。
- 已完成的短期 branch 合併後應刪除；長期實驗線、相容性線或歷史封存 branch 可由 `PROJECT_RULES.md` 明確保留。
- 不得任意 force-push `main`。只有歷史清理、機密移除等使用者明確同意的維護作業可例外執行，完成後必須重新驗證 refs。

## 4. Commit 身分與命名

- 新 commit 的 author／committer email 一律使用 GitHub private noreply：`286269326+simonliu1118-byte@users.noreply.github.com`；GitHub 官方 bot 自身的 noreply 身分除外。
- 不得再使用個人 Gmail 作為新 commit metadata。
- 中文名稱羅馬拼音一律採 Wade–Giles；志遠使用 `CY`、`Chihyuan` 或 `Chih-yuan`，不得使用 `Zhiyuan`。
- 專案、資料夾、檔名與程式識別優先沿用既有正式名稱，避免無必要更名造成相容性與追蹤問題。

## 5. 原始碼、執行資料與機密

- 原始碼、建置設定、必要資源、測試與維護文件應納入 Git。
- EXE、DLL、ZIP、7z、MSI、LOG、Cache、暫存檔、使用者資料、正式執行資料與本機設定原則上不得提交至 Git；個別專案例外必須由 `PROJECT_RULES.md` 明確列出。
- 可公開 repository 與其 Git 歷史不得包含正式密碼、API Key、token、OAuth client secret、private key、正式公司敏感資料、客戶／發票／帳務資料或其他機密。
- 公開測試憑證只有在來源本身已由供應商公開，且 `PROJECT_RULES.md` 明確記錄其用途時才可進入 Public source。
- 任何可能公開的程式不得依賴寫死在原始碼中的固定管理密碼、清除密碼或其他秘密；應使用安全的本機設定、雜湊或作業系統安全儲存。
- `.gitignore` 只能防止未來誤提交，不能視為已清除歷史。秘密一旦進入 Git，必須另做 history cleanup／rotation／風險處理。

## 6. 版本號、Build 與版本來源

- 每個可發行專案根目錄必須有 `VERSION`，內容只放基礎版本 `X.Y.Z`，作為程式、CI、封裝與 Release 的版本來源。
- 專案以 `BUILD` 整數保存同一工作項目的返修次數：`0` 代表不顯示 Build；`1` 代表 `Build 1`；依此類推。既有專案首次導入時預設 `0`。
- 使用者可見版本格式為 `VX.Y.Z`；當 `BUILD > 0` 時為 `VX.Y.Z Build N`。
- `X`（Major）代表重大產品世代。**只有使用者可以決定升 X**；AI 不得自行升 Major。
- `Y`（Minor）代表使用者能明顯感受到的新能力、完整功能階段或具份量的功能升級。負責開發的 AI 可依實際工作內容自行判斷是否升 Y，升 Y 時 Z 歸零、Build 歸零；PR／版本說明需簡要說明升 Y 理由。一般修正、小改善或單一 UI 調整不得濫用 Y。
- `Z`（Patch）是日常開發的預設版本遞增單位。開始新的獨立修改項目、新 bug、新需求或小型功能時，通常 Z + 1，Build 歸零。
- 同一個 Z 所代表的工作項目若第一次交付／測試後仍未達成原要求，繼續修正**不再升 Z**，改為 Build + 1。例如：`V1.0.2` → `V1.0.2 Build 1` → `V1.0.2 Build 2`。
- 上一個工作項目完成後，開始另一個獨立項目時再升下一個 Z；若新工作本身達到 Minor 標準，AI 可改升 Y。
- 「同一項目返修」包含上一版尚未修好的同一 bug、同一功能驗收失敗、同一原需求未完整達成；「新項目」包含原要求完成後提出的新修改、不同 bug、不同功能或新增需求。界線不清楚時，AI 必須先詢問使用者。
- 單純重跑完全相同 source 的 CI、runner／網路失敗後 retry、重新下載同一 artifact，不改 `VERSION` 也不改 `BUILD`；GitHub workflow run number 只用來識別 CI 執行批次，**不是**本規則中的 Build N。
- 尚未達 1.0 的專案可使用 `0.Y.Z`；由 `0.x.x` 升為 `1.0.0` 視為 Major 決策，由使用者決定。
- preview／RC／獨立實驗線等特殊版本身分可以由 `PROJECT_RULES.md` 定義，但不得與正式產品線混淆。
- 正式 tag 預設使用專案明確可辨識的 `vX.Y.Z` 或 monorepo `<project>-vX.Y.Z`；各專案既有 tag 慣例可在 `PROJECT_RULES.md` 固定。
- 若準備正式 Release 時目前 `BUILD > 0`，不得擅自把 Build 身分消失或覆寫成不同內容的同版；應先向使用者確認正式發布的版本身分／升版方式。
- 已存在的正式 tag／Release 不覆寫；需要後續修改時依上述 X/Y/Z/Build 規則建立新的版本身分。
- 測試包／工程 Artifact 必須顯示 `VERSION` 與適用的 `Build N`，並可另外附 workflow run number 或 short SHA 作技術追蹤。

## 7. CI / GitHub Actions / 協作 AI

- CI 的目的，是以合理自動化成本提高品質與可重建性；不得為省少量資源而增加大量人工步驟，也不得把 CI 當作每個小修改的試錯迴圈。
- Public 與 Private repo 都可正常使用自動 CI；Private 需更留意 Actions minutes，但不以犧牲便利性與可靠性換取小幅節省。
- PR 進入 `main` 前應自動執行與該專案相關的必要驗證；使用 `paths`／`paths-ignore` 避免不相關專案一起跑。
- 一般開發 branch 的每次 push 原則上不重複跑完整昂貴 CI；若能明顯降低維護成本或風險，可由 workflow 明確保留。
- Draft PR 可略過昂貴完整 CI；Ready for review 後應進入必要驗證。不得要求使用者為省少量 minutes 額外反覆手動操作。
- 同一 PR 新 commit 應以 `concurrency` 取消尚未完成的舊 run。
- 主要 Build／Test workflow 應保留 `workflow_dispatch`，供必要時手動驗證。
- Build／Test 預設只授予 `contents: read`；只有確實需要建立 tag／Release／寫入 repo 的 workflow 才給 `contents: write`。
- 純測試 CI 與 Codex、Claude 等計量式 AI 審查應盡量解耦；不得每次 push 都重新啟動昂貴 AI 審查。
- GitHub 官方 Actions 使用仍受支援的穩定 major 版本；不為追新而無意義頻繁升級。

## 8. 測試包、Artifact 與上傳規則

- 開發中供驗證的 Windows x64 包預設使用 Actions artifact；正式版使用 GitHub Release，除非 `PROJECT_RULES.md` 明確定義不同流程。
- 工程 artifact 預設保留 14 天；需要更長保存時由專案規則或該次工作明確決定。
- 正式 Windows 發行預設提供 portable package，不要求安裝器；若專案需要 MSI／installer，必須由 `PROJECT_RULES.md` 另行規定。
- 正式 ZIP／EXE 必須可由 repository 的正式 source、依賴與 build script 重建。
- 封裝物不得含 runtime 個資、正式資料、local settings、log、cache 或未授權秘密。
- 正式發行至少提供 SHA-256；若只有單一 EXE，也應對可下載檔提供 hash。
- Git 不作為 binary release 倉庫；正式 binary 放 Release，短期驗證 binary 放 Artifact。

## 9. 正式 Release

- 正式 Release 屬低頻且具外部影響的動作，原則上以 `workflow_dispatch` 或其他明確人工啟動方式執行，不因一般 branch push 自動發布；專案若有明確例外，必須寫入 `PROJECT_RULES.md`。
- 正式 Release 預設只能由 `main` 建置；專案若有例外，必須寫入 `PROJECT_RULES.md`。
- Release workflow 必須重新核對 `VERSION`、`BUILD`、必要測試、敏感資料掃描、建置／封裝、SHA-256 與 tag，不得只依賴先前某次 CI 成功。
- Release title、tag、asset 必須與版本身分一致；若經使用者明確批准帶 Build 發布，不得冒充無 Build 的同版。
- 每個正式 Release 應保留該版變更摘要；專案可使用 `CHANGELOG.md`、`VX.Y.Z.txt` 或兩者，但內容不得與 `VERSION`、`BUILD`、tag、Release title 不一致。
- Public repo 的正式 Release 可公開下載；公開下載不代表取得根 `LICENSE` 以外的權利。Private repo 的 Release 必須維持 private。

## 10. Copyright、License 與年份

- 正式 Release 的標準 notice：`Copyright © <YEAR> C.C. Liu, Chihyuan Co. All Rights Reserved.`
- `<YEAR>` 使用該 Release 實際發布年份。
- Repository 根 `LICENSE` 可使用起始年份或年份區間，例如 `2026`、`2026–2027`；Public 與 Private repo 可以使用不同授權內容。
- README、發行說明、使用說明與 package 內文件不得寫出與根 `LICENSE` 相衝突的權利或散布條款。

## 11. AI 開發與交接

- 接手專案前，AI 必須先讀：根 `REPOSITORY_RULES.md` → 根 `REPO_POLICY.md` → 專案 `PROJECT_RULES.md` → 再讀 `WORK_HANDOFF`／`PROJECT_STATUS`／README／TODO 等狀態文件。
- 若 AITeam 正在協作其他已具治理規則的 repository，必須先讀目標 repository 的規則，**不得用 AITeam 自己的專案流程覆蓋目標 repo 的版本、Release 或安全規則**。
- 不得因舊對話、舊 branch 或記憶與 `main` 不一致就直接覆寫正式 source；應先確認目前正式基準。
- 不得自行大改架構、重寫 UI 或更換技術棧，除非使用者明確同意或現有方案已證明無法安全維護。
- 發現規則矛盾、資料不明確、不可逆操作、可能外洩或版本身分混淆時，必須先說明差異、風險與建議，再處理。
- 專案特例只寫入唯一 `PROJECT_RULES.md`；禁止因一次 bug 或一次需求新增新的規則檔。
