# CYInvoice 雲端版上線需求盤點

本文件整理 CYInvoice 從目前單機／雙機工具進入雲端協調架構前，正式上線需要具備的功能、資料責任與分階段順序。

## 1. 核心原則

1. **光貿／AMEGO 仍是發票與折讓官方資料唯一準則。** 雲端資料庫不另建立一份完整發票帳冊來取代光貿。
2. CYInvoice 雲端主要處理：員工／權限、裝置、跨機待辦、跨機防重、操作稽核與未來折讓流程協調。
3. 發票內容、作廢結果、折讓結果仍應由 AMEGO API 即時或同步回查確認；雲端不得自行宣告官方成功。
4. **AMEGO App Key 不上雲。** 各 Windows 電腦維持本機設定與 Windows DPAPI 保護；雲端只保存 CYInvoice 自己需要的憑證。
5. 現行產品不採持續登入。一般操作維持目前模式；需要員工／管理員權限的動作才進行身分驗證。
6. 單機版不新增本機操作稽核、備份／還原、發票匯出與管理員開機提醒；相關跨機價值功能等雲端資料庫建立後再做。

## 2. 雲端上線最低必要資料

### 2.1 公司／Workspace

至少需要：

- `organization_id`
- 公司顯示名稱
- 啟用狀態
- 建立／更新時間
- 雲端 schema／資料版本

不需要保存 AMEGO App Key。公司統編若為跨機識別必要，可保存；但不能因此把雲端資料庫當成正式發票資料來源。

### 2.2 員工與權限

將目前各機本機 `employees` 收斂為中央資料：

- 4 碼員工編號
- 姓名
- Email
- 角色：`SUPER_ADMIN`／`ADMIN`／`EMPLOYEE`
- 啟用／停用
- 密碼 Hash 與 salt／參數
- 登入失敗計數與鎖定狀態
- 復原相關 metadata
- 建立／更新時間與版本號

雲端化後，角色與停用狀態以雲端為準；本機只保留必要離線 Cache，不可讓 A、B 機長期各自形成兩份權限真相。

### 2.3 裝置

每台 CYInvoice 電腦需要獨立裝置身分：

- `device_id`
- Workspace／公司
- 裝置顯示名稱
- Device Token 或等效長期憑證的 Hash／metadata
- 首次配對時間、最後連線時間
- 啟用／撤銷狀態
- 客戶端版本

裝置憑證與員工密碼是兩件事。裝置憑證讓程式可以安全連到 CYInvoice Cloud；員工密碼仍只在需要權限的動作時驗證使用者。

### 2.4 跨機待辦

目前只存在本機 `sync_issues`／invoice metadata 的人工作業，雲端版要集中：

- 紙本作廢人工確認
- 發票作廢結果待確認
- 折讓人工申請／官方確認
- 折讓作廢人工申請
- 管理員手動結案
- 之後正式折讓 API 的 pending／結果不明

每筆至少要有：

- `work_item_id`
- operation type
- 發票號碼／OrderID／折讓單號等最小必要識別
- requester employee
- reviewer／resolver employee（若有）
- reason
- state
- created／updated／resolved timestamps
- optimistic version
- last official check result summary

不需要把整張發票品項複製進雲端，除非未來有明確功能需要。

### 2.5 跨機防重與操作鎖

A／B 機同時操作時，雲端必須能阻止：

- 同一待辦被兩台同時處理
- 同一發票重複送出相同敏感操作
- 同一折讓申請建立兩次
- 未來 AllowanceNumber 跨裝置撞號

至少需要：

- idempotency key
- operation lock／lease 或同等原子機制
- compare-and-set／version check
- timeout 後的結果不明狀態

任何遠端 timeout 都不能被解讀成「沒送到」而直接重送。

## 3. 權限驗證模式

第一版雲端不必把 CYInvoice 改成「開程式就登入」。建議維持目前習慣：

1. 一般開票不建立持續使用者 session。
2. 作廢、折讓、設定、帳號管理等需要權限時才要求員工編號＋密碼。
3. 桌面程式將驗證請求送到 CYInvoice Cloud。
4. Cloud 驗證角色／啟用狀態／鎖定狀態後，回傳短效、限用途的 operation authorization。
5. 桌面程式完成該次操作後即失效，不把管理員身分當成整個程式生命週期登入狀態。

因此目前不做「管理員打開程式就顯示待辦提醒」是正確的；沒有持續登入，就不能可靠判定目前操作者是不是管理員。

## 4. 雲端操作稽核

等雲端資料庫建立後再做稽核紀錄。建議記錄：

- 誰在什麼時間提出作廢／折讓／折讓作廢
- 哪位管理員批准、取消、退回或手動結案
- 帳號建立、停用、角色變更與密碼重設
- 哪台裝置執行
- AMEGO 操作結果：已確認／pending／結果不明／失敗
- idempotency key／work item ID

不記：

- 員工密碼
- 復原碼明文
- AMEGO App Key
- 不必要的完整發票內容

稽核紀錄是 CYInvoice 自己的跨機操作證據，不取代 AMEGO 官方發票紀錄。

## 5. AMEGO API 與雲端責任邊界

由於 App Key 維持本機，第一階段建議：

- AMEGO 發票開立／query／PDF：仍由 Windows 客戶端直接呼叫。
- Cloud：在敏感操作前負責權限、work item、鎖與 idempotency。
- 客戶端完成 AMEGO 呼叫後，將「可安全保存的結果摘要」回報 Cloud。
- 最終官方狀態仍以之後的 AMEGO query 為準。

未來正式折讓 API：

- `/json/g0401` 折讓開立
- `/json/g0501` 折讓作廢

可以仍由 Windows 客戶端以本機 App Key 呼叫；Cloud 負責跨機唯一性、管理員授權、AllowanceNumber 產生與 pending 狀態機。除非未來另行決定，沒有必要為了雲端化而把 AMEGO App Key 集中上傳到伺服器。

## 6. 雲端 API 最低需求

實際路徑可在實作時再定，但功能面至少需要：

- Health／版本相容性
- Device register／pair／revoke
- Employee list／create／update／disable
- Operation authentication
- Work-item create／read／transition／resolve
- Operation lock／idempotency
- Audit append／query（Cloud DB 上線後）
- 客戶端同步 checkpoint／version

所有寫入 API 都要有：

- authenticated device
- 明確 authorization
- request id／idempotency key
- 伺服器端時間
- 可重試但不重複執行的語意

## 7. 離線與故障策略（上線前必須定案）

雲端版最重要的產品決策之一是 Cloud 暫時無法連線時哪些功能仍可使用。

建議基線：

- 已快取發票查閱：可用。
- 一般不涉及跨機權限／防重的本機功能：可評估保留。
- 員工／管理員權限變更：Cloud 不可用時禁止。
- 會建立跨機待辦的作廢／折讓／折讓作廢：Cloud 不可用時先停止，不自行另建一套離線真相。
- 需要跨機唯一編號或 lock 的正式折讓 API：Cloud 不可用時禁止送出。

若之後確實需要離線寫入，再另外設計 outbox／reconciliation；第一版不應在沒有完整衝突模型下先做。

## 8. 安全與營運最低要求

正式上線前至少要有：

- HTTPS only
- Device Token 可撤銷
- 密碼強 Hash、per-user salt、登入 rate limit／lockout
- server-side authorization，不能只信任桌面 UI
- Workspace 資料隔離
- schema migration 可回滾／向前相容策略
- staging 與 production 分離
- Cloud health／error logging／基本告警
- API 版本相容檢查
- 雲端資料庫的服務層復原機制

最後一項不是「CYInvoice 單機備份功能」。本機發票 Cache 不需做使用者備份／還原；但 Cloud 將來保存的員工、裝置、待辦、稽核與防重資料不是 AMEGO 能重建，因此雲端服務本身仍需要可復原能力。

## 9. 舊版資料轉雲端

不能在更新程式後直接把兩台電腦的員工表互相覆蓋。建議採一次性啟用流程：

1. 由既有超級管理員建立 Cloud Workspace。
2. 配對第一台可信任裝置。
3. 顯示本機員工清單，明確確認要匯入哪些帳號。
4. Cloud 建立唯一員工資料。
5. 第二台裝置配對後下載中央員工資料，不再把第二份本機資料當權威。
6. 完成後本機員工表降為離線 Cache／遷移相容資料。

密碼 Hash 是否能直接遷移，要依當時 Cloud 密碼模型決定；若不能安全沿用，就讓使用者在雲端啟用時重設，而不是傳送或還原明文密碼。

## 10. 建議上線階段

### Phase 1：Cloud Foundation

- Workspace
- Device pairing／token／revoke
- Health／API version
- D1 或最終選定資料庫 schema + migration
- staging／production
- 基本監控與服務層復原

### Phase 2：中央員工與權限

- 員工同步
- per-operation authentication
- role／enabled／lockout 中央化
- 本機員工資料遷移
- 裝置與使用者權限邊界

### Phase 3：跨機工作中心

- 作廢／折讓／折讓作廢 work items 上雲
- A／B 機即時或短週期同步
- 原子 state transition
- idempotency／operation lock
- 管理員手動結案同步

### Phase 4：雲端稽核

- 操作稽核 append-only 紀錄
- 管理員查詢
- 裝置／使用者／work item 關聯

### Phase 5：正式折讓 API

- 全域唯一 AllowanceNumber
- `/json/g0401`
- `/json/g0501`
- 官方結果回查與 pending state machine
- 多裝置防重

### Phase 6：非必要增強

- Email 復原／驗證碼
- MO店+ 密碼安全同步
- 小型營運摘要
- 更完整雲端診斷／維運工具

## 11. 上線前仍需使用者定案

開始 Cloud Foundation 前，至少再確認：

1. 第一版雲端供應商／部署區域與預算上限。
2. A／B 機是否都必須能在 Cloud 離線時繼續開一般發票。
3. Cloud 無法連線時，作廢／折讓是否接受「一律暫停」的安全策略。
4. 員工資料第一次上雲時，以哪一台現有 CYInvoice 為第一份基準。
5. 操作稽核保留多久、哪些角色可以查看。
6. Email 復原是否要納入第一版，或繼續使用超級管理員離線復原碼。
7. 正式折讓 API 要與 Cloud 首版一起上線，還是等中央員工／待辦穩定後再接。

以上定案後，才適合凍結 Cloud schema 與 API contract；在此之前不應先把單機 SQLite schema 直接照搬到雲端。
