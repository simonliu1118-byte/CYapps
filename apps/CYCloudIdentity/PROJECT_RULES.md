# CYCloud Identity Project Rules

本文件只記錄 **CYCloud Identity** 的專案級永久規則。共通規則依 repository root `REPOSITORY_RULES.md`；repo-specific 規則依 root `REPO_POLICY.md`。

## 1. 產品定位與責任邊界

- CYCloud Identity 是志遠各 Cloud App 共用的 Workspace／Employee／Credential／Workspace Role／Identity Admin capability／Application Access／Session／Email verification／OTP／Recovery 身分權威。
- CYID 回答三件事：使用者是誰、Workspace 層級為何、是否可進入某個 CY App。各 App 的業務資料、模組權限與 domain-specific authorization 不放進 CYID。
- 所有接入 CYID 的 App 直接採用三層 Workspace Role：`SUPER_ADMIN`、`ADMIN`、`USER`；consumer 不得另外建立一套 coarse role projection。
- CY Web 是核心帳號管理 App；首次 Email 驗證、首次正式密碼設定與一般帳號 self-service 都由 CY Web 作主要入口，但 authority 仍屬 CYID。

## 2. Workspace Role、Super Admin 與 Identity Admin

- Employee 身分以 Workspace 為邊界；同一 Employee No 或 Email 可存在於不同 Workspace，但同一 Workspace 內必須唯一。
- Workspace Role 固定為 `SUPER_ADMIN / ADMIN / USER`。
- 每個 active Workspace 恰有一名有效 Super Admin。Super Admin 由受保護的 Workspace authority pointer 決定，不是一般 role update 可產生的普通角色。
- Consumer-facing 名稱使用 **超級管理員（Super Admin）**。只有目前 Super Admin 可透過受保護流程移交 authority；其他 Employee 不得降級、停用、刪除或撤銷 Super Admin。
- `Identity Admin` 是附掛於 `ADMIN` 的 Identity-management capability，不是第四種 Role；目前只有 ADMIN 可持有。
- 只有 Super Admin 可授予或撤銷 Identity Admin。Identity Admin 不得自授、自撤、撤銷其他 Identity Admin capability，也不得直接把另一名 Identity Admin 降為 USER。
- 若未來 HR 需要部分 Identity lifecycle 能力，應新增 narrower capability，而不是現在增加第四種 Workspace Role。

## 3. Employee lifecycle 與 Email 驗證

- 新增 Employee 時直接選擇初始 Role：一般 ADMIN 只能建立 USER；Identity Admin／Super Admin 可建立 USER 或 ADMIN；建立時不得直接授予 Identity Admin。
- 對外產品語言一律稱 **Email 驗證**。不得把首次使用流程另命名為「啟用帳號」，也不在 CY Web 登入頁維護第二個「啟用帳號」入口。
- 建立新 Employee 後，CYID 必須自動寄出第一封 Email 驗證郵件。郵件提供一次性 **首次登入密碼**；首次登入密碼不是正式 Employee password。
- 首次登入密碼必須有明確 expiry、只可用於 CY Web 核心帳號 App 的首次登入流程，且受登入 rate limit 保護。它不得建立一般 Identity session，也不得直接登入 CYACCweb、CYInvoice 或其他 App。
- 使用首次登入密碼驗證成功後，CYID 只可簽發短效、不可作 App authorization 的 first-login ticket，讓使用者設定自己的正式密碼。
- 使用者完成正式密碼設定後，CYID 必須：建立正式 credential、標記 Email 已驗證／完成首次 lifecycle、使首次登入密碼與 first-login ticket 失效；**不得直接建立一般 Identity session**。CY Web 必須回到一般登入頁，要求使用者用剛設定的正式密碼重新登入。
- 首次登入密碼逾期時仍屬 Email 驗證未完成；有權限的管理員可執行 **重寄驗證 Email**。每次重寄都必須產生新的首次登入密碼並立即使舊密碼失效，同時重新計算 expiry。
- 修改尚未完成 Email 驗證 Employee 的 Email 後，驗證流程改以新 Email 為準，舊首次登入密碼立即失效並寄出新的驗證 Email。
- Email provider 寄送失敗不得回滾刪除 Employee；保留 pending Employee、呈現寄送失敗並允許重寄驗證 Email。
- Pending 管理 UI 至少提供 `編輯 / 重寄驗證 Email / 刪除`。只有從未完成第一次 Email 驗證／正式 credential 建立的 Employee 可實體刪除。
- `activated_at` 或等價欄位可作內部 durable lifecycle marker，但不是使用者-facing 流程名稱；後續 Email recovery 不得把已完成首次 lifecycle 的 Employee 退回初次 pending。

## 4. 已啟用帳號、Credential、Session 與 OTP

- 已完成首次 lifecycle 的 Employee 後續只可停用／重新啟用，不得實體刪除，以保留 Audit 與 referential integrity。
- Identity Admin／Super Admin 可對已啟用 Employee 執行管理員強制 Email recovery；一般 ADMIN 不可。
- 強制 Email 變更後帳號仍屬已啟用、正式密碼保留、新 Email 變為待驗證、既有 Session 必須撤銷；不得混同首次 Email 驗證。
- Password 明文、首次登入密碼明文、OTP、raw session token 不得寫入 Git、log、Audit 或 backup metadata。正式 password verifier 與 initial credential verifier 只存在 CYID authority。
- 正式 password 長度固定 8–16 Unicode 字元；所有 consumer App 採同一輸入規則，不自行放寬或縮限。
- Browser session raw token 只在 client cookie／受控 server transport 保存；CYID server 只保存不可逆 hash。Session 必須有 application、workspace、employee、expiry 與 credential-version 邊界。
- Session resolve 不採 sliding-write heartbeat。Employee 停用、Role 變更、credential version 變更、強制 Email recovery、Application Access 失效或 Super Admin transfer 都必須立即反映 server-side authority。
- OTP 仍用於 Workspace bootstrap、password recovery、已啟用 Email re-verification、Super Admin transfer 等用途；OTP 必須 purpose-scoped、single-use、有 expiry、嘗試限制與 resend cooldown。
- Email transport 必須 provider-neutral；Brevo、Resend 或其他 provider 只作 adapter，不改變 Identity contract。

## 5. Application Access 與 consumer contract

- Application registry 必須 generic/data-driven；Public source 不預置正式 Workspace→App 或 Employee→App access matrix。
- CY Web 是核心帳號管理 App：每名有效 Employee 的 CY Web entry access 固定 `TRUE`／不可取消，即使沒有任何 CY Web business Module Access，仍必須能進入 account self-service。
- 除 CY Web 核心 entry 外，新 Employee 的其他 App Access 預設不授予，建立後由 Identity Admin／Super Admin 管理。
- 一般 ADMIN 不得設定任何 App Access。Identity Admin 可設定 USER、ADMIN、其他 Identity Admin 的 App Access，但不得修改自己的 Access 或 Super Admin Access。Super Admin 對 Workspace 已啟用 App 自動有 entry access。
- Role 與 App Access 是獨立維度；Role 變更不得自動新增、刪除或重算既有 Access。
- 所有 consumer 採直接 role projection：`SUPER_ADMIN -> SUPER_ADMIN`、`ADMIN -> ADMIN`、`USER -> USER`。Identity Group／Group Application mapping 不再作 forward authority。
- CY Web 是多模組特例：Customer／Order／Item／Outsourcing／WorkLog 等 Module Access 由 CY Web 保存與 server-side enforcement；Super Admin 全模組自動允許，Identity Admin／Super Admin 管理 eligible Employee，正常 ADMIN 不具 Access-management 權限。
- UI 顯示永遠不是授權來源；App entry 與 module/business authorization 必須由 server 重新確認。

## 6. Super Admin 專屬安全權限

- Workspace Recovery Email 對應目前 Super Admin 已驗證 Email；Super Admin transfer 成功時一併切換。
- Super Admin transfer 必須重新驗證目前 Super Admin credential，並以目前已驗證 Email 完成 OTP，再原子切換 authority 與 Recovery Email。
- Identity Admin grant/revoke、Super Admin transfer、Workspace Recovery、安全核心／OTP policy 等最終控制權操作只允許 Super Admin。
- Identity Admin 不得修改 Super Admin 的 Role、App Access、CY Web Module Access、Email 或 authority pointer。

## 7. Public Source 與部署

- `CYapps` 是 Public repository；source、commit、PR、Actions log、Artifact metadata 都視為外部可見。
- Public source 只保存 schema、generic contract、placeholder、adapter、migration、deployment logic 與 synthetic fixture；production IDs、Employee／Email、實際 access matrix、API key、OTP pepper、credential、session、backup key 等不得 commit。
- Cloud Apps 優先透過 private Service Binding 使用 CYID；browser 不直接取得 provider secret 或 credential verifier。
- Production deploy、正式資料建立、付費方案與不可逆 cutover 必須另有使用者明確同意。

## 8. 成本與資源

- 架構不得預設需要付費 Cloudflare／Email／Backup tier；若容量超出可用額度，先量測與最佳化，再由使用者決定升級。
- Identity 查詢必須有索引；避免 full scan、高頻 polling、session heartbeat writes、無必要 Cron 或其他會快速消耗免費額度的背景工作。

## 9. Consumer contract 與工作線治理

- `docs/CONSUMER_INTEGRATION_STANDARD.md` 是所有 CYID consumer 的唯一 shared technical integration standard；永久遵循義務由本文件與 repository governance 建立。Consumer-specific handoff 不得複製共同規範後形成第二套 Identity authority。
- CYID 以 `CONSUMER_CONTRACT_VERSION` 發布最新 consumer contract revision，以 `CONSUMER_MIN_COMPATIBLE_VERSION` 定義 provider runtime 仍支援的最舊 consumer revision。完成 CYID 接入的 consumer 必須保存自己的 `CYID_CONSUMER_VERSION`。
- CYID product `VERSION` 與 consumer contract version 是不同維度；只有 consumer-visible obligation / endpoint / field / authority semantics 改變時才需要推進 consumer contract version。
- 每個修改 CYID 的 PR 都必須標示 consumer impact：`NONE`、`BACKWARD_COMPATIBLE` 或 `CONSUMER_UPDATE_REQUIRED`。後兩者必須同一工作項目更新 consumer standard、contract version 與 consumer contract changelog。
- `BACKWARD_COMPATIBLE` 變更不得使目前最低相容版本失效；consumer 可在支援版本窗內逐步升級。
- `CONSUMER_UPDATE_REQUIRED` 變更必須先保留可讓既有正式 consumer 運作的 compatibility path，或完成協調 migration；在受影響 consumer 尚未更新前，不得先提高最低相容版本並部署會讓 production consumer 失效的 provider。
- Consumer-specific handoff 只保存 app 現況差異、migration plan、app-specific例外與 acceptance，不保存 shared Role / Session / App Access / first-login / recovery 規格副本。
- 跨 repository consumer 必須依 `CONSUMER_SYNC_MANIFEST.json` 保存 shared contract 的同步鏡像，提供可重現 sync 流程，並在 governance/CI 與 deployment 前逐檔驗證與 CYID `main` canonical bytes 一致；鏡像只讀、不形成新 authority。同一 repository 內 consumer 直接讀 canonical files，不另建重複鏡像。
- Manifest-listed 文件即使只是 documentation-only 修正、未推進 consumer contract version，跨 repo mirror 仍必須同步；consumer contract version 只表示相容語意，不取代文件同步。
- CY Web 是第一個 Shared Identity consumer 與核心帳號管理入口。
- CYAccountingWeb（CYACCweb）與 CYInvoice 採同一 CYID consumer standard，但實際 consumer migration 在各自工作線執行；本專案不直接修改其業務 runtime。
- CYInvoice-specific Device pairing、Device Token、Local→Cloud transition 與 Windows offline credential cache 保留在 CYInvoice 工作線，不反向升格為 CYID 共通規則。