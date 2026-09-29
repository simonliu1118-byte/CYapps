# CYInvoice 雲端版長期藍圖與上線路線

本文件整理 CYInvoice 從目前單機版進入多機雲端協調後的產品邊界、長期擴充方向、資料責任與分階段順序。若由新的長時間工作階段／ChatGPT Work 接手目前 Identity 階段，先依 `AGENTS.md` 讀永久規則，再讀 `CLOUD_WORK_HANDOFF.md`。

目前定案的核心策略是：**V3.0 先解決志遠高雄單一公司的多機協同，不提前把尚未發生的多公司／SaaS需求做進產品；但底層不得把未來擴充路堵死。**

Workspace／Device／Device Token／SUPER_ADMIN／Email Recovery 的完整定案生命週期見 `CLOUD_IDENTITY_LIFECYCLE.md`；本文件保留長期架構與實作順序。

## 1. 長期產品藍圖

### 1.1 CYInvoice V3.0：志遠高雄單公司、多機協同

V3.0 的實際目標只有一個：

> 讓志遠高雄目前同一家公司／同一統編下的多台 CYInvoice 電腦，可以共用中央員工、裝置、待辦、同步與稽核資料，並在 Cloud 暫時失效時仍可安全使用最後成功同步的中央 Employee 快取與既有本機業務能力。

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

Workspace 建立後不因 Device 遺失、Windows 重灌或 Token 遺失而重新建立。

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

`device_id` 由 Cloud 建立；Device Token 由 Windows 先以安全亂數產生並 protected storage 保存，Cloud 只保存 Token hash。

未來多公司時，是否讓 Device 綁預設 Company 屬於後續產品功能，不在 V3.0 寫死。

### 2.4 Employee = 人的身分

員工／管理員／SUPER_ADMIN 是人的權限身分，不是電腦身分。

V3.0 的 Workspace **永遠只允許一名 SUPER_ADMIN**；ADMIN 可多人。若要更換 SUPER_ADMIN，只能由現任 SUPER_ADMIN 對指定 ADMIN 執行原子的「移交超管權限」，同一操作中原超管降為 ADMIN、新超管升為 SUPER_ADMIN。

長期應允許一個 Workspace 級 SUPER_ADMIN 管理多家公司；一般員工未來才視需求增加 Company scope。

V3.0 維持目前 per-operation authentication，不改成程式啟動即持續登入。

### 2.5 Employee authority：Local 與 Cloud 不做雙主

單機模式只有 Local EmployeeStore。

轉 Cloud 時先完成 whole-device Employee Transition；只有全部 identity／Email／credential／conflict 都整理完成後才 cutover。

Cutover 後：

```text
Cloud Employee = 唯一帳號 authority
Windows Local DB = 同步 cache + protected offline credential verifier
```

Cloud Mode 斷網時仍是 Cloud Mode Offline，不會復活舊 Local role／credential authority。這避免長期維持兩套帳號、兩套 role 與 Pending Y 暫時權限特例。

### 2.6 Cloud = Coordination Service，不是業務總開關

Cloud 主要提供：

- Workspace。
- Device。
- Employee／Role。
- Work Items。
- Audit。
- 同步狀態與必要的跨機協調／防重。

AMEGO 仍是發票、作廢、折讓官方結果的唯一準則；Cloud 不應變成第二套完整發票帳冊。

## 3. Whole-device Employee Transition

Device 建立／加入 Workspace 後先進入 `CloudTransition`，一次盤點全部既有 Local Employees。

Identity matching 使用 Employee No + Email；Name 只作顯示：

- Employee No + Email 都不存在中央資料 → 新 Cloud Employee；先驗證該 Employee 自己的 Email。
- Employee No + Email 都命中同一中央 Employee → 直接採用既有 Cloud Employee，不做 merge，保留中央 role。
- Employee No only／Email only／兩欄分別命中不同 Employee → pending conflict，由目前 Workspace SUPER_ADMIN 明確確認。

第一台 X 成為唯一 Cloud SUPER_ADMIN；bootstrap 已驗證 Recovery Email 直接作為 X 的 verified Email。既有 Workspace 新機的 Local SUPER_ADMIN Y 若是全新中央 Employee，Cloud role 預設 ADMIN；若精確命中既有 Employee，採既有中央 role。

全部 transition item ready 後才做一次性 cutover。

## 4. 離線與恢復

Cloud Mode 斷網時：

- 使用最後一次成功同步的 Cloud Employee identity / role / enabled / credential verifier。
- 需要權限的操作仍在執行當下輸入 Employee No + Password。
- 不切回舊 Local EmployeeStore。

完全離線期間不可能知道 Cloud 上剛發生的 role／enabled／password 異動，因此只能使用最後已知 cache；恢復連線後由 Cloud authority 更新 cache。

Workspace-wide 帳號異動全部 Online-only：

- 新增 Employee。
- Email / role / enabled / password。
- identity conflict resolution。
- SUPER_ADMIN transfer。
- Device / Workspace 管理。

如此避免產生 A、B 兩台離線各自改同一 Employee 後再嘗試 merge 的雙主模型。

## 5. 中央 Employee 管理

Cloud Employee account management 採 execution-time credential verification。

目前 V3 contract：

- 新 Employee：管理員帳密 re-auth + 新 Employee 自己的 Email OTP。
- 修改 Email：管理員 re-auth + 新 Email OTP，成功前舊 Email 不變。
- role：`ADMIN ↔ EMPLOYEE`，不可自改 role。
- enabled：不可自停用；SUPER_ADMIN 不可停用。
- password：本人可改自己；管理員可重設其他非 SUPER_ADMIN；SUPER_ADMIN 密碼只能本人改。
- `SUPER_ADMIN` 不得由一般 role update 建立或移除。

密碼明文不上 Cloud；Windows 先產生 PBKDF2 verifier，再經 HTTPS 更新中央 credential。

## 6. SUPER_ADMIN Transfer

SUPER_ADMIN 更換只走 dedicated transfer：

```text
X execution-time password re-auth
  ↓
OTP to X verified Email
  ↓
backend recheck X / Y
  ↓ atomic
X → ADMIN
Y → SUPER_ADMIN
Recovery Email → Y verified Email
```

Y 必須是 enabled ADMIN 且 Email 已驗證。

## 7. Cloud 最低必要資料

### Workspace

- workspace_id。
- display name。
- status。
- recovery Email / verification state。
- Employee revision / compatibility metadata。

### Employee

- stable employee_id。
- 4 碼 Employee No。
- name。
- verified Email。
- role。
- enabled。
- credential verifier / version。
- revision / timestamps。

### Device

- device_id / workspace_id。
- display name。
- Device Token hash。
- paired / revoked / last-seen metadata。
- client version。
- Employee authority transition state。

### Work Item / Audit

後續集中現有人工工作與跨機協調時，只保存必要識別、狀態與 audit metadata，不無差別複製完整發票 JSON / PDF / AMEGO payload。

## 8. AMEGO 資料責任

AMEGO 仍是發票／作廢／折讓官方結果唯一準則。

Cloud：

- 不自行宣告 AMEGO 官方成功。
- 不保存 AMEGO App Key。
- 不把完整 invoice / PDF cache 無差別鏡像到中央 DB。
- 可以保存跨機必要的 work item、idempotency、結果不明、audit metadata。

## 9. V3 後續實作順序

### Phase A — Identity foundation

目前已進入工程完成／驗證階段：

- Workspace bootstrap。
- Device identity / protected pending token。
- Pairing / Device Join。
- whole-device Employee Transition。
- central Employee authority + offline cache。
- conflict resolution。
- central Employee CRUD。
- SUPER_ADMIN transfer。
- Schema 7 / API 1 compatibility。

剩餘：remote development deployment、D1 migration、live Email OTP、多機 Windows 實測、Device revoke / recovery。

### Phase B — 多機資料同步

- 既有 invoice list / query 模型接入跨機協調。
- recent 3-day sync / daily full sync 維持既定安全規則。
- optimistic revision / idempotency / pending work item。
- 不明結果不得盲目重送。

### Phase C — Work Item / Audit

集中：

- 作廢覆核。
- pending / failed / unknown。
- 人工折讓／折讓作廢過渡工作。
- 管理員結案。
- audit actor + Device + timestamp + safe result summary。

### Phase D — 正式折讓 API

Cloud coordination 成熟後再完成 `/json/g0401` / `/json/g0501` 與 allowance number global uniqueness；目前仍依已定案的 interim `invoice_query`／人工流程。

### Phase E — 未來 Company

只有真正出現多統編公司需求時才新增 Company migration / UI / scope。

## 10. 上線前必要驗證

- Remote Worker / D1 Schema 7 實際狀態。
- Live Brevo Email OTP。
- A/B Windows whole-device transition 與 exact/new/conflict identity matrix。
- Central account CRUD / snapshot sync。
- Cloud Mode Offline execution-time auth / reconnect refresh。
- SUPER_ADMIN transfer。
- Device revoke / recovery。
- Multi-device business sync / Work Item 安全行為。

## 11. 後續大版本：多公司 Workspace

此區只保留擴充點，不列入 V3.0 工程範圍。

- 真正有志遠台北／台中等不同統編需求時，再新增 `companies`／Company entity 與 `company_id`。
- V3.x 單公司 Workspace 升級時，自動建立第一個 Company，既有公司相關資料全部歸到該 Company。
- 視實際需求再做 Employee ↔ Company 權限、Device 預設 Company、公司切換 UI、跨公司待辦／查詢／報表。

## 12. 對外雲端相容／開源準備

Cloud 功能與 API Contract 穩定後，撰寫技術中立的 **Cloud Integration Guide**。

Guide 只定義 endpoint、request／response schema、Device authentication、Employee authorization、錯誤碼、版本相容、reconciliation／idempotency 必要語意；不規定第三方使用 Cloudflare、D1、AWS、Azure、SQL Server、PostgreSQL 或其他技術。

## 13. 後續增強

- 一般員工／管理員忘記密碼的 Email self-service。
- MO 密碼安全雲端同步。
- 小型營運摘要：今日／本月開票張數與金額、待處理工作數、同步異常數。

AMEGO App Key 不列入雲端同步範圍，仍維持各電腦自行設定、Windows protected storage 本機保護。

正式 merge、tag、Release 仍需使用者明確授權。