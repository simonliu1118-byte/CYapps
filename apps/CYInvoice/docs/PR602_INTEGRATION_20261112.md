# CYInvoice PR #602 與最新主線整合紀錄（2026-11-12）

## 範圍及限制

- 僅允許編輯 `apps/CYInvoice/**` 的程式碼、測試與專案文件。本整合不操作其他專案的 PR、branch、worker、repo、資料庫或正式環境；若未來需要跨專案操作，必須再次徵求使用者同意。
- 以最新 `main` 的正式 CYInvoice V3.1.50 為基準；原 [PR #602](https://github.com/simonliu1118-byte/CYapps/pull/602) 為 V3.1.50 Build 7 的歷史 Draft，不能直接合併或整包 cherry-pick。
- 特別不要把 #602 的 `StartupCheckOverlay`／舊開機轉圈程式、VERSION=3.1.50／BUILD=7、舊版 README 以及未發行的 Cloud 描述覆寫回主線。現行正式版已有 Claude #614 的啟動／匯入動畫修正。
- #612 已關閉、未合併，內容被 #614 取代；已加上歸檔註記。舊分支可刪除，但目前 GitHub Connector 沒有 branch delete 操作，未執行刪除。

## 主線確認的缺口

1. **本機 G0401 提出者撤回：** `EmployeeAllowanceWorkflowService.CancelManualReview` 目前只允許管理員；`SyncIssuesForm` 的待辦開啟也強制管理員登入。#602 允許已驗證提出者只讀取／撤回自己尚未核准的申請；主管原有權限保留，不擴大折讓作廢權限。
2. **雲端 G0401 提出者撤回與安全條件：** 現行 `cloud/src/allowances-route.ts` 已有 withdraw 管道但僅主管可用；#602 Cloud Source 0.9.15 加入可信 requester actor、Workspace／Device／revision／Grant／Ledger 原子 guard。僅能在本地合成測試完整通過後再規劃部署，不能將原始碼修改當作線上已部署。
3. **確認退回後立即解除待處理投影：** 原版 Cloud 終態可能仍讓發票持有 pending metadata。#602 的做法只對完全相符的帳戶、原發票及 application ID 清除，舊回應不解鎖新申請；結果不明絕不清除。
4. **折讓詳情與表格：** 尚缺三區式原發票／本次折讓／審核資訊畫面；缺共用原生 ListView 編輯器、序號、空資料與捲動布局。不能由資料缺口自行推算或填造稅額、提出者及官方查證資料。
5. **摘要與原因：** 處理中顯示帳號及可信姓名與折讓原因（不增加逐筆 API 查詢）；折讓原因與取消退回原因使用單行最多 20 字。

## 工作階段

- [x] Stage 0：比對 main 與 PR #602，封存 #612 結案說明，建立此獨立 CYInvoice 工作分支。
- [x] Stage 1（本次起始）：移植**本機** G0401 提出者撤回的身分／權限守門、最長 20 字單行原因，以及對應只讀詳情／撤回操作；建立 synthetic 測試，確認不能撤回他人、核准後、官方待查案件，且不增加 AMEGO 查詢。
- [ ] Stage 2：安全移植 Cloud G0401 requester-only 讀取／withdraw API、Client 與 0.9.15 source 契約及完整權限／併發測試；不部署 Cloud。
- [ ] Stage 3：確認終態後的單筆 metadata 原子刷新；結果不明保留 pending，防舊結果解鎖新 application。
- [ ] Stage 4：重構折讓頁面／三區詳情／原生表格／原因與序號；保留現行主線的最新動畫、自動更新與 AutoInput 契約。
- [ ] Stage 5：全部 Windows x64、Cloud、Governance CI、封裝與 96／125／150 DPI 視覺驗證；未完成真人測試另列清單；只在完成整合及得到明確發行授權後合併／部署／Release。

## 安全門檻

- 一般員工不得核准折讓、撤回他人或擴大 G0501 折讓作廢權限。
- 所有本機撤回必須以同一未核准申請、有效帳號及原有 ledger／pending guards 為依據；對 unknown／approved／issued／claimed 禁止擅自釋放。
- Cloud 終態更新必須重新查核原 workspace／device／account／invoice／application／revision；任何歧義都 fail-closed。
- 不接觸正式個資、正式 AMEGO 開票或 Cloud D1、Worker deployment；測試使用合成資料。
