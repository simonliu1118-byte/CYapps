# Shared Common Rules Changelog

## 2.4.0 — 2026/09/13

- 一般 Build／Test workflow 預設同時支援 `pull_request` 與 `workflow_dispatch`；PR 自動驗收是 AI／日常開發的標準入口，手動 dispatch 只作備援。
- Draft PR 也可正常執行 Build／Test；Draft 僅表示尚未準備合併，不再兼任 CI 開關，也不得要求 AI 為觸發 CI 反覆切換 Draft／Ready。
- 一般 branch push 不再額外重複跑一套完整昂貴 CI；有 PR 時由 PR workflow 驗收，並以 `paths`／`paths-ignore` 與 `concurrency` 控制成本。
- 節省 Actions 與 Token 的主要方式固定為 Local-first、集中修改、減少 push、path filter、concurrency 與只讀必要 log；不得把 Build/Test 全部改成 manual-only 來省資源。
- 一個合理修改批次完成後的 push 視為遠端驗收邊界；已有 PR 時應自動驗收，不要求 AI 另外具備手動 Run workflow 能力。

## 2.3.0 — 2026/09/13

- 明確將 Wade–Giles（威妥瑪）定為全域共通羅馬拼音規則：凡中文名稱需要轉寫為羅馬拼音時一律使用 Wade–Giles。
- 既有正式英文名、品牌名、產品名與固定 spelling 仍由各 repo 的 `REPO_POLICY.md`／`PROJECT_RULES.md` 定義；因此公司專屬名稱不會被帶入個人 sandbox。

## 2.2.0 — 2026/09/13

- 將共通母本泛化為可同時供公司與個人 repository 使用的中性規則；公司／個人身分、品牌、特定固定拼法、copyright 與授權改由各 repo 的 `REPO_POLICY.md` 定義。
- 共通母本不再直接包含特定公司名稱、公司 copyright 或特定下游 repo 名稱；AITeam 仍是唯一共通母本來源。
- 新增 WPF 至 Windows-specific 正式驗證範圍，延續 Local-first / GitHub-final verification 原則。
- 允許個人 sandbox 等 repo 直接同步同一份共通母本，而不攜帶公司專屬內容。

## 2.1.0 — 2026/09/13

- 新增 Local-first / Token-efficient 開發原則：同一工作階段只讀必要檔案，避免無理由反覆完整重讀 repository。
- 同一輪相關修改先集中於工作環境完成、本地 test／lint／可行 build 後，再形成合理 commit／push 單位，避免每個小修正都觸發 GitHub 往返與 CI。
- GitHub Windows CI 定位為正式 Windows 驗收層；Windows-specific、resource／manifest、DLL／Registry／printer／WebView2／PowerShell 與 Release 等仍須真正 Windows 驗證。
- Actions 成功時只確認 job／test／artifact 結果；失敗時先讀必要錯誤區段，原因不明才逐步擴大 log，避免把完整長 log 無理由載入上下文。
- 明確規定不得以節省 Token 為理由省略必要編譯、測試、封裝與正式驗收；個別專案可在 `PROJECT_RULES.md` 依技術特性補充例外。

## 2.0.0 — 2026/09/13

- 建立可由多個 repository 共用的三層治理母本。
- 永久規則固定為：共通 `REPOSITORY_RULES.md`、repo-specific `REPO_POLICY.md`、project-specific `PROJECT_RULES.md`。
- 共通規則的唯一母本由 `simonliu1118-byte/AITeam` 的 `main` 維護；其他採用本治理體系的 repo 保存同步副本，不得自行分叉修改。
- 建立 `COMMON_RULES_VERSION`，共通規則每次變更必須同步升版並記錄本檔。
- 版本制度固定為 X.Y.Z + Build：X 只由使用者決定；Y 可由 AI 依明顯功能階段判斷；Z 為日常新工作項目；Build N 僅用於同一項目未完成的返修。
- 統一 branch / PR、CI、Artifact、Release、SHA-256、portable Windows 發行、noreply commit identity、機密處理與治理檔白名單原則。
