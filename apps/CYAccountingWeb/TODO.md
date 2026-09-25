# CYAccountingWeb TODO

本文件只記錄待辦、後續方向與未來評估項目，不作為永久規則來源。

## 電腦版核心功能移植

- [x] 基本記帳新增、編輯、刪除。
- [x] 帳戶、收入／支出科目、常用科目管理。
- [x] 期初餘額與月份鎖帳。
- [x] 常用摘要基本版（帳戶＋收支＋科目）。
- [x] CYInvoice Cloud 共用員工登入、Session 與 Email 忘記密碼。
- [x] 記帳資料表月份前後切換、摘要搜尋、完整月統計、逐筆餘額與帳戶分組檢視（V0.6.0）。
- [x] 記帳資料表直接欄位編輯：按編輯後原列直接切換為輸入控制，Enter 儲存、Esc 取消；鎖帳列不可編輯（V0.14.0）。
- [x] 輸入確認區：最近 10 筆存檔結果、成功／失敗狀態與千分位顯示（V0.7.0；後續已改為右側可收合側欄）。
- [x] 常用摘要設定：統計依據（帳務日期／近期輸入）、最近 N 筆、最低出現次數（V0.11.0）。
- [x] 帳戶、大分類、科目的 ↑／↓ 自訂排序（V0.11.0）。
- [x] 科目跨大分類移動（V0.12.0；限同一收支類型）。
- [x] 日期輸入桌面快速操作：YYYYMMDD、MMDD 四碼重填、Ctrl+↑／↓ 日期步進（V0.11.0）；日期 picker 目前保留原生 Web 控制。
- [x] 逐月鎖帳操作流程（V0.12.0）；設定頁仍保留管理者直接指定鎖帳月份的繞過流程。
- [x] 單月 Excel 匯出：直接產生標準 `.xlsx`，包含月統計、逐筆餘額與期初餘額工作表（V0.13.0）。
- [x] Excel 匯入：`.xlsx` 工作表選擇、標題列／欄位對應、預覽、7 位數驗證、鎖帳檢查、重複略過與確認後寫入（V0.15.0）。
- [ ] 既有 CYAccounting SQLite 帳本匯入／遷移工具。

## 備份／復原與高風險操作

- [x] 備份架構：Cloudflare D1 為唯一正式帳務資料來源；異地備份不作 live database 或雙向同步資料庫（V0.16.0 起）。
- [x] V0.16.0 曾完成 Google Drive OAuth／自動備份技術基礎；正式異地備份改採 Google Cloud Storage，Drive provider 僅保留作既有可重用邏輯來源，待 GCS production acceptance 後清理舊 runtime 路徑。
- [x] V0.17.0 provider-neutral boundary：`BackupService` 與 `BackupStorageProvider` 分離；GCS adapter 實作 `putObject`、`getObject`、`listObjects`、`deleteObject`。
- [x] V0.17.0 portable backup set 改為 `manifest.json` + `data.json`；包含 App/schema version、資料筆數、SHA-256 與 byte size，上傳後兩檔均需 read-back 驗證成功才記為有效備份。
- [x] 備份排程維持每日 03:30（台灣時間）；保留政策為最近 **14 天**，程式依 storage object 建立時間清理過期備份。
- [x] GCS 基礎設施準備：CYAccountingWeb 使用獨立 backup dataset 與獨立 least-privilege Service Account；bucket-scoped IAM 已驗證，Cloudflare Worker 已建立 `GCS_BUCKET` 與 `GCS_SERVICE_ACCOUNT_JSON` Secrets。Public Git 不保存正式 resource identifiers 或 credential。
- [x] **自動備份上線驗收（2026-09-26）**：V0.17.0 正式 Worker 首次實機備份成功；GCS 實際建立 `data.json` + `manifest.json`，Web UI 成功紀錄與 GCS 物件均確認存在，read-back SHA-256、byte size、row count、App/schema version 與 manifest 驗證均通過。
- [ ] Google Cloud Billing 設定每月低額預算警示（目標 NT$100）。
- [ ] 建議在 GCS Bucket 另設 14 天 Object Lifecycle 刪除規則，作為程式 retention 之外的第二層保護。
- [ ] Cloud Storage 復原功能 **僅 `SUPER_ADMIN` 可執行**；`ADMIN` 與 `EMPLOYEE` 均不可復原。
- [ ] 復原採雙重確認；第二次必須明確提示將覆蓋目前 D1 資料，且 Worker/API server-side authorization 為權威，不得只靠 UI 隱藏。
- [ ] 復原前驗證備份格式、App/schema version、manifest 與完整性；復原操作需留下操作者、時間、backup identifier 與結果等 audit evidence。
- [ ] 驗證「Cloudflare/D1 故障後，以 Cloud Storage 最近有效備份重建新 D1」的完整災難復原演練。
- [ ] Web UI **不提供「清除全部帳務資料／期初餘額」功能，也不提供對應一般應用 API**。若真的需要整庫清理，視為平台管理／維運操作，直接在 Cloudflare／D1 管理層處理。

## UI／UX 與多裝置支援

- [x] Desktop UI/UX Phase 3：收斂 Header 與主畫面垂直空間、固定記帳資料表欄寬比例、加強金額／餘額掃讀與列 hover、收窄操作欄，並提高設定視窗管理密度（V0.10.0）。
- [x] Desktop UI/UX Phase 2：主工作區縮窄約 20%、收支改雙態切換、輸入確認改右側 edge sidebar、期初餘額移至記帳資料區、重整記帳工具列（V0.9.0）。
- [x] Desktop UI/UX Phase 1：輸入確認改為右側可收合 drawer、修正版本顯示單一來源、提高記帳資料表桌面資訊密度（V0.8.0）。
- [ ] 完成功能後集中進行一輪 UI／UX 重整，避免開發期間因版面反覆調整增加返工。
- [ ] 採單一網站的 RWD 為基礎，不另做獨立 PC／手機兩套網站。
- [ ] 在 RWD 基礎上加入 Adaptive UI：相同資料與功能可依裝置使用不同 presentation，而非只把桌面版等比例縮小。
- [ ] 建議初步 breakpoint：Desktop `>= 1024px`、Tablet `768–1023px`、Mobile `< 768px`；實際數值於 UI／UX 階段依真實畫面驗證調整。
- [ ] 手機版重點：導覽 drawer、表格卡片化／重要欄位優先、單欄表單、較大的觸控區、Modal／設定頁可改全螢幕或 sheet 呈現。
- [ ] Desktop 仍以高資訊密度與鍵盤高效率輸入為主要操作模式；Mobile 以查詢、確認、快速輸入與簡單修改為優先。
- [ ] 若未來出現掃碼、拍攝單據、離線作業、Push Notification 等強烈行動裝置需求，再評估 PWA 或原生 App；目前不提前拆成第二套前端。

## 長期整合方向

- [ ] 未來可評估將 CYAccountingWeb 納入 **Chihyuan 企業管理系統**，作為其中的記帳／財務模組之一。
- [ ] 在真正整合前，CYAccountingWeb 仍維持獨立部署、獨立帳務資料庫、獨立 backup dataset／GCS service identity 與清楚 API 邊界，避免為尚未定案的企業入口過早耦合。
- [ ] 若未來 Chihyuan 企業管理系統整合多個 CY 工具，再統一規劃入口、導覽、角色／App 權限與共用帳號體驗。
- [ ] Backup 整合優先採「各 App → 共用 CY Backup Service／Worker → GCS」；不以直接共用同一把 GCS credential 作為整合方式。
- [ ] 即使改由共用 Backup Service 管理，各 App 的備份資料仍維持邏輯隔離與獨立還原能力。
- [ ] 目前共用帳號權威仍暫放 CYInvoice Cloud；等更多程式實際共用後，再評估抽出獨立的 CY Identity／SSO 服務。
