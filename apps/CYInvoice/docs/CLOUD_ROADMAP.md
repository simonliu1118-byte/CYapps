# CYInvoice 雲端版長期藍圖與上線路線

本文件整理 CYInvoice 從單機版進入多機雲端協調後的產品邊界、資料責任與分階段順序。

目前核心策略：**V3.0 先解決志遠高雄單一公司／單一統編的多機協同，不提前實作尚未發生的多公司／SaaS 需求；底層則保留未來插入 Company 層的空間。**

Workspace／Device／Employee／SUPER_ADMIN／Offline 的定案生命週期見 `CLOUD_IDENTITY_LIFECYCLE.md`；已完成工程狀態見 `CLOUD_ARCHITECTURE_STATUS.md`。

## 1. V3.0 產品目標

V3.0 目標：

> 讓同一 Workspace 內的多台 CYInvoice 電腦，共用中央 Employee、Device、待辦、同步與稽核協調，同時在 Cloud 暫時不可用時保留安全的既有業務能力。

V3.0 不做：

- 多公司新增／刪除／切換 UI。
- `company_id` 全面導入。
- 跨公司 Employee scope。
- 跨公司查詢／待辦／報表。
- 多家公司 AMEGO 設定中央管理。

Workspace ID 不得等於統編。

## 2. 長期資料層級

V3.0：

```text
Workspace
├─ Employee
├─ Device
├─ Work Item
└─ Audit / coordination metadata
```

未來真正需要不同統編公司時才新增：

```text
Workspace
├─ Company：高雄
├─ Company：台北
└─ Company：台中
```

舊 V3 單公司資料可在升級 migration 中自動歸到第一個 Company，不需要現在提前把 Company 做進所有資料表與 UI。

## 3. Provider-neutral 邊界

Windows Client 只依賴 CYInvoice-compatible HTTPS API：

```text
CYInvoice Windows
      │ HTTPS
      ▼
CYInvoice-compatible Cloud API
      │
      ├─ Cloudflare Worker + D1
      ├─ ASP.NET + PostgreSQL
      ├─ 自架 backend
      └─ 其他相容實作
```

Cloudflare Worker + D1 是 reference implementation，不是 Windows 必要執行環境。

AMEGO App Key 不上 Cloud；各 Windows 電腦仍依現有安全儲存方式管理 AMEGO credential。

## 4. Workspace / Device / Employee

### Workspace

Workspace 是協作與管理邊界，不是統編、AMEGO 帳號或某一家 Cloud provider 的 tenant 名稱。

Workspace 建立後長期存在，不因 Device Token 遺失、Windows 重灌或程式重新下載而重建。

### Device

每台電腦有獨立 Device ID / Device Token。

- Device ID：Cloud 產生。
- Device Token：Windows 產生並在送出 request 前先安全保存。
- Cloud 只保存 Token hash。

Pairing Code 只授權 Device 加入 Workspace，不授予 Employee role。

### Employee

Employee 是人的身分。一台 Device 可由多人操作，同一 Employee 也可在多台 Device 執行授權操作。

CYInvoice 維持 **per-operation authentication**，不改成程式啟動時登入並持續保留 session。

Workspace 恰好一名 `SUPER_ADMIN`；`ADMIN`、`EMPLOYEE` 可多人。

## 5. Local → Cloud 帳號路線

單機版：

```text
Local EmployeeStore = 唯一 authority
```

加入 Cloud 後不是立刻建立第二套 Cloud role，而是進入一次性 whole-device transition：

```text
Device 加入 Workspace
  ↓
CloudTransition
  ↓
盤點全部 Local Employees
  ↓
Identity matching / Email verification / Credential / Conflict resolution
  ↓
Cutover
  ↓
Cloud Employee = 唯一 authority
```

Identity matching：

- Employee No + Email 都不存在 → 新 Employee，先驗證本人 Email。
- Employee No + Email 都命中同一 Employee → 直接採用既有 Cloud Employee。
- 任一 partial / divergent match → SUPER_ADMIN 人工確認。

姓名不作 identity matching authority。

舊的單一 Local SUPER_ADMIN reconciliation 與任意 import time window 已退役。

## 6. Cloud Mode / Offline

Cloud cutover 後，網路故障時**不是降回舊 Local Mode**。

正確狀態：

```text
Cloud Mode
├─ Online  → Cloud authority
└─ Offline → last-synced Cloud Employee cache
```

Offline cache 可保存：

- Employee identity。
- role / enabled。
- credential version。
- protected offline credential verifier。

原本安全可本機完成的業務不因 Cloud 掛掉而 blanket lock；但 Workspace-wide account mutations 必須 Online，包括：

- 新增／修改 Employee。
- Email / role / enabled / password。
- identity conflict resolution。
- SUPER_ADMIN transfer。
- Device / Workspace 管理。

如此避免產生 A、B 兩台離線各自改同一 Employee 後再嘗試 merge 的雙主模型。

## 7. 中央 Employee 管理

Cloud Employee account management 採 execution-time credential verification。

目前 V3 contract：

- 新 Employee：管理員帳密 re-auth + 新 Employee 自己的 Email OTP。
- 修改 Email：管理員 re-auth + 新 Email OTP，成功前舊 Email 不變。
- role：`ADMIN ↔ EMPLOYEE`，不可自改 role。
- enabled：不可自停用；SUPER_ADMIN 不可停用。
- password：本人可改自己；管理員可重設其他非 SUPER_ADMIN；SUPER_ADMIN 密碼只能本人改。
- `SUPER_ADMIN` 不得由一般 role update 建立或移除。

密碼明文不上 Cloud；Windows 先產生 PBKDF2 verifier，再經 HTTPS 更新中央 credential。

## 8. SUPER_ADMIN Transfer

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

## 9. Cloud 最低必要資料

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

## 10. AMEGO 資料責任

AMEGO 仍是發票／作廢／折讓官方結果唯一準則。

Cloud：

- 不自行宣告 AMEGO 官方成功。
- 不保存 AMEGO App Key。
- 不把完整 invoice / PDF cache 無差別鏡像到中央 DB。
- 可以保存跨機必要的 work item、idempotency、結果不明、audit metadata。

## 11. V3 後續實作順序

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

## 12. 上線前必要驗證

- Remote Worker / D1 Schema 7 實際狀態。
- Brevo runtime secrets + live OTP。
- A 機建立 Workspace。
- B 機 Pairing。
- 多 Local Employee transition matrix。
- conflict resolution。
- central Employee CRUD / password / Email OTP。
- SUPER_ADMIN transfer。
- Offline credential cache + reconnect refresh。
- Device loss / revoke / recovery。
- Windows x64 build / startup smoke / contract / engineering package。
- Public repo safety check。

GitHub CI 綠燈不等於 remote deployment 已完成。

任何 merge、tag、正式 Release 都需要明確授權。
