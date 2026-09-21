# CYInvoice 雲端版長期藍圖與上線路線

本文件整理 CYInvoice 從目前單機版進入多機雲端協調後的產品邊界、長期擴充方向、資料責任與分階段順序。

目前定案的核心策略是：**V3.0 先解決志遠高雄單一公司的多機協同，不提前把尚未發生的多公司／SaaS需求做進產品；但底層不得把未來擴充路堵死。**

## 1. 長期產品藍圖

### 1.1 CYInvoice V3.0：志遠高雄單公司、多機協同

V3.0 的實際目標只有一個：

> 讓志遠高雄目前同一家公司／同一統編下的多台 CYInvoice 電腦，可以共用中央員工、裝置、待辦、同步與稽核資料，並在 Cloud 暫時失效時仍可安全降回現有單機模式。

V3.0 不做：

- 多公司新增／刪除／切換 UI。
- `company_id` 全面導入。
- 跨公司員工權限。
- 跨公司待辦、查詢、報表或營運統計。
- 每家公司不同 AMEGO 設定的中央管理。

V3.0 可以在 Workspace 上保存目前公司的顯示名稱／統編等必要 metadata，但 **Workspace ID 不得直接等於統編**。

### 1.2 後續大版本：志遠台北／台中等多公司

未來志遠若加入台北、台中或其他不同統編公司，仍可共用同一個 Workspace，SUPER_ADMIN 也可維持同一位管理者。

屆時才正式插入 Company／Business Unit 層：

```text
Workspace：志遠
├─ Company：高雄
├─ Company：台北
└─ Company：台中
```

至少先達成「各公司內部多機協同」即可；是否提供跨公司切換、跨公司權限、跨公司待辦或統計，留到當時再依實際需求決定。

V3.x 舊資料因為保證只有單一公司，未來升級時可以自動建立第一個 Company，並把既有 Workspace 內的公司相關資料歸到該 Company，不需要人工逐筆判斷。

### 1.3 更長期：對外販售／開源

CYInvoice Windows Client 不應綁定特定雲端供應商或資料庫。

長期應維持：

```text
CYInvoice Windows Client
        │ HTTPS
        ▼
CYInvoice-compatible Cloud API
        │
        ├─ Cloudflare Worker + D1
        ├─ ASP.NET + PostgreSQL
        ├─ 自架服務
        └─ 其他相容實作
```

對外使用時，使用者是誰、使用哪一家 Cloud、同一 Workspace 內有幾家公司，皆不應成為 Windows Client 的硬編碼假設。CYInvoice 只需提供穩定、技術中立的 Cloud API Contract／Integration Guide。

Cloudflare Worker + D1 是目前專案的 reference implementation，不是 CYInvoice 的必要執行環境。

## 2. 必須固定的架構邊界

### 2.1 Workspace = 協作與管理範圍

Workspace 不是統編、不是 AMEGO 帳號、也不等於任何特定 Cloud tenant 技術名詞。

V3.0 可以暫時採「一個 Workspace 實際只服務一家公司」，但不得把「Workspace 永遠只能有一家公司」寫成不可拆解的永久假設。

### 2.2 Company = 未來的發票營業人範圍

V3.0 不需要正式 Company entity／`company_id`。

未來多公司版本才加入 Company 層，由 Company 承載：

- 統編。
- 公司顯示名稱。
- 公司自己的業務資料範圍。
- 公司自己的 AMEGO 身分／設定關聯。

因此 V3.0 不應把 `workspace_id` 設計成統編，或讓資料表／API 名稱暗示 Workspace 永遠等於公司。

### 2.3 Device = 電腦身分

每台 CYInvoice 電腦必須有獨立 Device ID／Device Token。

Device 與 Employee 是不同身分；一台電腦可以由不同員工操作，同一員工也可以在不同裝置上執行授權操作。

未來多公司時，是否讓 Device 綁預設 Company 屬於後續產品功能，不在 V3.0 寫死。

### 2.4 Employee = 人的身分

員工／管理員／SUPER_ADMIN 是人的權限身分，不是電腦身分。

長期應允許一個 Workspace 級 SUPER_ADMIN 管理多家公司；一般員工未來才視需求增加 Company scope。

V3.0 維持目前 per-operation authentication，不改成程式啟動即持續登入。

### 2.5 Cloud = Coordination Service，不是業務總開關

Cloud 主要提供：

- Workspace。
- Device。
- Employee／Role。
- Work Items。
- Audit。
- 同步狀態與必要的跨機協調／防重。

AMEGO 仍是發票、作廢、折讓官方結果的唯一準則；Cloud 不應變成第二套完整發票帳冊。

## 3. 目前 Cloudflare／D1 基礎的處理方式

目前已建立的 Worker、D1 與 migration 不重做、不清空。

既有 migration：

- `0001_cloud_foundation.sql`：`workspaces`、`devices`。
- `0002_device_pairing.sql`：Device Token／pairing 與目前 development DB 單一 Workspace 約束。

目前 development D1 已完成 schema migration 與 Windows → Cloud API → Worker → D1 的實機連線驗證，但尚未建立正式 Workspace 資料。

因此後續新增功能一律採向前 migration，例如 `0003_xxx.sql`、`0004_xxx.sql`；不得回頭重寫已執行的 `0001`／`0002`。

V3.0 不因未來可能的多公司需求新增 Company migration；等後續大版本真正需要多公司時，再新增對應 migration 並自動把既有單公司 Workspace 轉成第一個 Company。

## 4. 雲端資料責任

### 4.1 AMEGO 仍是官方資料來源

1. 光貿／AMEGO 是發票與折讓官方資料唯一準則。
2. 發票內容、作廢結果、折讓結果仍由 AMEGO API 即時或同步回查確認。
3. Cloud 不自行宣告 AMEGO 官方成功。
4. 不把完整發票 JSON、PDF 或所有 AMEGO 回覆無差別複製到 Cloud。

### 4.2 AMEGO App Key 不上雲

各 Windows 電腦維持本機設定與 Windows DPAPI 保護。Cloud 只保存 CYInvoice 自己需要的裝置、使用者與協調資料。

### 4.3 V3.0 雲端最低必要資料

至少需要：

#### Workspace

- `workspace_id`。
- 顯示名稱。
- 目前單一公司的必要 metadata（若實作需要）。
- 啟用狀態。
- 建立／更新時間。
- schema／API 相容版本。

#### Employee／Role

- 4 碼員工編號。
- 姓名。
- Email（若未來驗證流程需要）。
- `SUPER_ADMIN`／`ADMIN`／`EMPLOYEE`。
- enabled／disabled。
- 密碼驗證資料、lockout／rate-limit metadata。
- 建立／更新時間與版本。

#### Device

- `device_id`。
- `workspace_id`。
- 裝置顯示名稱。
- Device Token hash／metadata。
- paired／last seen／revoked 狀態。
- 客戶端版本。

#### Work Item

目前本機 `sync_issues`／invoice metadata 的人工工作未來集中到 Cloud：

- 紙本作廢人工確認。
- 發票作廢結果待確認。
- 折讓人工申請／官方確認。
- 折讓作廢人工申請。
- 管理員手動結案。
- 未來正式折讓 API pending／結果不明。

每筆只保存必要識別與狀態，不無差別複製完整發票明細。

#### Audit

記錄誰、何時、在哪台 Device 對哪個 Work Item 做了什麼，以及可安全保存的 AMEGO 結果摘要；不保存密碼、復原碼、App Key 或不必要的完整發票內容。

## 5. Cloud 故障與單機降級策略

V3.0 的產品原則改為：

> **Cloud 是協作服務，不是 CYInvoice 能不能工作的總開關。Cloud 暫時失效時，程式應原則上直接降回既有單機模式。**

主畫面可顯示：

`雲端異常(單機模式) / 光貿連線正常`

### 5.1 Cloud 掛掉仍可使用的既有業務

經目前單機版流程盤點，以下原則上不應因 Cloud 故障被整體鎖住：

- 發票查閱、同步、PDF、列印。
- 一般開立發票（仍須遵守既有 OrderID／結果不明保護）。
- 直接作廢：既有流程在送出前會以 `invoice_query` 查最新狀態，並保存本機 pending marker，結果不明不得盲目重送。
- 紙本證明聯未收回的作廢：現行本來就是管理員人工覆核流程。
- 現行人工折讓：CYInvoice 建立本機待辦，由管理員至光貿人工處理。
- 現行人工折讓作廢：同樣維持管理員人工處理。
- 管理員手動結案：只改 CYInvoice 自己的追蹤狀態，不代表 AMEGO 官方完成。

### 5.2 Cloud 掛掉時不能執行的 Cloud-only 功能

至少包括：

- 新 Device 註冊／配對／撤銷。
- 中央 Employee／Role／enabled 等權限資料變更。
- 只能由 Cloud 決定的 Workspace 管理。
- 未來若某功能真的依賴 Cloud 全域唯一編號或不可替代的跨機原子操作，該功能才個別禁止，不做全系統 blanket lock。

### 5.3 不採「所有敏感操作都必須先拿 Cloud Lock」

雲端協調仍可提供 idempotency／optimistic version／原子 state transition，但只使用在真正需要的跨機協調點。

不應為了 Cloud 架構，反而讓現有單機版本來安全可操作的流程在 Cloud 故障時全部停擺。

### 5.4 仍需處理的多機 OrderID 問題

目前本機自動訂單編號只依本機紀錄遞增，多台電腦離線時可能產生相同自動 OrderID。

V3.0 多機正式上線前必須定案一個不依賴即時 Cloud 的避撞方案，例如每台 Device 固定短碼／namespace。Cloud 故障時仍應能產生不與其他已註冊 Device 撞號的 OrderID。

### 5.5 Cloud 恢復後

Cloud 恢復時應：

1. 重新驗證 Device／Workspace。
2. 對本機在離線期間產生的可同步狀態做 reconciliation。
3. 以 AMEGO 官方 query 校正發票／作廢／折讓最終狀態。
4. 不因 Cloud 恢復而重送任何結果不明的 AMEGO 操作。

## 6. 權限驗證模式

第一版雲端維持現有習慣：

1. 一般開票不建立持續使用者 session。
2. 作廢、折讓、設定、帳號管理等需要權限時才要求員工編號＋密碼。
3. 雲端化後，中央 Employee／Role／enabled／lockout 是跨機權限真相。
4. Employee 與 Device 身分分離。
5. 不另外建立第二套「Cloud Admin」；沿用既有 CYInvoice SUPER_ADMIN 身分並逐步與 Cloud 綁定。

首次 Cloud Workspace／第一台 Device 的初始化可使用一次性的 server-side bootstrap guard；初始化碼不得寫入 repo、log、安裝包或明文持久化設定。

## 7. AMEGO API 與 Cloud 責任邊界

- AMEGO 發票開立／query／PDF：Windows Client 直接呼叫。
- Cloud：保存協作資料、中央權限、Work Item、Audit 與必要防重資訊。
- Windows 完成 AMEGO 呼叫後，只回報 Cloud 需要的安全摘要。
- 最終官方狀態仍以 AMEGO query 為準。

未來正式折讓 API `/json/g0401`、`/json/g0501` 也可維持 Windows 以本機 App Key 呼叫；Cloud 只負責必要的跨機協調與狀態管理。

## 8. 雲端 API 最低需求

V3.0 至少需要：

- Health／API／schema 相容性。
- Workspace bootstrap／status。
- Device register／pair／revoke。
- Employee list／create／update／disable。
- Operation authentication。
- Work Item create／read／transition／resolve。
- Audit append／query。
- Client sync checkpoint／version／reconciliation。

API Contract 必須技術中立。Windows Client 只連使用者設定的 CYInvoice-compatible HTTPS endpoint，不直接連 D1／SQL／其他資料庫。

## 9. 安全與營運最低要求

正式上線前至少需要：

- HTTPS only。
- Device Token 可撤銷。
- 密碼強 Hash、per-user salt、rate limit／lockout。
- server-side authorization，不能只信任 Windows UI。
- Workspace 資料隔離。
- schema migration 與向前／向下相容策略。
- staging／production 分離。
- Cloud health／error logging／基本告警。
- API 版本相容檢查。
- Cloud 資料服務層復原能力。

Cloud 保存的 Employee、Device、Work Item、Audit 等資料不是 AMEGO 能完整重建，因此 Cloud backend 仍需要自己的復原策略；這與單機版是否提供「使用者備份／還原」是不同問題。

## 10. 舊版資料轉雲端

建議採一次性啟用流程：

1. 既有 SUPER_ADMIN 建立 Cloud Workspace。
2. 同一流程建立／配對第一台可信任 Device，回傳專屬 Device Token 並由 Windows 加密保存。
3. 驗證第一台 Device 身分。
4. 後續再進入中央 Employee／SUPER_ADMIN 綁定與遷移階段。
5. 第二台裝置用短效 pairing code 加入，同一 Workspace 下每台 Device 都有獨立 Token。
6. 中央 Employee 成為跨機權限真相後，本機員工資料只保留必要離線 Cache／相容資料。

Workspace bootstrap、SUPER_ADMIN 雲端化、第二台 Device pairing 應分階段完成，避免一次改動過大。

## 11. V3.0 建議實作順序

### Phase 1：Cloud Foundation 收斂

- Provider-neutral Cloud Client／API Contract。
- Local Only／Cloud Enabled 模式。
- 使用者自行設定 HTTPS endpoint。
- Health／API／schema／storage 相容檢查。
- Worker + D1 reference backend。
- migration／CI／engineering validation。
- Cloud 異常時正確顯示並降回單機執行狀態。

### Phase 2：Workspace + 第一台 Device

- 建立第一個 Workspace。
- 同一初始化流程建立第一台 trusted Device。
- 一次性 bootstrap guard。
- Device Token 只回傳一次並由 Windows 安全保存。
- `GET /v1/device` 或等效 API 再驗證裝置身分。
- timeout／結果不明時先重新查 onboarding 狀態，禁止盲目重建 Workspace。

### Phase 3：中央 Employee／SUPER_ADMIN

- 沿用既有 CYInvoice Employee／SUPER_ADMIN 身分，不建立平行帳號系統。
- 中央 role／enabled／lockout。
- 既有本機員工遷移策略。
- per-operation authentication。
- 第二台 Device pairing／revoke。

### Phase 4：跨機 Work Item 與離線 reconciliation

- 作廢／折讓／折讓作廢／管理員結案 Work Item 上雲。
- 原子 state transition／optimistic version。
- 只在必要位置使用 idempotency／跨機協調。
- Cloud 失效時沿用單機流程；恢復後安全 reconciliation。
- 多機自動 OrderID 避撞方案。

### Phase 5：Cloud Audit

- append-only 稽核。
- Employee／Device／Work Item 關聯。
- 管理員查詢。

### Phase 6：正式折讓 API

- `/json/g0401`。
- `/json/g0501`。
- AllowanceNumber 唯一性策略。
- 官方回查／pending／結果不明 state machine。

## 12. 後續大版本：多公司

真正有台北／台中等不同統編需求時再做：

- `companies`／Company entity。
- 既有 V3.x 單公司 Workspace 自動轉成第一個 Company。
- 公司範圍的 Work Item／Audit／同步資料。
- Employee ↔ Company 權限（若產品需要）。
- Device 預設 Company（若產品需要）。
- 公司切換 UI（若產品需要）。
- 多公司 Workspace 舊版相容策略。

向下相容原則可採：

- Workspace 仍只有一家公司時，V3.x Client 可由 Server 自動套用唯一公司。
- Workspace 已啟用兩家公司以上時，舊 Client 不得自行猜測，應要求升級到支援多公司的版本。

## 13. 對外相容／開源階段

功能與 Contract 穩定後，再提供技術中立的 Cloud Integration Guide，定義：

- endpoint。
- request／response schema。
- Device authentication。
- Employee authorization。
- error code。
- API／schema version negotiation。
- reconciliation／idempotency 必要語意。

Guide 不規定第三方使用 Cloudflare、D1、AWS、Azure、SQL Server、PostgreSQL 或其他技術；第三方只要提供相容服務即可。

## 14. 目前已定案、不再阻塞 V3.0 的事項

1. V3.0 只以志遠高雄單公司多機協同為目標。
2. V3.0 不先導入 `company_id` 或完整多公司功能。
3. Workspace 是協作範圍，不永久等同統編／公司。
4. 未來志遠多公司可共用同一 Workspace；跨公司功能到時再決定。
5. Cloudflare Worker + D1 只是目前 reference implementation。
6. 既有 `0001`／`0002` migration 保留，後續一律新增 migration。
7. Cloud 掛掉時原則上降回單機模式，不採全系統 blanket lock。
8. AMEGO App Key 仍只在 Windows 本機安全保存。
9. Cloud 不取代 AMEGO 官方發票／折讓資料來源。
10. 對外販售／開源時，以 CYInvoice-compatible Cloud API 作為唯一必要雲端介面。
