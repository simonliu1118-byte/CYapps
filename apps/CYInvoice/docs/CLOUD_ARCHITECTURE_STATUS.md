# CYInvoice Cloud Architecture Status

此文件只描述目前已實作／已驗證的雲端工程狀態。長期產品藍圖見 `CLOUD_ROADMAP.md`；Workspace／Device／Employee 身分生命週期見 `CLOUD_IDENTITY_LIFECYCLE.md`。

> 本 repository 為 Public repository。不得寫入任何實際 endpoint、密鑰、Device Token、OTP、真實 Email、正式公司資料或其他營運／個資資訊。

## 1. 工程基準

- Windows 正式產品線：C# / WinForms。
- 工程版本：V2.6.3 Build 0。
- Reference backend：Cloudflare Worker + D1。
- Cloud API：`1`。
- Cloud implementation version：`0.8.0`。
- Cloud schema compatibility：`7`，forward migrations `0001`～`0007`。
- Public Windows client 不內建專案擁有者私人 endpoint，只接受使用者設定的相容 HTTPS API。
- 已執行 migration 不回寫；schema 修改只能新增 forward migration。

GitHub Actions 驗證的是 source、Worker bundle、local SQLite migration、.NET contract、Windows build／startup smoke 與 engineering package；**不代表 Cloudflare remote Worker 或 remote D1 已部署至 Schema 7**。Remote 狀態必須另行查證。

## 2. 帳號權威模型

CYInvoice 不採「程式啟動後持續登入某人」的模型。

```text
需要權限的操作
  ↓
當下輸入 Employee No + Password
  ↓
驗證該次操作
  ↓
依當下 Employee Role 授權
```

單機模式：

```text
Local EmployeeStore = 唯一帳號主資料
```

從單機版轉 Cloud：

```text
Device 加入 Workspace
  ↓
CloudTransition
  ↓
盤點全部既有 Local Employees
  ↓
完成身分比對／Email 驗證／Credential 準備／衝突處理
  ↓
一次性 Cutover
  ↓
Cloud Employee = 唯一帳號主資料
```

Cloud Mode 之後，即使斷網也不切回舊 Local EmployeeStore。Windows 只使用最後成功同步的 Cloud Employee cache + protected offline credential verifier。

因此不存在長期並行的「Local Role」與「Cloud Role」兩套權限系統，也不存在 Pending Y 的暫時 Local 管理員特例。

## 3. Local → Cloud Employee Transition

目前已實作 whole-device transition，不再使用「只處理本機 SUPER_ADMIN」的舊 reconciliation 模型。

身分判定只使用 Employee No + Email：

| Local Employee | Cloud 判定 | 行為 |
| --- | --- | --- |
| Employee No、Email 都不存在 | 新人 | 驗證本人 Email，建立 Cloud Employee |
| Employee No、Email 都命中同一 Employee | 同一人 | 直接採用既有 Cloud Employee 資料 |
| 只有 Employee No 命中 | 衝突 | 交目前 Workspace SUPER_ADMIN 人工確認 |
| 只有 Email 命中 | 衝突 | 交目前 Workspace SUPER_ADMIN 人工確認 |
| Employee No、Email 各命中不同 Employee | 衝突 | 交目前 Workspace SUPER_ADMIN 人工確認 |

姓名只作顯示，不作 identity matching authority。

第一台建立 Workspace 的 X 可成為唯一中央 `SUPER_ADMIN`；其 bootstrap 時已驗證的 Recovery Email 可直接作為 X 的已驗證 Email，不重複寄 OTP。

既有 Workspace 新加入的電腦若有新的 Local SUPER_ADMIN Y，Y 若是新中央 Employee，Cloud role 預設為 `ADMIN`。若 Y 的 Employee No + Email 已精確對應同一既有 Cloud Employee，直接採用該 Employee 既有 Cloud role。

## 4. Device 與 Employee 分離

Workspace、Device、Employee 是三個不同概念：

- Workspace：長期協作邊界。
- Device：可信任電腦。
- Employee：人員帳號／操作權限。

Device Join 使用 Pairing Code；Pairing Code 只授權 Device 加入 Workspace，不授予任何 Employee role。

B 機進入 Device Join 前必須先於本機當下驗證 Local `ADMIN` 或 `SUPER_ADMIN`，證明操作者有權管理 B 機；Workspace 端 Pairing Code 則獨立證明 Workspace 已授權加入。

Device Token：Windows 產生並先以 protected storage 保存；Cloud 只存 hash。Device ID 與 Workspace ID 由 Cloud 產生。

## 5. Cloud Employee 帳號管理

Cloud cutover 後，帳號全域異動全部為 **Online-only**，並於操作當下重新驗證操作者帳密；不能只因為某人開啟了「帳號管理」視窗就持續授權。

目前中央帳號 API／Windows client 已支援：

- 新增 Employee。
- 修改姓名。
- 修改 Email；新 Email 必須先完成 OTP 驗證才 commit。
- `ADMIN ↔ EMPLOYEE`。
- 啟用／停用。
- 本人變更密碼。
- 管理員重設其他非 SUPER_ADMIN 的密碼。
- Employee snapshot 重新同步至本機 cache。

安全限制：

- `SUPER_ADMIN` 不能用一般角色修改流程降級；必須走正式 transfer。
- `SUPER_ADMIN` 不可停用。
- 管理員不能變更自己的 role 或 enabled state。
- `SUPER_ADMIN` 密碼只能由本人變更。
- Windows 不上傳新密碼明文；本機先產生 PBKDF2-SHA256 verifier，再送 Cloud。

## 6. Pending Identity Conflict

若 Local Employee 身分有歧義，Cloud 建立 pending conflict，不自動猜測也不建立重複 Employee。

Account Management 只有存在 unresolved conflict 時才顯示：

```text
待確認帳號 N
```

真正執行 conflict resolution 時重新驗證目前 Workspace `SUPER_ADMIN`；backend 再確認該 Employee 仍是目前唯一 SUPER_ADMIN 後才允許 mapping。

正常精確命中不叫 merge：確認 Employee No + Email 都指向同一中央 Employee 後，直接改採中央資料。

## 7. SUPER_ADMIN Transfer

每個 Workspace 恰好一名中央 `SUPER_ADMIN`。

Transfer contract：

```text
目前 SUPER_ADMIN X
  ↓ execution-time password re-auth
寄 OTP 至 X 目前已驗證 Email
  ↓ OTP success
再次確認 Y 仍為 enabled ADMIN + verified Email
  ↓ atomic transaction
X → ADMIN
Y → SUPER_ADMIN
Workspace Recovery Email → Y verified Email
```

Cloud transfer 不回寫或修改舊 Local role；cutover 後本來就以 Cloud Employee 為唯一帳號 authority。

## 8. 離線行為

Cloud Mode 斷網時仍是 Cloud Mode。

可使用最後一次成功同步的：

- Employee identity。
- Role / Enabled 狀態。
- Protected offline credential verifier。

進行原本需要帳密的本機操作時，仍在執行當下驗證；只是資料來源是最後同步的 Cloud Employee cache。

帳號全域異動不允許離線修改再合併，包括新增／Email／role／enabled／password／SUPER_ADMIN transfer／identity conflict resolution。如此避免多台電腦離線各自修改同一帳號後產生雙主衝突。

完全離線期間不可能得知 Cloud 上剛發生的 role／password／enabled 變更，因此只能使用最後已知狀態；恢復連線後以 Cloud authority 更新 cache。

## 9. OTP / Email

Reference backend 使用 provider-neutral Email abstraction；目前 development reference provider 為 Brevo，並保留 Resend adapter。

OTP foundation：

- Web Crypto 隨機 6 碼。
- HMAC-SHA256 digest at rest；不保存明文 OTP。
- 10 分鐘 TTL。
- 60 秒 resend cooldown。
- 5 次錯誤上限。
- rate limit。
- one-time consumption。
- `OTP_PEPPER`、Email provider API key、sender identity 全部是 runtime secrets，不進 Public repo。

目前尚不得宣稱 live Email delivery 已完成；需在 runtime secrets 設定完成後另做 development deployment 實測。

## 10. Legacy reconciliation 已退役

舊路徑：

```text
POST /v1/employees/reconcile-local
```

原先只處理單一 Local SUPER_ADMIN，並含任意 30 分鐘 import window。此模型與 whole-device transition 定案衝突，已從 Cloud business logic 退役。

Reference Worker 對舊 mutation route fail-closed，回傳 `LEGACY_EMPLOYEE_RECONCILIATION_RETIRED`；正式 Local → Cloud 身分轉換只能走 whole-device Employee Transition。

`GET /v1/employees` 暫保留為 read-only compatibility endpoint；正式 cutover／cache 同步使用 Employee Authority snapshot contract。

## 11. Business boundary

Cloud 是跨裝置協作、中央 Employee、Workspace 管理與後續 Work Item / Audit foundation，不是業務 kill switch。

AMEGO 仍是發票／作廢／折讓官方交易真相。Cloud 故障時，能安全本機執行的既有業務不因中央帳號管理暫時離線而全部停擺；但真正依賴 Cloud 的 Workspace／Device／Employee 全域異動必須等待恢復連線。

## 12. 尚未完成／需實機驗證

- Cloudflare development Worker / remote D1 migration 實際部署與 Schema 7 狀態確認。
- Brevo runtime secrets 完成後的真實 Email OTP delivery test。
- 多台 Windows 實機：A 建 Workspace、B Pairing、whole-device transition、offline cache、恢復同步。
- Device revoke / all-Device-Token-loss recovery。
- 後續 business sync / Work Item / Audit；不得把本文件的 identity foundation 誤認為 V3 全部功能已完成。

正式 merge、tag、Release 仍需明確授權。
