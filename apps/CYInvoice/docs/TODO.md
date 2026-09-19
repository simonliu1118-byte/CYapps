# CYInvoice 待辦與驗證

本檔只保留**目前仍未完成、需要後續驗證或尚未取得足夠樣本**的工作。已完成內容與歷史決策應由 `CHANGELOG.md`、`MIGRATION_HISTORY.md` 與設計文件保存，不在 TODO 重複堆疊。

目前工程開發基準：**CYInvoice V2.5.0 Build 3**  
最新正式 Release：`cyinvoice-v2.4.2`

## 1. V2.5 收尾

V2.5 員工帳戶、三級權限、超級管理員復原碼、舊管理密碼遷移、正式發票作廢、紙本未收回人工確認、作廢 pending 防重、同步與 UI 整合均已完成工程實作。詳細規格見 `V2_5_EMPLOYEE_VOID.md`。

Build 3 已統一以下最終規格：

- Email 為員工資料必填欄位，UI 與 `EmployeeStore` 底層一致拒絕空白 Email。
- 作廢原因最多 **10 字**；UI 與 `EmployeeVoidWorkflowService.MaxReasonLength` 共用同一規則。
- `CancelReason` 維持 `<4碼員工編號> <原因>`；AMEGO 上限仍為 20 字。
- V2.5 新增帳戶／密碼／復原／作廢視窗已統一採精簡尺寸、Label／欄位對齊、輸入靠左與 CYInvoice Icon；復原碼唯讀顯示刻意保留置中。
- 詳細頁「作廢」與最後「確認作廢」維持標準按鈕形狀，只套紅底白字 danger 色。
- 作廢責任聲明全文粗體。
- 進入最終作廢確認後，背景詳細頁只遮蔽可照抄的發票號碼、標題號碼及 PDF／模擬發票預覽；其他背景資料仍可見。驗證失敗重試期間持續遮蔽，離開作廢確認流程即還原；不禁止貼上。
- `invoice_status=99` 全系統固定為完成／綠燈；等待作廢由發票狀態表達，不改寫 upload status。
- 近 3 天同步仍走既有 `invoice_list`；只有 pending 發票超出 recent list 覆蓋範圍時，才單筆 `invoice_query` 補洞，不自動重送 `f0501`。

### 尚待完成

- [ ] Build 3 Windows CI：warnings-as-errors build、WinForms startup smoke、核心測試、作廢 workflow、SQLite migration、同步、Windows x64 package、portable smoke。
- [ ] 使用者實機回歸 Build 3 的 V2.5 新視窗與完整作廢流程。
- [ ] 測試環境實際送出例如 `3015 退貨`，保留完整 `invoice_query` JSON，確認 AMEGO 是否可查回 `CancelReason`／作廢原因。
- [x] 若官方 query 不提供作廢原因，不為此另建永久本機 `void_audit`。

正式 Release 仍只在使用者於當次工作明確要求發布時執行；VERSION／BUILD、PR merge、工程測試包都不等於 Release 授權。

## 2. 酷澎樣本補齊

目前已完成已寄出 DeliveryList 的基本解析與開票流程；以下格式仍缺可靠實際樣本。沒有樣本前維持安全停止，不猜欄位／金額。

- [ ] 未出貨／不同狀態的實際樣本。
- [ ] 公司統編訂單樣本。
- [ ] 多商品訂單樣本。
- [ ] 數量大於 1 的樣本。
- [ ] 折扣／負數調整樣本。
- [ ] 取得樣本後補 parser／金額／防重與測試案例。

## 3. 折讓功能

尚未開始正式實作，需先確認光貿 API 與使用流程再排版本。

- [ ] `/json/g0401` 折讓開立。
- [ ] 折讓查詢／清單與本機資料模型。
- [ ] 折讓狀態同步。
- [ ] 折讓 PDF／列印需求。
- [ ] 作廢折讓流程。
- [ ] 發票與折讓間的防重、結果不明與同步安全規則。

## 4. V2.4.x／既有功能實機回歸清單

V2.4.2 已正式 Release。下列項目若沒有另外留下可追溯的實機驗證紀錄，後續維護時仍保留為回歸清單；**不要因已發布就反推為一定完成**。

- [ ] 以 V2.3.0 真實 `Data` 備份升級到現行 SQLite，確認 DB 建立、舊 JSON 保留、資料筆數與內容一致。
- [ ] 正式環境手動 recent 3-day sync，確認可抓到另一台電腦／光貿端的合法更新。
- [ ] 程式持續開啟超過 5 分鐘，確認啟動同步／排程同步不重疊，手動重新整理維持 30 秒冷卻。
- [ ] 雙擊不同日期的發票，確認每張都先 `invoice_query` 再顯示詳細資訊；query 無法確認時不得把舊 Cache 冒充最新資料。
- [ ] 「上傳問題」／同步問題視窗確認帳號隔離、解決、刪除、重新整理與 Failed 紀錄流程。
- [ ] 測試環境 namespaced OrderID 實機確認：不與共享測試池其他資料撞號，UI 顯示仍維持原始可讀 OrderID。

## 5. V2.5 之後

- [ ] 可選雲端 API 位址。
- [ ] Cloudflare Workers / D1 等免費雲端服務。
- [ ] A/B 機員工同步，本機員工資料作離線 Cache。
- [ ] Email 忘記密碼／驗證碼。
- [ ] Device Token／裝置授權。
- [ ] MO 密碼雲端同步。
- [ ] 多台電腦共用登入失敗計數。

AMEGO App Key 不列入上述雲端化範圍；V2.5 維持各電腦自行設定、Windows DPAPI 本機保護的既有做法。
