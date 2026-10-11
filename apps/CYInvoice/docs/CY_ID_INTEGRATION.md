# CYInvoice × CY ID 整合設計基準

更新日期：2026-10-10（原設計基準：2026-09-29）

本文件記錄 CYInvoice 作為 CY ID / CYCloud Identity consumer 時已確認的產品邊界。這是 CYInvoice 端的設計／需求文件，不定義 CY ID／CYWEB 內部 schema、Group 儲存方式、Application Access 資料模型或管理介面實作。

Local／Built-in foundation 已合併，V2.6.14 介面與 Device metadata 仍在 PR #216。V2.6.15 已實作 CYID adapter／binding／offline consumer，尚未部署或正式切換；0-Device recovery 尚未實作。最新停點見 [現行交接](CLOUD_WORK_HANDOFF.md)，未完成工作只在 [TODO.md](TODO.md) 追蹤。

CYID 共通語意的唯一來源為 [Consumer Integration Standard](../../CYCloudIdentity/docs/CONSUMER_INTEGRATION_STANDARD.md)、CONSUMER_CONTRACT_VERSION（1.0.3）、CONSUMER_MIN_COMPATIBLE_VERSION（1.0.0）與 CONSUMER_SYNC_MANIFEST.json。同 repo 直接讀 canonical package，本文件只描述 CYInvoice 的 desktop／Device／offline 差異，不複製或重新定義 shared contract。

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

V2.6.15 已實作 Windows offline credential/cache；V2.6.16 依使用者定案銜接原有降級及光貿／雲端獨立連線檢查；技術邊界、last-trusted 限制及 acceptance 見 §14。原產品不變量保留：

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

## 14. V2.6.15 Consumer 實作與切換邊界

2026-10-10 使用者「可以開始做」授權本輪 consumer 開發；第 13 節保留 PR #379 文件交接時的歷史狀態，以本節及 CLOUD_WORK_HANDOFF 為目前實作停點。Source 基準整合 PR #216 head 1e6d4137、main 94f559cd 與 PR #379 文件；不將前置 PR 未合併／實機待驗項目宣告完成。

| 設計項目 | 本輪實作 | 權威／限制 |
| --- | --- | --- |
| 執行時驗證 | CyIdIdentityProvider → authenticated Worker gateway → canonical Login／Resolve／finally Logout | 不持續登入，不把 Session 傳給 Windows；原 workflow 最後驗證仍經 provider |
| Private transport | CYInvoice Worker 的 IDENTITY Fetcher | 只呼叫 canonical CYID endpoints，不直接連 CYID D1；runtime 配置另行核准 |
| 授權交集 | 有效 Device／CYInvoice Workspace ＋ CYID Employee／Workspace／App Access ＋ CYInvoice operation Role | Device Token、配對碼不代表 Employee 或 App Access；CYID Identity Admin 不是 CYInvoice Super Admin |
| Workspace | runtime identity Workspace 與 CYInvoice Workspace 分別固定 | gateway 只接受 configured consumer Workspace，不由 client 傳入／猜測綁定 |
| Offline／reconnect | 線上成功後的 device-bound DPAPI cache，雲端 transport／timeout／暫時 5xx 且光貿正常時驗本機 proof | 線上拒絕優先；重新連線使用新的 Role／App Access／credential，不讀 Local authority |
| 遷移／切換 | 0013 保留 Built-in 邀請歷史並新增外部 actor 欄位；CYID 預設關閉 | 不匯入 CYID verifier，不 bulk migrate Employee，不自動刪 Built-in 或切正式服務 |

### 14.1 Runtime owner 與最小相容責任

IdentityProviderRuntime 是唯一桌面選擇 owner。SettingsStore 保存 authenticated discovery 的整筆 protected binding（endpoint、兩個 Workspace、Device、Application、Consumer Version、Device Token digest）；CyIdIdentityProvider 是 CYID 桌面驗證 owner。app.ts 是唯一 Worker dispatch，cyid.ts 只管理 private Identity lifecycle，原 Device／onboarding handlers 保留唯一業務 mutation owner；未新增 handler 重入、observer、持續登入、mutation retry 或第二個權限庫。

CYID enabled 後舊 Employee／Web auth／bootstrap authority routes 明確拒絕，Windows 帳號管理隱藏、密碼復原提示 CY Web。舊 Worker 的 discovery 404 僅供尚未確認 CYID 的 Built-in client 相容；已確認 CYID 的 device 不自動降級。這個 rolling-deployment 相容 reader 不另存 authority，由 CYInvoice consumer owner 維護；待 Built-in 最低受支援 Worker 全部提供 discovery 後可移除 404 reader，以 legacy Worker 與 provider-mismatch regression 驗證退場。

新裝置 invitation preview／claim 使用現行 CYID Super Admin 帳密；pairing claim 保留已簽發的一次性 code，不增加員工欄位，它只加入 Device，後續每次受保護業務仍驗 CYID。Verified Email 從目前 authorized CYID admin snapshot 取該 Super Admin 一列，只為原 Email OTP／邀請寄送，不持久化 provider snapshot／verifier。

V2.6.17 的人工結案／折讓與 Built-in 中央帳號 mutation dialog 只收帳密，由既有 core／server 作最終完整 Device＋Employee 驗證。必要的開窗／讀取與後續 mutation 是不同授權階段，OTP 多階段保留每次 server 檢查；不宣稱整個 UI 流程只會呼叫一次，也不保存跨操作 Session。

### 14.2 Architecture Exception：使用者指定既有離線行為

Primary path 是當次 CYID online authority；2026-10-10 使用者定案：**沿用原 CYInvoice 降級單機功能**。Windows→Cloud transport failure、非使用者取消的 timeout，以及暫時 5xx（含 Worker→private CYID 故障的 503），在光貿可連線時使用已線上成功驗證的最後可信快取。不是重新建立 Local 員工／第二套降級 authority，不把 CloudMode、Workspace 或 Token 切成另一個模式。

| 光貿 | 雲端驗證 | 執行行為 |
| --- | --- | --- |
| 可用 | 可用 | 正常使用；權限操作採當次最新 authority |
| 可用 | 暫時不可用 | 使用原有降級；中央帳號／Device 異動等待雲端恢復 |
| 不可用 | 可用 | 停止光貿相關開票、查詢、作廢、折讓確認、PDF、同步；雲端帳號／Device 功能依原授權可用 |
| 不可用 | 不可用 | 一個 modal 提示阻擋全部業務；僅重新檢查／關閉程式可用；任一恢復後回到對應狀態 |

純 Local 沒有雲端依賴：光貿可用則正常，光貿不可用則使用同一阻擋方式，直到光貿恢復。MainForm 是唯一連線 UI／15 秒恢復檢查 lifecycle owner；ServiceConnectivity 是共用狀態／探測 owner，光貿檢查收斂到現有 AmegoConnectivityProbe 的 `/json/time`，不是只看網卡。正式 business service 在請求前重查光貿；cloud liveness 不代表 Employee／App Access 授權。CYID authenticated discovery 經原 private Binding 呼叫 canonical `/v1/health`；健康檢查不建立 Session、不讀 CYID D1，每次權限操作仍經 provider。

由線上成功的密碼在本機建立隨機 salt／PBKDF2 proof，連 principal、Role、revision、credentialVersion、完整 scope 一起 DPAPI 保護；不是 CYID 中央 verifier，不 mint server Session，不授權離線 Worker 管理。Built-in 沿用原 protected Cloud Employee cache；CYID 沿用 V2.6.15 已實作的裝置快取。

帳密錯誤、權限拒絕、裝置撤銷不能降級；HTTP 401／403、配置／scope 不一致、成功回應畸形與 caller cancellation 都 fail closed。拒絕依原 cache owner 清除相應快取；裝置／provider／Workspace 不符清除全部身分快取。已知 Device 拒絕後又遇 503 不能重新放行，須同一裝置／綁定成功驗證才能解除。發票、原 Workspace、Device／Token、設定及使用者輸入保留；不因短暫 outage 重建或 reset。

Last-trusted 沒有新增 TTL，降級期間無法即時觀察中央撤銷；權限仍受最後 principal 的 app-local Role 限制，server mutations 仍須在線。重新連線後每次重新驗證，最新 Role／App Access／credential 替換成功快取。光貿斷線不能在本機完成開票；恢復不自動重送，既有結果不明仍先查詢，不新增離線配號或待連線自動開票佇列。CYInvoice provider owner 維護本能力，scope 為目前 Windows 裝置；未來替換 server-approved offline policy 時以 outage／reconnect／deny／scope regression 驗證，不當作暫時雙 authority。

### 14.3 Session 與一致性限制

finally 呼叫 Logout；Logout 失敗只留下不含憑證的 CYID_LOGOUT_UNCONFIRMED，不重做業務、不假稱 CYID 已撤銷。若 Worker crash／Login response loss，provider Session 仍可能有效到 canonical expiry（目前 default 8 小時），由 CYID 負責 expiry／revocation；consumer 沒有 durable token 可恢復清理。這輪不修改 provider timeout/minimum 或建第二個 Session store。

每次 mutation 前會再 Resolve current CYID principal，consumer D1 仍在同一 transaction 檢查 active Device／Workspace／target 並寫 audit；CYID 與業務 D1 分屬服務，Resolve 和 mutation 不是跨庫 atomic transaction。Role／credential／App Access 的 mutation 前競態及被撤銷 Device 回歸已覆蓋可觀測拒絕，不承諾跨服務零時間差。

### 14.4 驗證與正式切換 gate

本機使用 repository 真實 CYID Worker、獨立 synthetic DB／migrations 測 Login／Resolve／Logout、Role／App Access／enabled／credential change、錯 scope、first-login、provider outage、logout loss 不 replay，以及 pairing／invitation／rename／revoke；C# 測整筆 protected cache、transport／timeout／503／HTML 503 降級、四種服務狀態、純 Local、拒絕後 outage 不復活、cancel、scope、Unicode 邊界。Windows CI 執行真實 DPAPI、加入控件 smoke、原本完整 business regressions 與封裝；Windows 阻擋 modal／不能關閉略過／重新檢查恢復 smoke 隨本輪 CI 驗證；實機依 RC_TEST AA／AB 尚待驗收，125／150 DPI 維持 Deferred。

實際部署／正式 CYID 切換前仍需：核對已註冊 Application 與 Workspace enablement／App Access、核准 private binding 與兩 Workspace 配對、備份／migration 0013 與 FK check、EmployeeNo 及歷史業務 actor 稽核、A/B/C device／offline reconnect 驗收、server rollback 與已確認 CYID client 的 fail-closed 邊界。先做隔離 staging，不將 source bundle／CI 視為 live 成功。0-active-Device recovery 未完成，LAST_ACTIVE_DEVICE 保護維持；未授權 production cutover、正式 tag 或 Release。V2.6.17 CYID Consumer Impact: BACKWARD_COMPATIBLE；provider source 0.3.6 與 canonical 1.0.3 新增 optional private invalidation，最低相容 1.0.0 保留。

### 14.5 原 Workspace 接續使用（2026-10-10 使用者明確要求）

切換 CYID 只替換 Employee identity authority，沿用既有 CYInvoice Workspace、Device ID、Device Token、業務資料與本機設定。CYID Workspace 是外部身分 scope，與既有 CYInvoice Workspace 建立綁定，不取代業務 Workspace、不搬到新 D1，也不要求現有裝置重新加入、重新設定或 destructive reset。

SettingsStore 在更新 provider 前核對既有 endpoint／CYInvoice Workspace／Device／Token digest；不同 Workspace 的 discovery 拒絕，不能覆寫原設定。回歸以真實本機 SQLite／SettingsStore 切換後重開，核對發票及 pending 狀態、買方名稱、PDF cache、公司／印表機／protected credentials；Worker 以原兩台 Device Token 在 flag 前後 discovery／authenticate，核對 Workspace、Devices、歷史員工／邀請／audit rows 完整保留。這是 synthetic source 證據，真實 A/B/C 接續使用仍待 RC AA。

正式切換前先在原安裝升級所有使用中的 Windows 到支援 CYID 的版本（目前 V2.6.17），備妥員工啟用、原 EmployeeNo 對應、Application Access、所需 Role 與 verified Super Admin Email，再在隔離 staging 證明原裝置可完成既有業務。任一條件未完成，維持現行 Built-in 部署，不開啟 CYID flag；不可先切 authority 再要求使用者重建 Workspace 修復。受控啟用後原安裝重新 discovery／驗證，以當次 CYID 結果更新權限；不因拒絕而回退舊 authority，也不承諾服務故障期間所有遠端操作可用。

### 14.6 裝置／權限同步與撤銷自動重設（V2.6.17 source，人工／staging 待驗收）

本節保存 CYInvoice 差異；shared private invalidation contract 只讀 canonical standard §11.1（1.0.3／minimum 1.0.0，BACKWARD_COMPATIBLE）。待辦唯一來源 TODO §7.3.2，人工驗收 RC_TEST AC；V2.6.16 CI 不代表本版完成。

- 無中央推播。已綁定裝置啟動立即、使用中每 60 秒、雲端重連立即同步；每輪 Windows → CYInvoice Worker 只發一個合併 Device／Workspace binding／Provider 權限變更請求。Worker 內部可查多個服務，分項判斷故障。
- 重用 self-status；已撤銷 Token 只可讀自身最小撤銷證據，不可讀員工／業務資料。裝置撤銷不能被 active-only gate、CYID outage 或員工驗證失敗掩蓋。
- CYID 須先正式提供 private service-level change contract；現有 Login／Resolve／Logout／admin snapshot 不是無 Session feed。背景不存帳密、不反覆登入、不讀 CYID D1、不 export verifier。Device Token 不是 Employee／App Access authority；Built-in 一併收斂，Local 保留。
- 變更涵蓋 Employee enabled／刪除、Workspace／Application 狀態、App Access、Role、credentialVersion 及漏同步。受影響 cache 失效；不把新 principal 接到舊密碼 proof，新的成功線上員工驗證才重建可信離線 entry。Wire contract／版本／Consumer Impact 由 canonical Provider 管理。
- 最終操作一次確認 Device＋原 Workspace＋Employee credentials＋current App Access／Role，再執行 app-local 業務授權。收斂 dialog／core 重複，保留真正執行前檢查；不沿用開窗時的舊授權或跨操作 Session。
- MainForm 為 lifecycle owner，ServiceConnectivity／Provider 為既有狀態與授權 owner。60 秒不改光貿發票同步頻率；15 秒連線偵測不每輪重跑完整 feed。去重並發同步，防舊回應覆蓋新權限或復活 known deny／revoke。
- 已綁定啟動檢查不能被空 Employee cache／EnsureInitialSetup 阻斷；pending reset 優先恢復，未綁定走原初始化。雲端不可達仍依 §14.2 原降級，離線不能即時觀察中央異動，不新增 TTL／authority。
- Automatic wipe 僅限明確同一 Device／Workspace 撤銷；一般 401／403、密碼／員工拒絕、503、scope mismatch、單純 Workspace disabled 不觸發。手動退出原規則不暗改；切換 CYID 仍沿用 §14.5，不 reset 原資料。
- 確認撤銷先阻擋新操作並由 LocalResetCoordinator 持久化 authorized marker，再停止全部背景寫入、關閉資料存取、清除及重啟。marker 不能等正常退出後才寫；kill／失敗／部分清除後下次啟動續清，完成前不開放業務。
- 使用者明定 **Logs 也全部清除，整個 CYInvoice 可攜資料夾回到發行包首次使用狀態**。盤點 root／各子目錄全部 runtime 檔案，含 Data、Cache、Logs、設定、憑證、身分 proof／binding、發票、PDF／暫存；保留原程式及發行必要檔案。marker 最後移除，不刪 folder 外使用者檔案，不只刪三目錄就宣稱完整。
- 已送出 AMEGO request 不能宣稱取消或自動重送；核對既有結果不明查核及其他有效裝置／重新設定後查詢途徑，不為此保留 revoked-device 業務資料或新增離線 queue。正式部署／切換／tag／Release 未授權。

V2.6.17 implementation：ServiceConnectivity 共用 checkGate 與 monotonic 60 秒節流；CYID background invalidation／操作時 proof 更新共用原 cache gate，Builtin revision 防舊快照覆寫。worker 在 provider 故障後仍重新確認 Device，撤銷結果優先；缺欄位／scope mismatch／configuration invalid 不降級。每個 portable directory 的 Program process gate 防其他程序在清除期間寫回，新程序等待原程序退出後直接恢复 authorized marker，不先開 repository。package manifest 只保存原始發行檔案；清除失敗維持 marker 並阻擋業務，缺 manifest 也不猜測刪除。正常授權升版保留 scope 與原資料；1.0.2/1.0.3 reader 是 canonical 支援版本窗，非第二套 authority。

60 秒刷新不對 CYID 建立／刷新 Session。每裝置持續開啟一天約 1,440 次 permission refresh，15 秒 liveness 約 5,760 次 aggregate request（完整 sync 已包含其中）；只讀 D1 查詢，實際 requests/rows/延遲及免費額度待 staging 量測。離線期間无法得知中央異動，不能承諾即時撤銷。系統 temp 的既有 PDF preview finally 清理不屬 portable reset 掃描；不得擴大刪除外部共享 temp 或使用者 exports。

Rolling upgrade 順序：在原 CYInvoice 業務 D1／Workspace 保留且 CYID flag 關閉時，先升級 additive Cloud 0.9.2（包含 /v1/runtime/sync），再升級原 Windows 至 V2.6.17；完成 private RPC 與原裝置 staging 後才核准切 CYID。舊 Worker 404 明確提示版本未提供同步，保留原既有降級，不清除／不換 Workspace；不能把這種舊 Gateway 當作已完成新同步驗收。0.9.2 Cloud rollout 與 CYID 0.3.6 named entrypoint 都仍待部署授權。
