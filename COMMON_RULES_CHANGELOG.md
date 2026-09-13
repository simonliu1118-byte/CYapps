# CY Common Rules Changelog

## 2.1.0 — 2026/09/13

- 新增 Local-first / Token-efficient 開發原則：同一工作階段只讀必要檔案，避免無理由反覆完整重讀 repository。
- 同一輪相關修改先集中於工作環境完成、本地 test／lint／可行 build 後，再形成合理 commit／push 單位，避免每個小修正都觸發 GitHub 往返與 CI。
- GitHub Windows CI 定位為正式 Windows 驗收層；Windows-specific、resource／manifest、DLL／Registry／printer／WebView2／PowerShell 與 Release 等仍須真正 Windows 驗證。
- Actions 成功時只確認 job／test／artifact 結果；失敗時先讀必要錯誤區段，原因不明才逐步擴大 log，避免把完整長 log 無理由載入上下文。
- 明確規定不得以節省 Token 為理由省略必要編譯、測試、封裝與正式驗收；個別專案可在 `PROJECT_RULES.md` 依技術特性補充例外。

## 2.0.0 — 2026/09/13

- 建立 AITeam / CYapps / CYapps_pvt 三個 repository 共用的治理母本。
- 永久規則固定為：共通 `REPOSITORY_RULES.md`、repo-specific `REPO_POLICY.md`、project-specific `PROJECT_RULES.md`。
- 共通規則的唯一母本改由 `simonliu1118-byte/AITeam` 的 `main` 維護；CYapps / CYapps_pvt 保存同步副本，不得自行分叉修改。
- 建立 `COMMON_RULES_VERSION`，共通規則每次變更必須同步升版並記錄本檔。
- 版本制度固定為 X.Y.Z + Build：X 只由使用者決定；Y 可由 AI 依明顯功能階段判斷；Z 為日常新工作項目；Build N 僅用於同一項目未完成的返修。
- 統一 branch / PR、CI、Artifact、Release、SHA-256、portable Windows 發行、noreply commit identity、機密處理與治理檔白名單原則。
