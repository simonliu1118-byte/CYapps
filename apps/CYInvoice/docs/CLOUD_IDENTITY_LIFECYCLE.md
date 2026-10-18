# CYInvoice V3.0 Workspace／Device／Employee 身分生命週期定案

更新日期：2026-10-13（原設計基準：2026-09-29）

現行 Windows 正式版 **V3.0.0 Build 0** 已發布（V2.6.19 為歷史）；CYID Consumer source 與原 development 啟用有既有驗證，但實機／Workspace 全矩陣及 0-Device recovery 仍依 [現行交接](V3_1_HANDOFF.md) 和唯一 [TODO.md](TODO.md) 的未完成項目追蹤。CYID 與 CYInvoice 不是同一個 D1；本文件 §19 保留早期 credential-only 提案，已由 §20 完整退役決策取代；2026-10-13 本公司原 D1 已完成 CYID_ONLY 維護、私有備份、本機還原與保留資料／FK 核對。0-Device recovery 仍未實作。具體 runtime／offline 邊界見 CY_ID_INTEGRATION §14。本文件的 CYID 設計依 [canonical shared standard](../../CYCloudIdentity/docs/CONSUMER_INTEGRATION_STANDARD.md) 適配 desktop／per-operation transport，不建立平行 shared contract。

本文件記錄 CYInvoice V3.0 在 Workspace、Device、Device Token、Employee authority、Local／Cloud、離線、Recovery 與最高管理權上的定案行為。

CY ID 整合的專門邊界另見 `CY_ID_INTEGRATION.md`。本文件是設計／需求基準，不取代 `PROJECT_RULES.md`；治理衝突時仍依 repository／project governance 優先順序處理。

## 1. 核心不變量

1. CYInvoice Cloud Workspace 是 CYInvoice 自己的 Device／同步／Work Item／業務協同範圍；即使採用 CY ID，也不與 CY ID Workspace 合併成同一個資料實體。
2. Cloud Workspace 建立後長期存在；Windows 重灌、程式重新下載、Device Token 遺失都不得因此重建 Workspace。
3. 權限 role 正式統一為 `SUPER_ADMIN`、`ADMIN`、`USER`；不再使用 `EMPLOYEE` 作為 role 名稱。
4. Workspace 恰好一名 `SUPER_ADMIN`；`ADMIN`、`USER` 可多人。
5. Device 與 Employee 是不同身分；Device Token 只能證明可信任電腦，不能單獨證明操作者是管理員。
6. CYInvoice 不採持續 Employee login。需要權限的操作一律在執行當下驗證 Employee No + Password／對應 Identity authority，並依當下有效 role 授權。
7. 每個執行狀態只能有一套 Employee authority，不允許 Local、Built-in Cloud、CY ID 同時成為帳號真相來源。
8. Cloud Mode 斷網仍是 Cloud Mode Offline，不得復活舊 Local EmployeeStore 作第二套 authority。
9. Cloud Offline 本機只保留可信的 Employee cache + protected offline credential material；Online 恢復後最新 authority 必須重新生效。
10. Cloud Employee 全域異動為 Online-only；不支援離線帳號修改後再 conflict merge。
11. Device Token 不寄 Email、不寫 log、不存 Cloud 明文；Windows protected storage 保存，Cloud 只存 hash。
12. Identity matching 不使用姓名猜測；只使用 Employee No + Email 等明確 identity 規則。
13. Cloud 是協作／身分／裝置協調層，不是發票業務 kill switch；AMEGO 仍是發票／作廢／折讓官方真相。
14. 已完成 Cloud cutover 的電腦若改回單機版，視為本機恢復首次使用狀態：必須雙重確認本機資料將全部清除，並安全退出目前 Device identity 後重新建立 Local SUPER_ADMIN。
15. 一個 active Workspace 必須至少存在一條可驗證的管理恢復路徑；這條路徑不必永遠等同於 active Device。Built-in Cloud 目前仍以可信任 Device + 中央 SUPER_ADMIN 為恢復路徑，因此最後一台 active Device 不可直接撤銷；未來 CY ID 模式若已有獨立的 CY ID SUPER_ADMIN recovery contract，則可合法存在 0 active Device。
16. 刪除本機資料、撤銷 Device、停用 Workspace、永久刪除 Workspace 是四個不同 lifecycle action，不得以 mode flag 或單一 destructive API 混為同一件事。

## 2. 三種正式使用模式

### 2.1 Local

```text
CYInvoice Local
└─ Local EmployeeStore
```

- 不需要 Cloud Workspace。
- 不需要 CY ID。
- Local EmployeeStore 是唯一 Employee authority。
- 帳號管理由 CYInvoice 本機負責。

### 2.2 Built-in Cloud / Self-hosted

```text
CYInvoice Workspace
├─ Built-in Employee authority
├─ SUPER_ADMIN / ADMIN / USER
├─ Credential / Email OTP
├─ Device / Device Token
└─ CYInvoice business coordination
```

這是 CYInvoice 可獨立部署的正式模式。第三方公司可依 User Manual 建立自己的 Worker／Database／Email Provider／Workspace，不需要 CY ID。

Built-in Cloud 不是 fallback；CY ID 整合不得移除這套能力。

### 2.3 CY ID Cloud

```text
CY ID Workspace
└─ Employee / Credential / 共通身分
        │
        │ identity binding
        ▼
CYInvoice Workspace
├─ Device / Device Token
├─ Pairing / Invitation / Revoke
├─ Invoice Sync
├─ Work Item
└─ CYInvoice business state
```

CY ID 是 Employee／Credential authority；CYInvoice Workspace、Device 與業務協同仍由 CYInvoice 管理。

CY ID 模式的細節另見 `CY_ID_INTEGRATION.md`。

## 3. Local Mode execution-time authentication

未加入 Cloud 或尚未完成 Cloud cutover 時：

```text
執行敏感功能
  ↓
當下輸入員編 + 密碼
  ↓
Local EmployeeStore 驗證
  ↓
依 Local role 執行此次操作
```

程式啟動本身不登入任何 Employee。

## 4. Built-in Cloud：第一個 Workspace / 第一台 Device

第一台 A 機：

```text
Local SUPER_ADMIN X 當下驗證
  ↓
沿用 X 既有 Email
  ↓
Email OTP
  ↓
Windows 先產生並安全保存 Pending Device Token
  ↓
Cloud 建立 Workspace + Device
  ↓
GET /v1/device 核對
  ↓
Device identity 正式成立
  ↓
進入 whole-device Employee Transition
```

Workspace ID、Device ID 由 Cloud 產生；Device Token 由 Windows 產生。

X bootstrap 時已完成的 Email OTP，可直接作為後續中央 X 的 Email verified evidence，不重複要求相同 Email OTP。

## 5. Built-in Cloud：Existing Workspace / B Device Join

B 機加入：

```text
使用者選擇加入既有 Workspace
  ↓
B 機當下驗證 Local ADMIN / SUPER_ADMIN
  ↓
才允許輸入 Pairing Code / 現行核准加入憑證
  ↓
Windows 先產生並安全保存 Pending Device Token
  ↓
Cloud 建立 Device ID，只存 Token hash
  ↓
GET /v1/device 核對
  ↓
B Device 加入完成
  ↓
進入 whole-device Employee Transition
```

Local 管理員驗證只證明操作者有權管理 B 機；Pairing／Invitation 另外證明 Workspace 授權此 Device 加入。兩者不可互相取代。

Device 加入憑證授權 Device，不授予 Employee role。

## 6. Built-in Cloud：Whole-device Employee Transition

Device 加入後先進入：

```text
CloudTransition
```

正式 cutover 前既有 Local EmployeeStore 仍是唯一帳號 authority；不建立 Local/Cloud 雙 authority 特例。

一次盤點全部既有 Local Employees。

### 6.1 Identity matching matrix

| Employee No | Email | 判定 | 行為 |
| --- | --- | --- | --- |
| 未命中 | 未命中 | 新 Employee | 驗證本人 Email，建立中央 Employee |
| 命中 A | 命中同一 A | 同一 Employee | 直接採用 A 的 Cloud 資料 |
| 命中 A | 未命中 | 衝突 | 交目前 SUPER_ADMIN 人工確認 |
| 未命中 | 命中 A | 衝突 | 交目前 SUPER_ADMIN 人工確認 |
| 命中 A | 命中 B | 衝突 | 交目前 SUPER_ADMIN 人工確認 |

姓名只作 UI 顯示，不作身分比對依據。

Employee No + Email 都命中同一中央 Employee 時，不做 merge；確認同一人後直接改採 Cloud Employee 的 name / Email / role / enabled / credential authority。

### 6.2 新 Employee role

第一台 A 機的 X：

```text
Local SUPER_ADMIN X → Cloud SUPER_ADMIN
```

既有 Workspace 的 B 機若 Local SUPER_ADMIN Y 是真正的新中央 Employee：

```text
Local SUPER_ADMIN Y → Cloud ADMIN
```

若 Y 精確命中既有中央 Employee，直接採用既有 Cloud role，不強制改成 ADMIN。

一般帳號正式 role 使用 `USER`。

### 6.3 Conflict

任何 partial / divergent match 都只建立 pending identity case，不建立重複中央 Employee、不猜測、不自動改 role。

真正 Resolve 時，必須在當下重新驗證目前 Workspace `SUPER_ADMIN`，backend 再檢查該 Employee 此刻仍是唯一 SUPER_ADMIN。

## 7. Built-in Cloud cutover

只有當本機全部既有 Employee 都完成 identity resolution、必要 Email verification、credential preparation 與 conflict resolution，才允許 cutover。

```text
Local authority
  ↓ atomic logical cutover
Built-in Cloud Employee authority
```

Cutover 後：

- Built-in Cloud Employee 是唯一帳號主資料。
- Local EmployeeStore 不再作權限 authority。
- 本機 Cloud Employee cache 只供顯示、離線驗證與安全 fallback。
- Cloud 更新成功後，各 Device 重新抓 Employee snapshot 更新 cache。
- 切回單機版不是復活舊 Local authority，而是執行第 10 節的破壞性本機重置。

## 8. Cloud execution-time authentication

Cloud Mode 沒有 persistent Employee login。

### Built-in Cloud

```text
按下敏感操作
  ↓
輸入 Employee No + Password
  ↓
Online：Built-in Cloud authority 驗證 credential + current role
Offline：最後可信 protected Cloud cache 驗證
  ↓
只授權此次操作
```

Cloud 可連線時，任何 Employee 密碼／權限驗證必須使用當下最新 authority，不得只相信等待背景同步的舊本機 cache。新建 Employee、停用、role 或 password 變更在其他 Device 的下一次權限驗證即應生效；成功取得最新 snapshot 時同步刷新本機 cache。

### CY ID Cloud

Online 時以 CY ID 回傳的目前有效 Employee identity、enabled、CYInvoice access 與 role 為 authority。CYInvoice 不維護第二套中央 credential authority。

V2.6.15 已實作 CYID device-bound DPAPI offline cache，V2.6.16 在雲端 transport／timeout／暫時 5xx 且光貿正常時沿用原降級；帳密／權限／Device 拒絕不轉降級，reconnect 更新最新權限。Last-trusted 無新增 TTL 與 Session loss 的限制見 CY_ID_INTEGRATION §14；不讀 CYID D1、不下載中央 verifier，不形成第二套 authority。

## 9. Cloud Mode Offline

斷網後仍是：

```text
Cloud Mode / Offline
```

不是 Local Mode。

雲端暫時不可用（含 503）且光貿正常時，沿用原降級所使用的最後可信 cache：

- Employee identity；
- role；
- enabled；
- credential version / protected verifier 或 CY ID 模式等效的受保護 offline material。

雲端不可達期間無法知道中央剛發生的 role、enabled 或 password 變更。光貿與雲端同時不可達時阻擋全部業務；純 Local 光貿不可達同樣阻擋，直到光貿恢復。連線矩陣及自動恢復見 CY_ID_INTEGRATION §14.2。恢復連線後，Online authority 必須更新／取代本機 cache。

Workspace-wide account mutations 不支援 offline。

## 10. Cloud → Local destructive reset

使用者在已有 Cloud Device identity 的電腦選擇「單機版」時，不允許只把 mode flag 改成 Local。已完成 cutover 的 CloudPreferred 與尚在 whole-device transition 的 CloudTransition，都必須先安全處理目前 Device identity；差別只在當下 Employee authority 來源。

```text
使用者選擇切換單機版
  ↓
第一次警告：切換將清除這台電腦全部 CYInvoice 本機資料
  ↓
第二次明確確認：資料清除後不可由本機復原，需重新建立單機版 SUPER_ADMIN
  ↓
完整關閉 CYInvoice，停止背景同步／敏感操作
  ↓
若當前 Device 已有 Cloud identity，先依正式 Device revoke/retire 規則處理該 Device
  ↓
Cloud 明確確認 Device 已 revoked，或 Workspace 已 disabled
  ↓
清除本機 Data／Cache／Local EmployeeStore／Cloud Employee cache／
Cloud identity、token、pending state 與本機設定
  ↓
重新啟動到首次使用狀態
  ↓
選擇單機版並重新建立 Local SUPER_ADMIN
```

此動作只清除目前這台 Windows 電腦的 CYInvoice 本機資料；不得刪除既有 Cloud Workspace、其他 Device 或中央 Employee／CY ID Workspace。

之後若再次選擇 Cloud，視為全新 Local authority 的裝置，重新走正式加入／轉換流程，建立新的 Device identity，不得復活被清除前的 Device Token、舊 Cloud cache 或舊 Local authority。

### 10.1 Crash-safe reset transaction

Cloud revoke 可能已在 server commit，但 client 因網路中斷收不到成功回應。Windows 因此不得以「呼叫失敗」或「HTTP 斷線」推測 Device 是否仍 active。

正式 reset 必須在本機 Data／Cache 外保留最小 reset marker，且 marker 不存 Employee password 或 Device Token。只有以下任一條件經 Cloud 明確確認後才可進入本機 wipe：

- 目前 Device 已 `revoked`；或
- Workspace 已 `disabled`。

若結果不明，保留 Data、Cache、Device Token 與 marker；下次啟動先使用狹窄的 Device self-status recovery surface 重新確認。若 Cloud 仍回報 Device `active` 且 Workspace `active`，取消未完成 reset 並完整保留本機狀態；若狀態仍無法確認則 fail-closed，不進入一般程式操作。

### 10.2 Built-in Cloud 最後一台 Device

Built-in Cloud 目前沒有 Device 之外的獨立 recovery root，因此 active Workspace 的最後一台 active Device 不可直接 revoke。

```text
仍有其他 active Device
→ revoke current Device
→ Cloud 確認 revoked
→ wipe local

最後一台 active Device + 要保留 Workspace
→ 先加入另一台 Device
→ 再退出目前 Device

最後一台 active Device + 不再使用 Workspace
→ 操作者在中央 Workspace 管理端手動停用 Workspace
→ Windows 只確認 Workspace 已 disabled
→ wipe local
```

Windows Client 不提供 Workspace disable、delete、purge、全 Device 銷毀或其他 Workspace 級 destructive action。這些屬高 blast-radius 中央資源管理，不應由一般桌面程式持有。

### 10.3 CY ID Cloud 未來規則

CY ID 模式若正式完成以下 recovery contract：

```text
有效 CY ID SUPER_ADMIN
+ CYInvoice App Access
+ 有效 CY ID Workspace ↔ CYInvoice Workspace binding
→ 可重新授權新的 CYInvoice Device
```

則 CYInvoice Workspace 可以合法存在 `0 active Device`。CY ID SUPER_ADMIN 可在新電腦重新認證後，為原 Workspace 建立全新的 Device identity；不需要復活舊 Device Token。

在上述 CY ID recovery 尚未正式實作與驗收前，不修改 Built-in Cloud 現有 `LAST_ACTIVE_DEVICE` 保護，也不讓 CY ID 假設滲入 Package 4。

## 11. Built-in Credential

Built-in Cloud Mode 的同一 Employee 在 A / B 是同一套 credential authority。

```text
Password plaintext
  ↓ Windows 本機 KDF
credential verifier
  ↓ HTTPS
Built-in Cloud 儲存 verifier + credential_version
```

新密碼明文不上 Cloud、不寫 log、不寫 repo。

各可信 Device 取得可供離線驗證的 credential cache 時，必須再使用 Windows secure storage / DPAPI 邊界保護。

CY ID 模式不沿用本節作為 Shared Identity 儲存規則；CY ID credential ownership 由 CY ID 工作線決定，CYInvoice 只依其穩定 consumer contract 整合。

## 12. Account management ownership

### Local

CYInvoice 顯示並管理本機帳號。

### Built-in Cloud

CYInvoice 顯示並管理 Built-in Cloud 帳號。全域帳號異動 Online-only 且 execution-time re-auth。

目前正式 role：

- `SUPER_ADMIN`
- `ADMIN`
- `USER`

Built-in Cloud 帳號管理涵蓋 Employee create/edit、Email、enabled、password、role 與 SUPER_ADMIN transfer。

### CY ID Cloud

CYInvoice 的「帳號管理」功能直接隱藏。

Employee create/edit、Email、password、enabled、SUPER_ADMIN、CYInvoice access 與 CYInvoice role 由 CYWEB / CY ID 帳號中心處理。CYInvoice 不建立第二套帳號 CRUD UI。

CY ID 內部 Group、Application Access schema 與管理方式不在 CYInvoice 工作線決定。

## 13. CY ID role contract

CYInvoice 只要求 CY ID 最終提供適用於 CYInvoice 的權限結果：

```text
SUPER_ADMIN
ADMIN
USER
```

直接 1:1 使用：

```text
CY ID SUPER_ADMIN → CYInvoice SUPER_ADMIN
CY ID ADMIN       → CYInvoice ADMIN
CY ID USER        → CYInvoice USER
```

`SUPER_ADMIN` 不降階、不另做「視同 ADMIN」。

App Access 是 CY ID 的上層入口控制；CYInvoice 不提供第二套 App Access 管理功能。

Role rename 已實作；Local employees／Cloud cache 以一次性 schema migration 轉為 USER，Cloud 以 forward migration 0010 收斂。active runtime 不保留 EMPLOYEE alias。原先「不需 migration」假設已由實際 schema 契約取代。

## 14. SUPER_ADMIN Transfer（Built-in Cloud）

目標 Y 必須已是 enabled `ADMIN` + verified Email。

```text
X 執行移交
  ↓
X 當下重新驗證 Employee No + Password
  ↓
寄 OTP 至 X 目前已驗證 Email
  ↓
X OTP 成功
  ↓
backend 再確認 X / Y 當下狀態
  ↓ atomic transaction
X ADMIN
Y SUPER_ADMIN
Workspace Recovery Email = Y verified Email
```

不能由 Y 自我升級；不能由普通 ADMIN 把其他人升 SUPER_ADMIN；不能讓 Workspace 出現 0 或 2 名 SUPER_ADMIN。

CY ID 模式的 SUPER_ADMIN ownership 與 transfer 由 CY ID 帳號中心處理；CYInvoice 只消費其結果。

## 15. Recovery Email / Device loss

CYInvoice Workspace 不因 Device loss 重建。

Built-in Cloud 若仍有可信 Device，新增 Device 使用既有 Workspace 授權流程。若只剩最後一台 active Device，在沒有另一條正式 recovery path 前不得把該 Device 撤銷並留下 active Workspace。

CY ID Cloud 未來若已具備有效的 CY ID SUPER_ADMIN recovery contract，則可以 0 active Device 保留原 CYInvoice Workspace；SUPER_ADMIN 重新通過 CY ID authority 後可授權全新的 Device identity 加回原 Workspace。

Device recovery 是 CYInvoice Device lifecycle 問題；即使 CY ID 提供 Employee recovery，也不能把 CY ID password recovery 誤當成舊 Device Token recovery。

## 16. Legacy reconciliation

舊的單帳號 `POST /v1/employees/reconcile-local` 與任意 30 分鐘 Employee import window 已退役。它們與 whole-device transition、單一 authority 模型衝突。

Built-in Cloud 正式流程只能使用 whole-device Employee Transition。

## 17. Public repository safety

不得進 Public repository / PR / Actions log / artifact metadata：

- AMEGO App Key、MO 密碼、Cloud runtime secret；
- Device Token / OTP / recovery code；
- 真實 Employee Email / customer data；
- 私人 production endpoint 或正式營運資料。

Cloudflare Worker + D1 是 reference implementation；CYInvoice 對外可自架能力不得依賴志遠私人 CY ID 才能運作。

## 18. 目前尚待完成

- 四個 foundational package 已合併，CI 與 9/29 development deployment 完成；尚待最新版 A/B/C lifecycle 人工驗收。
- 邀請碼加入、撤銷／重寄、result-unknown recovery 實機驗收。
- A/B Built-in Employee CRUD、role、enabled、password 與 reconnect 行為驗收。
- CYID canonical contract 1.0.2／minimum 1.0.0；V2.6.15 adapter／desktop transport source 已接線，尚待隔離 staging、Windows acceptance、遷移／rollback 與正式切換。
- CYID protected offline cache 已實作；Windows 實機斷網／reconnect／deny／scope acceptance 尚待完成。
- CY ID `0 active Device` recovery / re-authorize Device flow。
- Built-in all-Device-Token-loss 的獨立 Recovery Device flow（若未來產品需要）。
- 後續 business sync / Work Item / Audit。
- **延後／非目前阻塞：**Cloud Employee offline cache server-signed snapshot／完整性簽章。除非實際發生竄改事件、威脅模型提高或有稽核需求，否則保留 TODO，不投入目前版本成本。

Merge／deploy 依 repository governance 與當前使用者授權執行；tag 與正式 GitHub Release 仍另行確認。


## V2.6.14 使用版本、最後使用與裝置名稱

每次開啟成功回報，僅由該 Device Token 更新自己的 VERSION／BUILD 與 Cloud UTC last_seen_at；不從本機提交時間或另一台 target。舊版、離線及失敗保留先前確認值，不等同目前在線。改名沿用中央 SUPER_ADMIN 執行時驗證，不改 token／Device identity／Employee authority；同一 Workspace active Device 與當前 credential 在 transaction 再核對，device_renamed audit 與名稱同交易。撤銷 Device 仍保留 DB／audit，Windows inventory 不再顯示歷史；LAST_ACTIVE_DEVICE 與安全 reset 契約不變。

## 19. CYID 部署的 D1 舊帳號資料退場（規劃中，2026-10-13）

本節為已被 §20 取代的歷史 credential-only 準備提案；其「尚未授權／未清理」描述與舊員工表 SQL 是當時狀態，不能對已完成 CYID_ONLY 退役的本公司 D1 直接重跑。現況與後續工作以文件首段、§20 及 TODO 為準。

**性質：設計／清理準備，不是已執行的 D1 清除、migration 或 Worker 部署。** 使用者已確認本批 TODO 已勾選項目完成，指示先研究及規劃 CYInvoice D1 中舊 Built-in Employee 資料的退場；本階段不修改遠端資料、真實 Workspace、Device Token、Cloudflare Worker 或 CYID。所有實際資料刪除／不可逆清理仍需對應環境的精確範圍、備份及另行確認。

### 19.1 目前有證據的架構事實

1. **兩個 authority／兩個資料範圍**：目前 development CYInvoice Worker 曾於 2026-10-12 的受控 Run #42 驗到 `CYID_ENABLED=true`，已有 1 個原 CYInvoice Workspace、4 筆 Device record 保留；這是當次 deployment 證據，不是 2026-10-13 的即時 D1 SELECT 或 Employee 實際筆數。Employee／Credential／Role 的線上權威已改由 CYID；CYInvoice D1 仍承載自己的 Workspace、Device、Pairing、Invitation、Security Audit，未來還會承載 Work Item，絕不能與 CYID D1 合併／整庫刪除。
2. `cloud/src/app.ts` 在 CYID mode 已拒絕 `/v1/employees*`、`/v1/employee-*`、`/v1/web-auth*`、`/v1/web-password*`、`/v1/super-admin*` 及舊 bootstrap authority 路由；`runtime-sync.ts` 在 CYID mode 取 CYID `IDENTITY_AUTHORITY` 結果而不是 Built-in `employeeSnapshot`。目前只足以判斷 **CYID mode 的正常帳號 authority 不依賴 Built-in credential rows**，不能由此直接斷言所有舊資料安全可刪。
3. `cloud/migrations/0004_central_employees.sql` 建立 `cloud_employees`、`device_employee_links`、`employee_link_candidates`；`0006_employee_transition_items.sql` 建立轉換資料。它們有多層 `ON DELETE RESTRICT`：`device_employee_links.employee_id`、`employee_link_candidates.resolved_employee_id`、`employee_transition_items.matched_employee_id/employee_no_match_id/email_match_id`；`0013_cyid_invitation_actor.sql` 仍讓 **舊 `device_invitations.issued_by_employee_id` 參照 `cloud_employees`**，新的 `issued_by_cyid_employee_id` 則分開保存。刪除父表或舊 Employee row 會遇到外鍵／歷史斷鏈。
4. `security_audit_events.actor_employee_id` 為歷史身分參照（未宣告指向 Built-in Employee 的 FK），CYID 新事件可記錄外部 ID；在未核對事件來源與保留策略前，不得改寫成「相同員編就是同一人」或全面清空。
5. `email_otp_challenges` 不是純舊帳號表。`index.ts` 的 Device pairing Email OTP 在 CYID 路徑仍可能使用這張表；`workspaces.recovery_email`、`devices.employee_authority_state`、`workspaces.employee_revision` 也牽涉既有 Built-in／復原／相容生命週期，不能僅按欄位名稱直接刪除。
6. **Built-in Cloud／Self-hosted 是仍受支援的獨立正式模式，非 CYID fallback**；同一套 reference schema 與 source 還需要 `cloud_employees` 和 Employee/OTP 程式。不能為清理目前的 CYID 部署而全域 `DROP TABLE cloud_employees`、刪除 Built-in handler／migrations，或對其他 Self-hosted／Built-in Workspace 做相同清理。
7. `cloud/scripts/deployment-preflight.mjs` 在遠端部署前會對 `cloud_employees`、Device／Invitation／Audit 等資料作歷史 fingerprint，並要求 `PRAGMA foreign_key_check` 通過。**任何正式核准的清理都應由專用、可稽核的資料維護步驟處理，調整保存證據的比對邏輯；不得為了讓部署通過而無條件取消 `HISTORY_CHANGED_CLOUD_EMPLOYEES` 保護。**

### 19.2 資料分組與預設決策

| CYInvoice D1 區域 | CYID mode 的原則 | 目前動作 |
| --- | --- | --- |
| `cloud_employees` 的 `credential_verifier`、`credential_algorithm` 等 Built-in 憑證 | 優先考慮**只對已確認的 CYID Workspace**清除不再使用的 credential material；現有 Built-in mode 保留 | 先列出非 NULL 筆數、所屬 Workspace 和依賴，再評估局部 `UPDATE`，**不立即執行** |
| `cloud_employees` 其餘員編／姓名／Email／Role／ID | 可能仍是邀請、轉換與歷史事件的關聯錨點；不能不分先後刪除 | 暫時唯讀保留；確認審計留存、FK 與資料最小化方案，再討論 placeholder／歸檔／個資消除 |
| `device_employee_links`、`employee_link_candidates`、`employee_transition_items` | Built-in 的 Local→Cloud 歷史／過渡，部分有 Employee FK | 核對尚未結束 state 與關聯；沒有引用且已經過核定保存期才可規劃刪除 |
| `email_otp_challenges` | Device pairing OTP 與舊 Employee／Recovery OTP 混在同表 | 依 `purpose`、有效期限、是否消耗分類；**禁止整表刪除**，有效 pairing OTP 不碰 |
| `device_invitations`、`security_audit_events` | 裝置加入與歷史 actor 證據（含舊 Built-in 指標） | 保留外鍵與審計鏈，不為清 Employee 而刪除／偽造 actor |
| `workspaces`、`devices`、`device_pairing_codes` | 現行 CYInvoice 業務 Workspace／Device／入網授權的必要資產 | **維持原樣**，不更換 Workspace ID／Device ID／token hash，不 reset/rejoin |
| `workspaces.recovery_email`、`employee_revision`、`devices.employee_authority_state` | 可能仍涉及配對 OTP、Built-in 或舊端相容 | 暫不清理；依實際讀寫與 live scope 確認，不能猜成無用欄位 |

### 19.3 分階段執行方案與停損門檻

**階段 A — 唯讀現況盤點（現在可準備，不部署 Worker）**

- 從現行已授權的 GitHub source／正式部署紀錄確認目標為 CYInvoice **原本那個** Worker 綁定的 D1（非 CYID D1、非任何舊的測試 Worker），精確對照 worker version、D1 binding ID（只在私有執行環境核對）、`CYID_ENABLED`、CYID Application/Workspace binding。
- 對原 D1 使用既有受控的 **read-only 管理介面／授權工具**，只取每表總筆數及目標 Workspace 分組計數、`cloud_employees` credential 非 NULL 數、`device_invitations` 舊／CYID actor 筆數、Transition 未完成筆數、OTP `purpose`／過期數、`PRAGMA foreign_key_list`／`PRAGMA foreign_key_check` 結果；**不可 `SELECT *` 或在 Public PR／Actions log 列出員工姓名、Email、hash、Device token、OTP**。對照既有 Run #42 歷史數據，不把它冒稱現在的即時數據。
- 建立唯讀、無個資的 inventory checkpoint，先驗證 CYID 管理員及員工 App Access、Device 管理與既有 Windows 授權邊界；若有仍使用 Built-in 的 Workspace、失效的 CYID scope、未知 Worker binding 或未結束 Employee transition，**停止清理**。

**階段 B — 清理方案與本機驗證（不動真實 D1）**

- 以既有 Cloud Worker／SQLite fixture 在本地驗證 CYID mode：Employee route 拒絕、配對 Email OTP、邀請歷史、Device rename/revoke、runtime sync、A/B/C 不受影響；Built-in/self-hosted 模式以另一個 fixture 驗證舊 Employee lifecycle 完整保留。
- 對照 FK 與 Audit，設計第一批只清除 CYID Workspace 的**舊 credential material**的受限／可重試維護操作；要有正式 Workspace scope、預計變更數、資料條件、交易完整性、dry-run 數量與修改後 invariant／FK check。不能用未指定 Workspace 的 `DELETE FROM cloud_employees` 或廣域 `DROP TABLE`。
- 設計獨立的歷史資料保留／個資最小化政策。舊 Invitation 的 Built-in Employee ID 如需移轉成不可識別的留存識別，應透過核准的 forward migration、明確欄位語意及審計證據，不挪用 `issued_by_cyid_employee_id` 來偽裝舊 actor。
- D1 的 Time Travel 是有限期間恢復，不代表永久備份；依當時方案／計畫確認時限及 recovery bookmark，另外準備**私有、受存取與銷毀規則管理**的核准備份／回復方案，不把原始 DB 匯出到公開 repo 或 PR artifact。

**階段 C — 使用者批准後的受控清理（本階段尚不授權執行）**

- 使用者核可**確切 D1／Workspace、要清的欄位與資料保留範圍、備份/回復策略、執行窗口**後，才進行指定 Workspace 的最小變更。先記錄唯讀 fingerprint，再以受限批次／交易實作 credential 清理，保留所有 FK 身分連結與安全 audit。
- 清理後重做唯讀 counts／外鍵／history fingerprint 差異審查、CYID Device／Identity runtime contract、本地／CI 回歸；若和預期不一致，停止後續清理並依已核准的回復程序處理。對任何既有本機 Offline cache 不主張已實體清除；該 cache 需另行審視其原保護與失效生命週期。
- 舊名稱／Email／Transition data 的永久歸檔或銷毀列為**獨立第二批提案**，不得在 credential 第一批內默默進行；整表 schema removal 不是現在的目標。

**階段 D — 與 V3.0 開發接續**

- 優先實作已規劃的 Recovery Device 與真實角色／裝置 contract 驗證，再建立帶有明確 `actor_provider` 或等效來源語意的 Cloud Work Item／Audit，以免新資料再依賴 Built-in `cloud_employees.employee_id`。
- 然後才推進多裝置 Work Item revision／原子結案、離線 OrderID 防撞和正式折讓 API／逐筆歷史重做；不要把 D1 清理誤當作這些功能已完成。

### 19.4 原 D1 的只讀 SQL 核對清單（僅供後續受控執行）

以下為 **唯讀 SELECT／PRAGMA** 模板，不包含任何變更語句；`?1` 的 CYInvoice Workspace ID 必須由**私有、已確認原 D1** 的受控工具注入，不得改成未經驗證的文字拼接，也不得把結果內的實際 Workspace ID、Employee ID、員編、Email、Token hash、OTP digest 公開貼到 PR／Actions。唯讀成功並不代表可以直接做下一步刪除。

```sql
-- 1. 確認 schema 與完整性：必須 0 FK violation
PRAGMA foreign_key_check;
PRAGMA foreign_key_list('device_invitations');
PRAGMA foreign_key_list('device_employee_links');
PRAGMA foreign_key_list('employee_link_candidates');
PRAGMA foreign_key_list('employee_transition_items');

-- 2. 目標 Workspace 帳號／仍留存的 Built-in credential material
SELECT COUNT(*) AS employee_rows,
       SUM(CASE WHEN credential_verifier IS NOT NULL THEN 1 ELSE 0 END) AS verifier_rows,
       SUM(CASE WHEN credential_algorithm IS NOT NULL THEN 1 ELSE 0 END) AS algorithm_rows
  FROM cloud_employees WHERE workspace_id = ?1;

-- 3. 會使舊 cloud_employees 仍被引用的記錄
SELECT COUNT(*) AS link_rows
  FROM device_employee_links l
  JOIN devices d ON d.device_id = l.device_id WHERE d.workspace_id = ?1;
SELECT state, COUNT(*) AS rows
  FROM employee_link_candidates WHERE workspace_id = ?1 GROUP BY state;
SELECT state, COUNT(*) AS rows
  FROM employee_transition_items WHERE workspace_id = ?1 GROUP BY state;

-- 4. 邀請簽發者來源，尤其需要保留的舊 Built-in FK
SELECT COUNT(*) AS invitations,
       SUM(CASE WHEN issued_by_employee_id IS NOT NULL THEN 1 ELSE 0 END) AS legacy_actor_rows,
       SUM(CASE WHEN issued_by_cyid_employee_id IS NOT NULL THEN 1 ELSE 0 END) AS cyid_actor_rows,
       SUM(CASE WHEN consumed_at IS NULL AND revoked_at IS NULL
                    AND expires_at > strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
           THEN 1 ELSE 0 END) AS active_invitation_rows
  FROM device_invitations WHERE workspace_id = ?1;

-- 5. 原 Workspace 的保留資產（不可清除／重建）
SELECT status, COUNT(*) AS rows FROM devices
 WHERE workspace_id = ?1 GROUP BY status;
SELECT event_type, COUNT(*) AS rows FROM security_audit_events
 WHERE workspace_id = ?1 GROUP BY event_type;

-- 6. OTP 為共享用途表，沒有 workspace_id；僅依 purpose 匯總，
--    不查出 challenge/email/digest 明文。
SELECT purpose, COUNT(*) AS rows,
       SUM(CASE WHEN consumed_at IS NULL
                    AND expires_at > strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
           THEN 1 ELSE 0 END) AS unexpired_unconsumed_rows
  FROM email_otp_challenges GROUP BY purpose;
```

**判定條件**：必須先核對 `PRAGMA foreign_key_check` 為零、上述 counts 在可預期範圍、沒有未知的 Built-in 使用者／未完成 transition、CYID 與 Device 最新驗證正常；若有不一致，停止。執行階段的 D1 變更要另行產生明確 dry-run／expected-change-count／transaction 計畫及私有備份與回復證據，不能從本節 SQL 直接推定可刪除員工基本資料。

**全程限制：**既有 Cloudflare 測試 Worker 刪不掉的問題已由使用者明確禁止重試。這項清理的研究、本地驗證與 PR 不得建立／上傳／部署任何測試 Worker、遠端測試 handler 或替代測試服務，也不得修改 CYID D1。正式 Cloud D1 資料操作不是本規劃的隱含授權。

## 20. 現行決策：公司 CYID 專用 D1 完整退除 Built-in 帳號表（2026-10-13）

**本節取代 §19「只優先清除 verifier、保留員工表」的早期提案。** 使用者已明確重申：本公司 CYInvoice 原本的 D1 現已全面使用 CYID，應真正移除舊 Built-in 員工帳號與驗證資料表；未來不使用 CYID 的其他公司則從獨立新 D1 建立 Built-in 帳號資料，**絕不共用本公司 D1**。Built-in Cloud 的程式、API 與原始 migrations 均須保留正常運作。這是 **CYID-only deployment 的實體資料庫退役**，不是刪除整套 Built-in 功能，也不是把 CYID 資料搬回 CYInvoice。

### 已完成實作與本公司原 D1 維護（Cloud 0.9.3，2026-10-13）

[原 Worker 部署](https://github.com/simonliu1118-byte/CYapps/actions/runs/38396129643) 已通過，CYID_ENABLED 與原 bindings 保留；使用者另行批准的原 D1 退役維護已完成。以下為已落地的實作與資料保留邊界；執行證據、加密備份及回復資訊只在私有環境保存。此紀錄不構成其他 D1 的執行授權。

- `cloud/maintenance/retire-built-in-for-cyid-only.sql` 為**需獨立核准**的 CYID-only 維護 SQL，**刻意不放** `migrations/`，避免任何 Built-in／自架使用者因一般 Cloud migration 誤刪帳號。僅可對核對過的本公司原 CYInvoice D1 執行，且應納入原子交易／私有備份與回復窗口；不得對 CYID D1 或舊測試 Worker 操作。
- 移除 `cloud_employees`、`device_employee_links`、`employee_link_candidates`、`employee_transition_items` 四張 Built-in Employee 與 Local→Cloud transition 表；**不保留空表**。同時重建 `device_invitations` 去除對 `cloud_employees` 的 FK，但保留所有已存在的舊 `issued_by_employee_id` 原值作歷史 opaque actor reference（不等同 CYID 身分）、`issued_by_cyid_employee_id`、邀請與 Device 外鍵、狀態與事件。未來 audit/Work Item 必須明確標出 actor provider，不能將舊 ID 默認為 CYID。
- `email_otp_challenges` 屬共用驗證碼機制，CYID 新裝置的 pairing authorization 仍需要；因此**保留 OTP 表**，清除除 Device pairing 授權外的舊帳號／Workspace recovery／Email 驗證／密碼／SUPER_ADMIN 移交等 purpose 紀錄，並將 CYID-only Workspace 的原 Built-in `recovery_email`／`recovery_email_verified_at` 置空（不變更 Workspace ID、Device 或同步 revision）。`workspaces`、`devices`、`device_pairing_codes`、`device_invitations`、`security_audit_events` 及歷史 actor 依約保留。
- 加入 `cyid_storage_profile` 唯一標記，只有經受控清理的原 D1 才能宣告 `CYID_ONLY`。部署前檢查可辨識 Built-in 或 CYID-only；缺少舊表卻無合法標記、部分表殘存或發行期間 profile 變動皆應 fail closed，不能以缺表為由關閉整體歷史 fingerprint／FK 監測。
- `device-lifecycle.ts` 的 CYID Device rename 另採不引用 Built-in Employee 表的 SQL；Built-in 仍保留 SUPER_ADMIN verifier 檢查，避免 `OR` 分支即使不執行也需要 SQLite 解析已刪表。Cloud Runtime Sync CYID 依賴 CYID private authority，Built-in 仍用原快照路徑。
- `cloud/tests/cyid-account-retirement.test.mjs` 使用真實 SQLite migration fixture 驗證 CYID-only 實體刪表、Device／Invitation／Audit／OTP 保留、Built-in schema 不受影響，並測試未完成 transition／多 Workspace 時拒絕操作；CI 包含其測試。

### 本公司原 D1 維護完成證據與接續工作

1. **原目標唯讀核對完成**：確認原 Worker 唯一 D1 binding、CYID_ENABLED=true、原業務 Workspace、無待處理 transition，並記錄私有資料與 FK 基準；未操作 CYID D1。
2. **備份與回復驗證完成**：私有唯讀邏輯備份涵蓋應用資料表、schema/index 與 SQLite sequence（不匯出 Cloudflare 保護內部表），擷取前後 bookmark 一致。Windows DPAPI 加密備份已驗證解密 checksum、本機 SQLite 還原及原資料維護模擬；不宣稱已做遠端還原或跨電腦回復。
3. **相容程式先部署完成**：原 Worker 已升級 0.9.3，保留全部 bindings 與 CYID_ENABLED=true。Built-in／CYID_ONLY source 回歸與原資料本機模擬通過；未上傳測試 Worker 或遠端測試 handler。
4. **另行授權的原子維護完成**：透過已授權的 Cloudflare 管理工具執行受限維護批次，四張帳號／轉換表實體移除、Invitation FK 調整、舊帳號用途 OTP 與 Built-in recovery email 清理完成；不在一般 migrations 自動執行。
5. **事後讀回完成**：CYID_ONLY marker、表與暫存表不存在、原 Workspace／Device Token hashes／Invitation／Pairing／Audit／migration history 與 sequence 保留逐筆一致，FK 零異常、SQLite quick check 正常，Worker health 與舊 Employee route 拒絕符合預期。

此結果沒有將未執行的 Windows 全角色／多裝置實機矩陣宣告通過。DPAPI 備份依賴原 Windows 使用者與電腦；0-Device recovery、可攜式加密備份／金鑰保管及長期回復程序仍依唯一 [TODO §7.4](TODO.md) 接續。已退役 D1 不得僅關閉 CYID flag 回退 Built-in，也不得盲目重跑非冪等 DDL。

**長期資料庫模式**：本公司 CYID deployment 可以是帶有 `CYID_ONLY` marker 的小型協同業務 D1；未來其他公司 Built-in deployment 則全新 D1 依 0001..0013 schema 建立帳戶權威。兩者共用 provider-neutral CYInvoice 程式，不共用員工列、密碼或原 Workspace。

## 21. V3 多機協同的 CYWEB 裝置復原（2026-10-13 設計）

使用者決定災難復原與 V3 多機協同一起做，由 CYID 超管在 CYWEB 簽發復原邀請碼，新 Device 接回原 CYInvoice Workspace；裝置／邀請／協同資料上 CYInvoice 業務 D1，不合併 CYID authority。具體情境、private issuer、App-scoped per-operation 授權、issuer FK／forward migration、Pending Token／結果不明、備份及驗收見 [V3 裝置復原設計](CLOUD_DISASTER_RECOVERY.md)，唯一待辦在 TODO §7.4／§7.5。

這是待實作的產品設計，尚無 CYWEB 簽發入口或新版 D1 migration。既有有效加入碼仍沿用原流程；當所有 Token 與加入碼都不可用時，現行程式尚不能自助復原。CYWEB／CYID／業務服務故障先走維運回復，不能把 core entry access 當作永久可用性保證。
