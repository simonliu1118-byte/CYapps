# CYInvoice × CY ID 整合設計基準

更新日期：2026-10-09（原設計基準：2026-09-29）

本文件記錄 CYInvoice 作為 CY ID / CYCloud Identity consumer 時已確認的產品邊界。這是 CYInvoice 端的設計／需求文件，不定義 CY ID／CYWEB 內部 schema、Group 儲存方式、Application Access 資料模型或管理介面實作。

Local／Built-in provider、role migration、freshness、Device revoke 與 reset 已實作並合併；CYID adapter／binding／offline／0-Device recovery 尚未接線。目前工程版為 V2.6.14，最新停點見 [現行交接](CLOUD_WORK_HANDOFF.md)，未完成工作只在 [TODO.md](TODO.md) 追蹤。

CYID 共通語意的唯一來源為 [Consumer Integration Standard](../../CYCloudIdentity/docs/CONSUMER_INTEGRATION_STANDARD.md)、CONSUMER_CONTRACT_VERSION（1.0.2）、CONSUMER_MIN_COMPATIBLE_VERSION（1.0.0）與 CONSUMER_SYNC_MANIFEST.json。同 repo 直接讀 canonical package，本文件只描述 CYInvoice 的 desktop／Device／offline 差異，不複製或重新定義 shared contract。

## 1. 產品目標

CYInvoice 必須同時保留三種正式使用方式：

1. **Local**：單機使用，不需要 Cloud Workspace，也不依賴 CY ID。
2. **Built-in Cloud / Self-hosted**：使用 CYInvoice 自己的 Workspace、Employee authority、Credential、Device 與權限庫。第三方公司可依 User Manual 在自己的 Cloud 帳號建立自己的 CYInvoice Cloud，不需要 CY ID。
3. **CY ID Cloud**：CYInvoice 保留自己的 Workspace／Device／業務協同層，但 Employee／Credential authority 改由 CY ID 提供。

Built-in Cloud 是正式一等模式，不是 CY ID 不可用時的 fallback；CY ID 也不是取代整個 CYInvoice Cloud。

## 2. Workspace 邊界

CY ID Workspace 與 CYInvoice Workspace 是不同層級的實體，不得因名稱相同而合併為同一個資料實體。

```text
CY ID Workspace
= 組織／Employee／Credential／共通身分範圍

CYInvoice Workspace
= CYInvoice Device／配對／同步／Work Item／業務協同範圍
```

### Built-in Cloud

```text
CYInvoice Workspace
├─ Built-in Employee authority
├─ SUPER_ADMIN / ADMIN / USER
├─ Credential / Email OTP
├─ Device / Device Token
└─ CYInvoice business coordination
```

不需要任何 CY ID Workspace。

### CY ID Cloud

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

CYInvoice Workspace 必須保存自己的穩定 identity；與 CY ID Workspace 以 binding 關聯，不要求兩者共用同一個 Workspace ID。

目前志遠可採一個 CY ID Workspace 對一個 CYInvoice Workspace，但資料模型不應依賴「兩種 Workspace 永遠就是同一筆資料」這個假設。

## 3. Employee authority 模式

CYInvoice Cloud Workspace 在任何時刻只能有一套有效 Employee authority，不能同時讓 Built-in 與 CY ID 都成為帳號真相來源。

```text
identity_mode = BUILTIN
```

或：

```text
identity_mode = CY_ID
identity_workspace_binding = <CY ID Workspace identity>
```

切換／轉換期間可以有受控 transition state，但完成後必須收斂成單一 authority。

不需要為未確定的 Entra ID、LDAP、Google Workspace 或任意第三方 Identity Provider 預先建立通用插件框架。CYInvoice 第一階段只需要支援 Built-in 與 CY ID；其他人若 fork 後需要不同 provider，可自行擴充。

## 4. 權限模型統一

CYInvoice 正式 role 名稱統一為：

```text
SUPER_ADMIN
ADMIN
USER
```

不再使用 `EMPLOYEE` 作為 role 名稱。Employee 是帳號／人員實體；`USER` 才是一般使用者的權限等級。

CY ID 對 CYInvoice 的權限直接 1:1 對應：

```text
CY ID SUPER_ADMIN → CYInvoice SUPER_ADMIN
CY ID ADMIN       → CYInvoice ADMIN
CY ID USER        → CYInvoice USER
```

`SUPER_ADMIN` 仍然是 `SUPER_ADMIN`，不降階、不使用「視同 ADMIN」等轉換。

Role rename 已完成，Local employees／Cloud cache 的一次性 schema migration 與 Cloud forward migration 0010 均已實作；active runtime 使用 USER，不保留 EMPLOYEE alias。

中文 UI 建議保持：

- `SUPER_ADMIN` → 超級管理員
- `ADMIN` → 管理員
- `USER` → 一般使用者

## 5. CY ID 模式的帳號管理 UI

CY ID 模式下，CYInvoice 的「帳號管理」功能直接隱藏，不保留一套唯讀／半可用的帳號 CRUD UI。

帳號相關操作統一由 CYWEB / CY ID 帳號中心處理，包括但不限於：

- Employee 建立／停用；
- 姓名／Email；
- 密碼／忘記密碼；
- Email OTP；
- SUPER_ADMIN 管理／移交；
- CYInvoice 使用權與 CYInvoice role 指派。

CYInvoice 只消費目前有效的 Employee identity 與 CYInvoice role，不在 CY ID 模式建立第二套帳號管理 authority。

Local 與 Built-in Cloud 模式仍保留 CYInvoice 自己的帳號管理功能。

## 6. Application Access 邊界

Application Access 是 CY ID 的上層入口控制，負責判斷某 Employee 是否可使用 CYInvoice。

CYInvoice 不提供第二套 App Access 管理介面，也不需要了解 CY ID 內部如何以 direct grant、Group 或其他方式算出有效 access。

對 CYInvoice 而言，CY ID 驗證結果只需能回答：

- 操作者的穩定 Employee identity；
- Employee 是否 enabled；
- 是否具有 CYInvoice access；
- CYInvoice role：`SUPER_ADMIN` / `ADMIN` / `USER`。

若 CY ID 判定沒有 CYInvoice App Access，應在 Identity／授權邊界拒絕；CYInvoice 不再維護一份重複的 App Access 清單。

CY ID 的 Group 設計、Application Access schema、CYWEB 帳號中心 UI 與管理流程不在本工作線決定。

## 7. CYInvoice 永遠自行管理的範圍

無論使用 Built-in Identity 或 CY ID，下列內容都屬於 CYInvoice，而不是 CY ID：

- CYInvoice Workspace；
- Device identity；
- Pairing Code / Invitation；
- Device Token；
- Device revoke / retire；
- Cloud → Local destructive reset；
- AMEGO 設定與官方結果；
- Invoice Sync；
- Work Item；
- CYInvoice business audit / coordination；
- Windows Cloud Offline 行為與本機 cache 邊界。

Device identity 與 Employee identity 必須持續分離。CY ID credential 成功不等於 Device Token；Device Token 也不等於 Employee 管理權限。

## 8. Self-hosted / 第三方公司情境

第三方公司（例如 ZZ 公司）必須可以完全不使用 CY ID，依 CYInvoice User Manual 自行部署：

```text
ZZ Cloud Account
├─ CYInvoice Worker
├─ CYInvoice Database
├─ Email Provider
└─ CYInvoice Workspace
    ├─ ZZ SUPER_ADMIN
    ├─ ZZ ADMIN / USER
    └─ ZZ Devices
```

其 Workspace、Employee、Credential、權限與 Device 都屬於自己的 deployment。CYInvoice 不應因加入 CY ID 整合而移除目前 Built-in Cloud Employee authority。

## 9. Local 情境

Local Mode 維持最小依賴：

```text
CYInvoice Local
└─ Local EmployeeStore
```

不建立 Cloud Workspace、不需要 CY ID、不需要外部 Identity service。

## 10. Migration 暫不列核心工作

目前 CYInvoice development Workspace 實際營運資料量很低，其他同仁仍主要使用舊單機版本；現階段不為 Built-in → CY ID 建立複雜的正式帳號 migration framework。

AMEGO 仍是發票官方真相，CYInvoice 重新接回正式光貿後可依既有兩期同步策略重建發票資料。

真正 cutover 前仍需做一次 acceptance check，確認沒有只能存在本機、無法由 AMEGO 兩期同步重建的 pending／結果不明／特殊本機狀態；這是切換前驗收，不是現在要建立的通用 migration 系統。

## 11. Windows Offline 整合

CY ID 模式仍必須保留 CYInvoice 已定案的 Cloud Offline 使用能力；但 CYInvoice 不應直接讀 CY ID D1，也不應要求 CY ID 把自己的 credential verifier 當成一般 consumer data 回傳。

CY ID 模式下 Windows offline credential/cache 的最終協定屬後續實作階段技術設計項目。產品不變量只有：

- Online 時以目前 CY ID authority 為準；
- Offline 時只能使用最後一次可信同步／建立的本機 protected cache；
- reconnect 後最新 CY ID authority 必須重新生效；
- 不得因此產生 Built-in + CY ID 雙 authority。

本節目前不提前鎖死實作 API。

## 12. 實作原則

CYInvoice 已以 IIdentityProvider／AppPrincipal／AppRole 抽開 Local／Built-in authority；後續 CYID 接入延用此邊界，不建立過度通用的 Identity plugin framework。

最低需求是讓 CYInvoice 可以明確分流：

```text
Local authority
Built-in Cloud authority
CY ID authority
```

Built-in 與 CY ID 共用 CYInvoice 的 Device／Workspace／業務層；差別只在 Employee／Credential authority 來源與帳號管理 ownership。

任何正式 source 修改、Build 推進、Cloud migration、CY ID binding 或 production cutover 都需另行規劃與驗收。
