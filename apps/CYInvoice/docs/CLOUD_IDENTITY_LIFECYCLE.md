# CYInvoice V3.0 Workspace／Device／Employee 身分生命週期定案

更新日期：2026-09-29

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

CY ID 模式 Windows offline credential/cache 的最終協定尚待實作階段定義，但不得直接讀 CY ID D1、不得造成 Built-in + CY ID 雙 authority，且 reconnect 後最新 CY ID authority 必須重新生效。

## 9. Cloud Mode Offline

斷網後仍是：

```text
Cloud Mode / Offline
```

不是 Local Mode。

Offline 可使用最後成功建立／同步的可信 cache：

- Employee identity；
- role；
- enabled；
- credential version / protected verifier 或 CY ID 模式等效的受保護 offline material。

完全離線期間無法知道 Cloud 上剛發生的 role、enabled 或 password 變更，這是離線系統不可消除的限制。恢復連線後，Online authority 必須更新／取代本機 cache。

Workspace-wide account mutations 不支援 offline。

## 10. Cloud → Local destructive reset

使用者在已完成 Cloud cutover 的電腦選擇「單機版」時，不允許只把 mode flag 改成 Local。

```text
使用者選擇切換單機版
  ↓
第一次警告：切換將清除這台電腦全部 CYInvoice 本機資料
  ↓
第二次明確確認：資料清除後不可由本機復原，需重新建立單機版 SUPER_ADMIN
  ↓
停止背景同步／敏感操作
  ↓
若當前 Device 已有 Cloud identity，先依正式 Device revoke/retire 規則處理該 Device
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

若退出 Cloud 前無法安全完成必要 Device revoke/retire，正式實作必須 fail-closed 或保留可恢復狀態；不可先刪掉唯一可用的本機 Device Token 再留下無法管理的 active Device。

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

目前實際使用資料沒有已持久化的 `EMPLOYEE` role 需要相容，因此 `EMPLOYEE` → `USER` 不需要歷史資料 migration；實作時只需同步修改 source/schema fixtures/tests/docs/UI。

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

若仍有可信 Device，新增 Device 使用既有 Workspace 授權流程。

若所有 Device Token 都失效但 recovery path 仍可用，後續需完成正式 Recovery Device flow。

Device recovery 是 CYInvoice Device lifecycle 問題；即使 CY ID 提供 Employee recovery，也不能把 CY ID password recovery 誤當成 Device Token recovery。

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

- Cloud 在線 execution-time Employee authority 即時刷新／驗證，避免新建或異動帳號需等背景同步。
- Device revoke / retire。
- Cloud → Local 破壞性重置。
- 邀請碼加入、撤銷／重寄、result-unknown recovery 實機驗收。
- A/B Built-in Employee CRUD、role、enabled、password 與 reconnect 行為驗收。
- CY ID consumer contract 穩定後的 CYInvoice 專用整合；目前不修改 source。
- CY ID 模式 Windows Offline credential/cache 技術方案。
- all-Device-Token-loss Recovery Device flow。
- 後續 business sync / Work Item / Audit。
- **延後／非目前阻塞：**Cloud Employee offline cache server-signed snapshot／完整性簽章。除非實際發生竄改事件、威脅模型提高或有稽核需求，否則保留 TODO，不投入目前版本成本。

任何正式 merge、tag、Release 仍需明確授權。