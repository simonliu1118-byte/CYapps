# CYInvoice PR #602 與最新主線整合紀錄（2026-11-12）

## 範圍及限制

- 僅允許編輯 `apps/CYInvoice/**` 的程式碼、測試與專案文件。本整合不操作其他專案的 PR、branch、worker、repo、資料庫或正式環境；若未來需要跨專案操作，必須再次徵求使用者同意。
- **本輪嚴格只做本機端功能；Cloud 0.9.15、雲端提出者撤回、Worker／D1 修改與部署全部不在本輪工作範圍，另交使用者指定的本機 Work 處理。**
- 以最新 `main` 的正式 CYInvoice V3.1.50 為基準；原 [PR #602](https://github.com/simonliu1118-byte/CYapps/pull/602) 為 V3.1.50 Build 7 的歷史 Draft，不能直接合併或整包 cherry-pick。
- 特別不要把 #602 的 `StartupCheckOverlay`／舊開機轉圈程式、VERSION=3.1.50／BUILD=7、舊版 README 以及未發行的 Cloud 描述覆寫回主線。現行正式版已有 Claude #614 的啟動／匯入動畫修正。
- #612 已關閉、未合併，內容被 #614 取代；已加上歸檔註記。舊分支可刪除，但目前 GitHub Connector 沒有 branch delete 操作，未執行刪除。

## 主線確認的缺口

1. **本機 G0401 提出者撤回：** `EmployeeAllowanceWorkflowService.CancelManualReview` 目前只允許管理員；`SyncIssuesForm` 的待辦開啟也強制管理員登入。#602 允許已驗證提出者只讀取／撤回自己尚未核准的申請；主管原有權限保留，不擴大折讓作廢權限。
2. **雲端 G0401 提出者撤回與安全條件（留待本機 Work）：** 目前 Cloud 僅有既有主管權限；#602 Cloud Source 0.9.15 的提出者權限、Workspace／Device／revision／Grant／Ledger 原子 guard，不在此 PR 實作、修改或部署。桌面端不得假設雲端已支援一般員工撤回。
3. **本機狀態即時更新：** 使用現有本機已確認的撤回／退回結果刷新畫面與精確 application pending 投影；涉及未部署 Cloud 契約的雲端終態更新保留為交接項目，舊回應不可解鎖新申請、結果不明絕不清除。
4. **折讓詳情與表格：** 尚缺三區式原發票／本次折讓／審核資訊畫面；缺共用原生 ListView 編輯器、序號、空資料與捲動布局。不能由資料缺口自行推算或填造稅額、提出者及官方查證資料。
5. **摘要與原因：** 處理中顯示帳號及可信姓名與折讓原因（不增加逐筆 API 查詢）；折讓原因與取消退回原因使用單行最多 20 字。

## 工作階段

- [x] Stage 0：比對 main 與 PR #602，封存 #612 結案說明，建立此獨立 CYInvoice 工作分支。
- [x] Stage 1（本次起始）：移植**本機** G0401 提出者撤回的身分／權限守門、最長 20 字單行原因，以及對應只讀詳情／撤回操作；建立 synthetic 測試，確認不能撤回他人、核准後、官方待查案件，且不增加 AMEGO 查詢。
- [ ] Stage 2：完成本機立即狀態刷新、只處理精確對應的本機申請；不改 Cloud API 或雲端權限。
- [ ] Stage 3：完成本機可確認的單筆 metadata 刷新；雲端終態投影／0.9.15 保留待本機 Work，結果不明保留 pending。
- [ ] Stage 4：重構折讓頁面／三區詳情／原生表格／原因與序號；保留現行主線的最新動畫、自動更新與 AutoInput 契約。
- [ ] Stage 5：Windows x64、現有 Cloud 契約相容性（不更動 Cloud source）、Governance CI、封裝與 96／125／150 DPI 視覺驗證；未完成真人測試另列清單，完成後把雲端交接文件交給本機 Work；只在得到明確授權後合併或發行桌面 Release。

## 安全門檻

- 一般員工不得核准折讓、撤回他人或擴大 G0501 折讓作廢權限。
- 所有本機撤回必須以同一未核准申請、有效帳號及原有 ledger／pending guards 為依據；對 unknown／approved／issued／claimed 禁止擅自釋放。
- 本輪不實作或啟用雲端提出者撤回；所有 Cloud 端的權限／終態更新需求僅記錄交接，不改 Cloud 原始碼，任何歧義都 fail-closed。
- 不接觸正式個資、正式 AMEGO 開票或 Cloud D1、Worker deployment；測試使用合成資料。
