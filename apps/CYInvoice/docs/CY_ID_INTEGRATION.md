# CYInvoice × CY ID 整合設計基準

更新日期：2026-09-29

本文件記錄 CYInvoice 作為 CY ID / CYCloud Identity consumer 時已確認的產品邊界。這是 CYInvoice 端的設計／需求文件，不定義 CY ID／CYWEB 內部 schema、Group 儲存方式、Application Access 資料模型或管理介面實作。

本文件目前只定案架構方向；尚未開始 CYInvoice source 修改、Build 推進、Cloud migration 或正式 cutover。

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

目前實際使用資料沒有已持久化的 `EMPLOYEE` role 需要相容，因此本次 role rename **不需要歷史資料 migration**。後續實作只需同步修正 source、schema/fixture、測試、文件與 UI 名稱。

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

CYInvoice source 後續應把「Cloud Employee 一定存在 CYInvoice 自己的 backend」這個假設從權限驗證邊界抽開，但不需要建立過度通用的 Identity plugin framework。

最低需求是讓 CYInvoice 可以明確分流：

```text
Local authority
Built-in Cloud authority
CY ID authority
```

Built-in 與 CY ID 共用 CYInvoice 的 Device／Workspace／業務層；差別只在 Employee／Credential authority 來源與帳號管理 ownership。

任何正式 source 修改、Build 推進、Cloud migration、CY ID binding 或 production cutover 都需另行規劃與驗收。

## 13. CYID Consumer 接入交接 — 2026-10-10

> **範圍**：本節供 CYInvoice 主控工作線接手，僅整理已確認設計、GitHub source 事實與待決問題。沒有授權 CYInvoice runtime、CYID Provider、D1 Migration、部署、Release 或資料切換。持續性的 shared Identity 語意以 CYID 的 `docs/CONSUMER_INTEGRATION_STANDARD.md` 等 canonical contract 為準；本節不另立規範。

### 13.1 基準、可依賴的既有成果

- 2026-10-10 GitHub `main` source：`VERSION=2.6.10`、`BUILD=2`。既有 `docs/TODO.md` 首段的 V2.6.10 Build 0 是較早 checkpoint；交接以目前版本檔為準。此處沒有重新查證 live Cloudflare 部署或 Windows 實機驗收。
- CYID Provider source `0.3.5`；canonical Consumer Contract `1.0.2`、minimum compatible `1.0.0`。CY Web consumer `1.0.2`，CYACCweb `1.0.1`，皆已用 `IDENTITY` private Service Binding 呼叫 CYID canonical login／resolve／logout。相容版本不表示 CYInvoice 已經完成接入。
- `src/CYInvoice.Core/IdentityProvider.cs` 已有 `AppPrincipal`、`AppRole`、`IIdentityProvider`、`LocalIdentityProvider`、`BuiltInCloudIdentityProvider`、`IdentityProviderRuntime`，目前 ProviderKind 只有 Local 和 BuiltInCloud；**尚無 CYID Provider**。
- Built-in Cloud 線的 online freshness、Device revoke/retire、Cloud→Local crash-safe reset 已有 source 基礎；A/B/C 裝置實機 lifecycle acceptance 仍以 `docs/TODO.md` 未勾選項目為準。勿把 source 完成當成實機通過。
- 現有 `cloud/src/web-auth.ts` 的 `/v1/web-auth/login` 是 **CYInvoice Built-in Cloud** Employee 驗證路徑，不能當成 CYID canonical consumer API；CYID 正式路徑是 `/v1/identity/login`、`/v1/identity/session/resolve`、`/v1/identity/logout`。
- CYInvoice 目前 Cloud Worker / D1 保存自有 Device、Workspace、Built-in Employee 與業務協同資料。接入 CYID 不代表要複製 CYID D1、取消 CYInvoice 業務 D1，或把 CYID Workspace 與 CYInvoice Workspace 合成同一筆。

### 13.2 既定責任與路由

```text
CYInvoice Windows (execution-time Employee authentication; no always-on Employee login)
    -> CYInvoice Cloud Worker (CY_ID mode only)
       -> private IDENTITY Service Binding
          -> CYID Worker
             -> CYID Identity D1

CYInvoice Windows <-> CYInvoice Worker/D1: own Workspace binding, Device Token,
pairing/revoke, sync, business operations, app-local authorization

Local mode: local EmployeeStore only, no CYID requirement
Built-in Cloud / Self-hosted: its own Employee/Credential authority, no CYID dependency
```

CYInvoice 的 Desktop Client **不應直接連 CYID D1**；也不要假設 CYID Worker 有公開網域可供 Desktop 直連。以受管 CYInvoice Worker 作 private Service Binding gateway 為優先架構候選，具體 endpoint/request/authentication/Session lifecycle 留在 CYInvoice 工作線經威脅模型與測試確認；Device Token 不得被誤認為 Employee Session 或 Employee 授權。

CYID canonical response 需嚴格驗證 Workspace／Employee identity、`SUPER_ADMIN|ADMIN|USER`、Identity Admin capability 一致性、Application Access 和版本欄位；其中 `emailVerified`、`isWorkspaceSuperAdmin`、`credentialVersion`、`employeeRevision` 屬目前 shared principal schema。CYInvoice 可將 CYID principal 正規化成既有 `AppPrincipal`，但不可擅自把 CYID Employee ID 視為 CYInvoice Workspace ID 或把 role 當成全部業務授權。

### 13.3 需要主控對話先決定、不得靠相容層猜測的事項

1. **Operation-scoped CYID Session**：CYInvoice 不持續登入員工，但 CYID permanent login 會簽發 opaque app-scoped Session。要如何由受管 Worker 登入、Resolve、在操作完成後可靠 Logout／撤銷，處理 Worker/Windows 逾時、崩潰和撤銷結果不明？必須先決定此生命週期與顯示行為，不得保留長期未管控 Session 或建立 CYInvoice 第二種 Employee authority。
2. **Device + Employee 授權交集**：哪些操作必須同時確認有效 Device Token、CYInvoice Workspace 綁定、CYID app-scoped Employee Access 與 CYInvoice 業務權限？註冊/首次加入/Device recovery 應設計狹窄例外並有可測的 server-side 授權。
3. **Workspace binding & mode selection**：CYID Workspace 與 CYInvoice Workspace 的穩定關聯、錯誤綁定拒絕、選模式/切換/回復條件；不得把測試 Workspace 默認對應正式 Workspace。
4. **Offline auth**：CYID 不把中央 credential verifier 散給 consumer；Windows 本機 offline protected material 的建立、保存、有效期間、可授權操作範圍、reconnect/role/access/password 撤銷同步及 Session 無法在線撤銷時的狀態，必須安全設計並隔離 Built-in Cloud 的 verifier snapshot 模型。
5. **First login／Recovery／帳號管理**：尚未完成 CY Web 首次 Email 驗證的員工不能在 CYInvoice 用一次性首次登入憑證進入一般業務；員工管理/密碼重設/Super Admin Transfer 統一由 CY Web／CYID。CY ID 模式隱藏 CYInvoice 帳號管理；Local、Built-in 不受影響。
6. **舊資料與遷移**：是否要把當前 CYInvoice Workspace 切換到 CY_ID mode；先做不可恢復或未結 Work Item／裝置狀態稽核，不預設大規模 Employee migration，也不以破壞性 Cloud→Local reset 代替受控 Identity authority switch。
7. **基礎設施及驗證**：確認已註冊 CYInvoice Application 與該 Workspace 的 enablement、經核准的 `IDENTITY` production binding、Cloudflare Free D1/Workers 限額與實際用量。現有 CYInvoice 業務 D1 可以保留；小型未來 App 可以共用輕量資料庫，但**不得將 CYID Credential/Session authority 與業務表共庫**。Dev testing 可 local-first，需要跨 Worker 真實驗證時才用隔離 Cloudflare 資源。不要在 Public Git 中寫實際 IDs、tokens 或 secrets。

### 13.4 最小分批實作建議（僅規劃，待主控確認）

1. 先核對目前 CYInvoice 主控 branch、`PROJECT_RULES.md`、待合 PR、真正版本/Deploy baseline 和 Windows Device 驗收狀態；確認既有 `IdentityProviderRuntime` owner 可直接擴充，避免新增平行 authentication handler。
2. 定案上述 operation-scoped Session、Device binding、online/offline 與錯誤狀態後，先建 CYInvoice Worker 的 CYID gateway，以及 `CyIdIdentityProvider` 介面適配（或現有 boundary 的最小必要擴充），**不要修改** Local/Built-in 的 authority 邏輯。
3. 對 CYInvoice 宣告獨立 `CYID_CONSUMER_VERSION`（目前可依實作採用 provider 支援的 1.0.2），Cloudflare configuration 由部署時注入 `IDENTITY`／Application／Workspace binding，不進 Public Git。
4. 先完成 synthetic unit/Worker integration：登入／Resolve／Logout、非法回應拒絕、App Access deny/revoke、Role/Employee disable/password change 即時生效、跨 Workspace/Device、Timeout/CYID outage、ambiguous logout 與無憑證洩漏；再安排 Windows 真機網路失敗、Offline/reconnect、A/B/C Device/Reset、Account Management 隱藏與高權限操作回歸。
5. 依規則採獨立 branch / PR 與必要 Windows CI；正式 CYID Cloud cutover、Migration/Release/Production 部署另行審核，避免將本次交接當成執行許可。

### 13.5 正式參考（先讀 canonical，再讀 CYInvoice 專用設計）

- 共通治理：`/REPOSITORY_RULES.md` → `/REPO_POLICY.md` → `apps/CYInvoice/PROJECT_RULES.md`。
- CYID：`apps/CYCloudIdentity/docs/CONSUMER_INTEGRATION_STANDARD.md`、`AUTH_CONTRACT.md`、`ROLE_AND_ACCESS_MODEL.md`、`ARCHITECTURE.md`，以及 consumer contract 版本檔／changelog。
- CYInvoice：本文件 → `IDENTITY_PROVIDER_REFACTOR_PLAN.md` → `CLOUD_IDENTITY_LIFECYCLE.md` → `TODO.md`；再比對 `IdentityProvider.cs`、Desktop sensitive-operation call-sites、`cloud/src/app.ts`／`employee-authority.ts`／`web-auth.ts`。
- CYID 與 CY Web／CYACCweb source parity 的 read-only 檢查已另由 CYID `cyid/docs-consumer-integration-readiness-20261010` branch 的 PR #378 提出；該 PR 仍待合併，不得把未合併紀錄當成正式 main 基準。
