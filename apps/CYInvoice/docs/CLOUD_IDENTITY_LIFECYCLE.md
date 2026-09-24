# CYInvoice V3.0 Workspace／Device／Employee 身分生命週期定案

本文件記錄 CYInvoice V3.0 在 Workspace、Device、Device Token、Employee、Local → Cloud 轉換、離線、Recovery Email 與 SUPER_ADMIN 移交上的定案行為。

本文件是設計／需求基準，不取代 `PROJECT_RULES.md`。治理衝突時仍依 repository／project governance 優先順序處理。

## 1. 核心不變量

1. Workspace 建立後長期存在；Windows 重灌、程式重新下載、Device Token 遺失都不得因此重建 Workspace。
2. Workspace 恰好一名 `SUPER_ADMIN`；`ADMIN`、`EMPLOYEE` 可多人。
3. Device 與 Employee 是兩種不同身分；Device Token 只能證明可信任電腦，不能單獨證明操作者是管理員。
4. CYInvoice 不採持續登入。需要權限的操作一律在**執行當下**要求 Employee No + Password，並依當下有效 role 授權。
5. Local Mode 只有一套 Local Employee authority；Cloud cutover 後只有一套 Cloud Employee authority。
6. Cloud Mode 斷網只是 Offline，不得復活舊 Local EmployeeStore 作第二套 authority。
7. Cloud Mode 本機只保留 Cloud Employee cache + protected offline credential verifier。
8. Cloud Employee 全域異動為 Online-only；不支援離線帳號修改後再 conflict merge。
9. Device Token 不寄 Email、不寫 log、不存 Cloud 明文；Windows protected storage 保存，Cloud 只存 hash。
10. Identity matching 不使用姓名猜測；只使用 Employee No + Email 的明確規則。
11. Cloud 是協作／中央身分服務，不是發票業務 kill switch；AMEGO 仍是發票／作廢／折讓官方真相。

## 2. Local Mode

未加入 Cloud 或尚未完成 cutover 時：

```text
Local EmployeeStore = 唯一帳號 authority
```

權限驗證仍依既有 CYInvoice 模式：

```text
執行敏感功能
  ↓
當下輸入員編 + 密碼
  ↓
Local EmployeeStore 驗證
  ↓
依 Local role 執行此次操作
```

程式啟動本身不登入任何帳號。

## 3. 第一個 Workspace / 第一台 Device

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

## 4. Existing Workspace / B Device Join

B 機加入流程：

```text
使用者選擇加入既有 Workspace
  ↓
B 機當下驗證 Local ADMIN / SUPER_ADMIN
  ↓
才允許輸入 Pairing Code
  ↓
Windows 先產生並安全保存 Pending Device Token
  ↓
POST pairing claim
  ↓
Cloud 建立 Device ID，只存 Token hash
  ↓
GET /v1/device 核對
  ↓
B Device 加入完成
  ↓
進入 whole-device Employee Transition
```

B 的 Local 管理員驗證只證明「操作者有權管理 B 機」。Pairing Code 另外證明「Workspace 已授權這台 Device 加入」。兩者不可互相取代。

Pairing Code 授權 Device，不授予 Employee role。

## 5. Whole-device Employee Transition

Device 加入後先進入：

```text
CloudTransition
```

此時尚未完成 Cloud Employee authority cutover，因此既有 Local EmployeeStore 仍是唯一帳號 authority；不建立任何「Pending Y 暫時管理員」或 Local/Cloud 雙角色特例。

一次盤點**全部既有 Local Employees**。

### 5.1 Identity matching matrix

| Employee No | Email | 判定 | 行為 |
| --- | --- | --- | --- |
| 未命中 | 未命中 | 新 Employee | 驗證本人 Email，建立中央 Employee |
| 命中 A | 命中同一 A | 同一 Employee | 直接採用 A 的 Cloud 資料 |
| 命中 A | 未命中 | 衝突 | 交目前 SUPER_ADMIN 人工確認 |
| 未命中 | 命中 A | 衝突 | 交目前 SUPER_ADMIN 人工確認 |
| 命中 A | 命中 B | 衝突 | 交目前 SUPER_ADMIN 人工確認 |

姓名只作 UI 顯示，不作身分比對依據。

「Employee No + Email 都命中同一中央 Employee」不是 merge；確認同一人後，直接改採 Cloud Employee 的 name / Email / role / enabled / credential authority。

### 5.2 新 Employee role

第一台 A 機的 X：

```text
Local SUPER_ADMIN X → Cloud SUPER_ADMIN
```

既有 Workspace 的 B 機若 Local SUPER_ADMIN Y 是真正的新中央 Employee：

```text
Local SUPER_ADMIN Y → Cloud ADMIN
```

這不是本機降權；是在新中央 Employee 建立時指定 Cloud role。正式 cutover 前仍完全使用 Local authority。

如果 Y 精確命中既有中央 Employee，直接採用既有 Cloud role，不強制改成 ADMIN。

### 5.3 Conflict

任何 partial / divergent match 都只建立 pending identity case，不建立重複中央 Employee、不猜測、不自動改 role。

Account Management 只在有 unresolved case 時顯示：

```text
待確認帳號 N
```

真正 Resolve 時，必須在當下重新驗證目前 Workspace `SUPER_ADMIN`，backend 再檢查該 Employee 此刻仍是唯一 SUPER_ADMIN。

## 6. Cutover

只有當本機全部既有 Employee 都完成：

- identity resolution；
- 必要 Email verification；
- credential preparation；
- conflict resolution；

才允許 cutover。

```text
Local authority
  ↓ atomic logical cutover
Cloud Employee authority
```

Cutover 後：

- Cloud Employee 是唯一帳號主資料。
- Local EmployeeStore 不再作權限 authority。
- 本機 Cloud Employee cache 只供顯示、離線驗證與安全 fallback。
- Cloud 更新成功後，各 Device 重新抓 Employee snapshot 更新 cache。

## 7. Cloud Mode execution-time authentication

Cloud Mode 也沒有 persistent login。

```text
按下敏感操作
  ↓
輸入 Employee No + Password
  ↓
Online：Cloud backend 驗證 credential + current role
Offline：本機以最後同步的 protected Cloud credential cache 驗證
  ↓
只授權此次操作
```

開啟帳號管理視窗時驗證過某個帳號，不代表後續帳號異動可以沿用該身分。新增、修改、role、enabled、password、conflict resolution、SUPER_ADMIN transfer 等敏感動作仍須執行時重新驗證。

## 8. Cloud Mode Offline

斷網後仍是：

```text
Cloud Mode / Offline
```

不是 Local Mode。

可使用最後成功同步的：

- Employee identity；
- role；
- enabled；
- credential version / protected verifier。

完全離線期間無法知道 Cloud 上剛發生的 role、enabled 或 password 變更，這是離線系統不可消除的限制。恢復連線後，Cloud authority 更新本機 cache。

以下 Workspace-wide account mutations 不支援 offline：

- 新增 Employee；
- 修改 Email / name；
- role；
- enabled；
- password；
- conflict resolution；
- SUPER_ADMIN transfer；
- Device / Workspace 管理。

因此不需要設計「A 離線改一次、B 又改一次、上線再 merge」的雙主帳號衝突機制。

## 9. Credential

Cloud Mode 的同一 Employee 在 A / B 是同一套 credential authority。

Windows 建立新密碼時：

```text
Password plaintext
  ↓ Windows 本機 PBKDF2-SHA256
credential verifier
  ↓ HTTPS
Cloud 儲存 verifier + credential_version
```

新密碼明文不上 Cloud、不寫 log、不寫 repo。

各可信 Device 取得可供離線驗證的 credential cache 時，必須再使用 Windows secure storage / DPAPI 邊界保護。

密碼更新後 Cloud 增加 credential version；各 Device 下次同步更新 cache。

## 10. Cloud Employee account management

Cloud cutover 後，全域帳號異動全部 Online-only 且 execution-time re-auth。

目前正式規則：

- 新增 Employee：管理員 re-auth；新 Employee 自己的 Email OTP 成功後才建立。
- 修改姓名：管理員 re-auth。
- 修改 Email：管理員 re-auth + 新 Email OTP；OTP 成功前舊 Email 不變。
- `ADMIN ↔ EMPLOYEE`：管理員 re-auth；不能修改自己的 role。
- enabled：管理員 re-auth；不能修改自己的 enabled；SUPER_ADMIN 不可停用。
- password：本人可改自己；管理員可重設其他非 SUPER_ADMIN；SUPER_ADMIN 密碼只能本人改。
- `SUPER_ADMIN` role 不得透過一般 role update 修改。

## 11. SUPER_ADMIN Transfer

目標 Y 必須已是：

```text
enabled ADMIN + verified Email
```

流程：

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

## 12. Recovery Email / Device loss

Workspace 不因 Device loss 重建。

若仍有可信 Device，新增 Device 使用既有 Workspace 授權流程。

若所有 Device Token 都失效，但 Recovery Email 仍可用，後續需完成正式 Recovery Device flow。

若所有 Device Token 與原 Recovery Email 同時失效，最終維運救援為 reference backend / Cloudflare 管理端人工修改 Recovery Email；不再引入 Windows 第四套 emergency secret。

## 13. Legacy reconciliation

舊的單帳號：

```text
POST /v1/employees/reconcile-local
```

以及任意 30 分鐘 Employee import window 已退役。它們與 whole-device transition、單一 authority 模型衝突。

Reference backend 對舊 mutation route fail-closed；正式流程只能使用 whole-device Employee Transition。

## 14. Public repository safety

不得進 Public repository / PR / Actions log / artifact metadata：

- AMEGO App Key、MO 密碼、Cloud runtime secret。
- Device Token / OTP / recovery code。
- 真實 Employee Email / customer data。
- 私人 production endpoint 或正式營運資料。

Cloudflare Worker + D1 是 reference implementation；Windows contract 維持 provider-neutral。

## 15. 尚待完成的生命週期

- Remote development deployment + D1 Schema 7 migration 實際驗證。
- Brevo runtime secrets 完成後 live OTP delivery。
- A/B 多機 transition / offline / reconnect 實機測試。
- Device revoke。
- all-Device-Token-loss Recovery Device flow。
- 後續 business sync / Work Item / Audit。

任何正式 merge、tag、Release 仍需明確授權。
