# CYInvoice PR #602 與最新主線整合紀錄

更新：2026-11-24（Asia/Taipei）。正式發行目標 V3.1.52／Cloud 0.9.15（已部署），PR [#616](https://github.com/simonliu1118-byte/CYapps/pull/616)。

## 範圍及限制

- 基準為正式 V3.1.50；原 [PR #602](https://github.com/simonliu1118-byte/CYapps/pull/602) 是歷史 Draft，不整包合併或 cherry-pick。#612 已關閉且由已合併 #614 取代。保留正式版 BusySpinner、自動更新與 AutoInput。
- 11/12 原範圍只做本機；11/24 使用者要求此對話繼續 Cloud 程式整合，並修鼎新贈品匯入及合併日誌路徑。本輪限定 `apps/CYInvoice/**`。本批後續已由使用者明確批准Cloud0.9.15部署、#616合併及V3.1.52正式Release；D1 migration及真交易仍不在範圍內。
- 原Worker Cloud0.9.15已於2026-11-24完成部署，固定Source `abcf554809842973f218ee30d9dde484850d23b3`；schema／marker23／CYID_ONLY、正式CYID、Bindings、Secrets、旗標及Routes均保留，後驗通過。原0.9.14不可變版本、程式副本、設定及Time Travel bookmark保留於本機私有回復證據。

## 本輪整合結果（原始碼狀態，非真人驗收）

- Stage 0／1：比對原 PR、保留正式主線、實作本機提出者身分驗證與自身待核准申請撤回；保留主管及 Ledger 原限制。
- Stage 2／3：已加入單筆本機 metadata 刷新、Cloud 終態精確帳戶／application 的 SQLite JSON pending marker 移除；未知結果不得解除，舊回應不得解鎖新申請。
- Stage 4：已加入三區式折讓詳情、原生 ListView、編輯器、序號、處理中摘要及單行 20 字原因。雲端員工必須經伺服器驗證本人未核准 G0401 才可讀取／撤回；G0501、歷史詳情及核准仍保留主管權限。使用者可看自己待核准申請，不能核准或撤回他人。
- Cloud Source：原 #602 Cloud 權限、revision／Workspace／Device／Grant／Ledger guard 已逐檔核對並移植，加入 Device authority state 原子檢查。更新 Worker health/package 版本與配套合成測試。0.9.15部署後仍須通過伺服器驗證；不把無憑證拒絕或合成測試當作真人業務驗收。
- 鼎新贈品匯入：明確類型、正贈品量、銷貨量及全部已有金額／稅額為 0 才可使用實際贈品量建立零價明細；非明確贈品或不合法金額仍拒絕。維持原本整單合計核對與使用者確認流程。
- 日誌：統一根目錄 `log` 寫入；主程序取得 mutex 後搬移已知舊 `Logs/import-error.log`，同名保留，空目錄才移除，未知檔案保留；Data 內既有舊 IssueDiagnostics 讀取相容性維持。新增合成回歸測試，未提交真實 Excel 或日誌。

## 驗證與剩餘工作

- Build 1 修正 `2f4f0dce` 的 CS0117：runtime sync 殘留呼叫已移除的 `CloudAllowanceSubmissionService.CompleteProjection`。刪除該呼叫及其集合，沿用 `InvoiceSyncIssueStore.ApplyCloudAllowance` 的唯一精確終態處理，不恢復整庫載入／回寫或新增平行 owner。
- 本機Cloudflare MCP已完成當批授權、用量核對、有界前後檢與原Worker部署；完整完成摘要見MCP交接。沒有D1寫入、migration、CYID D1操作或真交易；正式POST探測被自動審查拒絕而未執行，權限案例採隔離測試。
- Build 1 本機已通過 Windows x64 build、WinForms startup smoke、來源機密掃描，以及 Core 84／Allowance 29／Sync 38／SyncCoordinator 15／Void 13／VoidWorkflow 43／SQLite migration 14 項與全部 Cloud client 合成測試。既有 WebView2 WindowsBase MSB3277 警告未新增；這些檢查不代替最新提交的 CI 或真人 DPI／業務驗收。
- 設定身分驗證 Smoke Test 的 Timer 輪詢逾時已於 `e5fcf5c894ff74e55d05afceeb6e4ca96b1edfaa` 修正，當時 Windows、Cloud Check、Governance 均通過。後續新提交不得沿用舊 CI 結果。
- 鼎新與日誌合成測試已加入；Cloud Source 與桌面整合提交 `25fd5c92035c3b8b75869b3cb1013caf86d820ce`。必要 CI、Cloud dry-run、Windows x64、WinForms smoke、工程封裝以 PR 最新 head 的 Actions 實際結論為準。
- [x] Stage 5自動化基準：`abcf5548`四條必要CI、工程包及本機回歸通過；V3.1.52版號／文件提交仍須核對其最新必要CI。
- [ ] 96／125／150 DPI及真人業務驗收仍依唯一清單追蹤，不因使用者批准本批發行而記為已完成。
- [x] Cloud0.9.15原Worker已依當批明確授權完成部署及後驗，schema23與原設定保留；完成步驟不重跑。
- [ ] 使用者已批准本批合併及V3.1.52正式Release；完成當前提交必要CI後合併，再由main執行正式Release workflow。不冒充Claude審查、真人或DPI驗收已完成。

## 安全門檻

一般員工不得核准折讓、撤回他人或擴大 G0501 權限。撤回須以同一有效未核准申請、已驗證 actor、active Workspace／Device、revision 及既有 Grant／Ledger guards 為依據；對 unknown／approved／issued／claimed 不得擅自釋放。不得使用本輪上傳的正式營運資料作公開 fixture 或執行正式 AMEGO 交易。
