# CYCloud Identity Project Rules

本文件只記錄 **CYCloud Identity** 的專案級永久規則。共通規則依 repository root `REPOSITORY_RULES.md`；repo-specific 規則依 root `REPO_POLICY.md`。

## 1. 產品定位

- CYCloud Identity 是志遠各 Cloud App 共用的 Workspace／Employee／Credential／Workspace Role／Application Access／Session／Email OTP／Recovery 身分權威。
- CYCloud Identity 的責任是確認「使用者是誰、Workspace 身分層級為何、是否可進入某個 CY App」；各 App 的業務資料、模組權限與 domain-specific authorization 不放進 CYCloud Identity。
- 目前所有接入 CYCloud Identity 的 CY App 都直接採用 CYID 的三層 Workspace Role：`SUPER_ADMIN`、`ADMIN`、`USER`；不得由 consumer App 另行重建一套不同的 Super Admin／Admin／User 對應。
- CYInvoice 後續若轉接 CYCloud Identity，必須由 CYInvoice 自己的工作線安全切換；本專案不得直接修改 CYInvoice runtime／Device lifecycle。

## 2. Workspace Role、Super Admin 與 Identity Admin

- Employee 身分以 Workspace 為邊界；同一 Employee No 或 Email 可在不同 Workspace 各自存在，但同一 Workspace 內必須唯一。
- Workspace 身分層級固定收斂為三層：`SUPER_ADMIN`、`ADMIN`、`USER`。
- 每個啟用中的 Workspace 必須維持恰好一名有效的 `SUPER_ADMIN`。Super Admin 是受保護的 Workspace authority pointer，不是可由一般 role update 產生的普通 Employee role；實作可維持 `super_admin_employee_id`／`isWorkspaceSuperAdmin` 等穩定 protocol 欄位。
- Consumer-facing 名稱使用 **超級管理員（Super Admin）**。Super Admin 只能由目前 Super Admin 主動完成受保護的移交流程，不得由其他 Employee 降級、停用、刪除或撤銷其最終控制權。
- 一般 Employee 的持久角色只需要 `ADMIN`／`USER`；有效角色計算時，若 Employee 是 Workspace authority pointer 指向的 Employee，則有效角色為 `SUPER_ADMIN`。
- `Identity Admin` 是附掛於 `ADMIN` 的特殊 Identity-management capability，**不是第四種 Workspace Role**。目前只有 `ADMIN` 可持有此 capability。
- 只有 Super Admin 可以授予或撤銷 `Identity Admin` capability；Identity Admin 不得自行取得、撤銷自己或其他 Identity Admin 的該 capability，也不得把另一名 Identity Admin 降為 USER。
- 未來若公司出現專職 HR／人資角色，可再把部分 Identity lifecycle 能力 capability 化，允許非 ADMIN Employee 持有受限 Identity 管理能力；此為 future/deferred direction，目前不得因此新增第四種 Role 或提前實作複雜 permission catalog。

## 3. Employee lifecycle 與管理邊界

- 新增 Employee 時直接指定初始 Role，不要求先建立 USER 再另外升級。
- 一般 `ADMIN` 只能新增 `USER`；`Identity Admin` 與 `SUPER_ADMIN` 可以直接新增 `USER` 或 `ADMIN`。新增 Employee 時不得直接授予 `Identity Admin` capability。
- 一般 ADMIN 可處理一般 USER 的日常帳號 lifecycle：建立 USER、編輯尚未驗證 USER 的資料、重寄 Email 驗證、刪除尚未完成第一次驗證／登入的 USER、停用／重新啟用已啟用 USER。一般 ADMIN 不具任何 App Access／CY Web Module Access 設定能力。
- Identity Admin 除上述能力外，可執行 `USER ↔ ADMIN`、管理非 Super Admin Employee 的 Application Access，以及執行已啟用 Employee 的管理員強制 Email recovery。Super Admin 擁有同等能力並保留最終控制權。
- Identity Admin 可以管理其他 Identity Admin 的 Application Access，但不得修改自己的 Application Access；自己的 Access 必須由另一名 Identity Admin 或 Super Admin 調整。
- Role 升降不得自動新增、刪除或重算既有 Application Access；Role 與 Access 是兩個獨立維度。
- 若一名 Identity Admin 要降為 USER，必須先由 Super Admin 撤銷其 `Identity Admin` capability，再完成 Role 降級；目前不得存在 `USER + Identity Admin` 組合。
- 只有從未完成第一次 Email 驗證／正式密碼設定的 Employee 可以真正刪除。任何曾完成啟用的 Employee 後續離職或停權都只能停用，不得實體刪除，以保留 Audit 與歷史 referential integrity。

## 4. 第一次 Email 驗證、Email recovery、Credential／Session／OTP

- 管理員建立尚未驗證 Employee 成功後，CYCloud Identity 必須主動發送 Email 驗證信；不得要求管理員另外通知使用者自行尋找啟用入口。
- 第一次 Email 驗證信不使用 activation link／activation OTP。信件提供 **4 碼使用者帳號 + 8 位亂數一次性預設密碼**；使用者直接到 CY Web 一般登入頁輸入帳號與該一次性預設密碼。
- 一次性預設密碼驗證成功後，CYID 只能核發短效且不可當作 Session 使用的 first-login ticket；在使用者設定正式密碼前，不得建立一般 Identity Session、不得取得其他 App／CY Web Module 使用權。
- 使用者必須立即設定新的 8–16 字元正式密碼。完成後才一次寫入 Email 已驗證、第一次啟用時間與 enabled 狀態，刪除／失效一次性預設 credential，並建立正常 Identity Session 進入 CY Web。
- 尚未完成第一次登入的管理 UI 狀態使用 **尚未驗證**；操作至少提供 **編輯／重寄 Email 驗證／刪除**。`重寄 Email 驗證` 必須產生新的 8 位一次性預設密碼，舊的一次性預設密碼及其尚未完成的 first-login ticket 必須失效。
- 修改尚未驗證 Employee 的帳號或 Email 時，若需要重新通知使用者，必須針對最新資料產生並寄送新的 Email 驗證資料。
- Email provider 暫時寄送失敗時，已成功建立的 Employee 不回滾刪除；應保存尚未驗證 Employee、回報寄送失敗狀態並允許管理員重寄。
- 一次性預設密碼明文只允許在產生後到 Email transport 的瞬時記憶體路徑存在；不得寫入 D1、Git、Audit、log、Actions output、API response 或 backup metadata。D1 只保存 verifier/hash。正式密碼不得透過 Email 傳送。
- 已啟用 Employee 的 Email 若失效、被停權或不可使用，`Identity Admin`／`SUPER_ADMIN` 可執行管理員強制 Email 變更。普通 ADMIN 不可執行此操作。
- 強制 Email 變更後，Employee 帳號仍屬已啟用帳號、原密碼保留，新 Email 變為待驗證，既有 Session 必須撤銷；不得把帳號錯誤退回第一次 `尚未驗證` 狀態。新 Email 可重寄驗證信。
- Password／credential 明文不得儲存、寫 log、進 Git、進 Audit 或進 backup metadata；credential verifier 只能存在 Shared Identity authority。
- 正式 Password 長度固定為 8–16 字元，所有 consumer App 必須遵循 CYCloud Identity 的同一驗證規則，不得自行放寬或縮限。
- Browser session token 只在 client cookie 保存原值；server 只保存不可逆 hash。Session 必須有 application、workspace、employee 與 expiry 邊界。
- 一般 session resolve 不採 sliding-write heartbeat；避免無意義 D1 writes。
- Employee 停用、Role 變更、credential version 變更、管理員強制 Email 變更與 Application Access 失效都必須立即反映 server-side authority；相關既有 Session 必須撤銷或在下一次 resolve/request 失效，不能依賴舊前端畫面繼續授權。
- OTP 仍適用於忘記密碼、已啟用帳號 Email 驗證、Super Admin 移交等需要 OTP 的 purpose；OTP 必須 purpose-scoped、single-use、有 expiry、錯誤次數限制與 resend cooldown，不同 purpose 不得互相重放。第一次新帳號驗證不再要求 activation OTP。
- Email transport 必須 provider-neutral。Brevo、Resend 或未來 provider 都只能作寄送 adapter，不得改變 Identity API contract。

## 5. Application Access 與 consumer role contract

- Shared Identity 只決定 Employee 是否可進入某個已註冊 App；App 內的細部 module/business permission 仍由各 App 自己管理。
- Application registry 必須是 generic/data-driven contract；Public migration／fixture／source 不預置正式 Workspace→App 或 Employee→App access matrix。
- **CY Web 是目前指定的核心帳號管理 App**：每一名有效 Employee 的 CY Web entry access 必須視為固定 `TRUE`／不可取消，即使該 Employee 沒有任何 CY Web 業務模組權限，也必須能登入 CY Web 管理自己的帳號。
- 除 CY Web 核心 entry access 外，新 Employee 的其他 App Access 預設為未授予，建立後再由 Identity Admin／Super Admin 調整。
- 一般 ADMIN 不得設定任何 Employee 的 Application Access。
- Identity Admin 可設定 USER、ADMIN 及其他 Identity Admin 的 Application Access，但不能修改自己的 Access，也不能修改 Super Admin 的 Access。
- Super Admin 對 Workspace 已啟用的所有 CY App 自動具有 entry access，且不可被一般 grant 誤刪或取消。
- 所有接入 CYID 的 CY App 暫時採一對一 role projection：CYID `SUPER_ADMIN → App SUPER_ADMIN`、`ADMIN → App ADMIN`、`USER → App USER`。不再以 Identity Group／Group Application mapping 產生另一套 App-specific coarse role。
- 對一般單體 App，ADMIN 只要有該 App Access，即視為該 App 的 Admin；更細的 App 內 permission 目前主要針對 USER，由 consumer App 自己管理。
- CY Web 是多模組特例：CYID 只保證 CY Web 核心 entry access；Customer／Order／Item／Outsourcing／WorkLog 等 Module Access 由 CY Web 自己保存與 server-side enforcement。Super Admin 對所有 CY Web Module 自動允許；Identity Admin／Super Admin 可管理 USER、ADMIN 與其他 Identity Admin 的 CY Web Module Access，Identity Admin 不得修改自己的 Module Access。ADMIN 有某 Module Access 時，在該 Module 內具有完整管理權。
- Application Access 與 CY Web Module Access 的前端顯示都不是權限來源；server-side authority 必須在 session resolve 或受保護 request 時重新確認。

## 6. Security authority reserved to Super Admin

- Workspace Recovery Email 必須對應目前 Super Admin 已驗證的 Email；Super Admin 移交完成時 Recovery Email 必須一併切換。
- Super Admin 移交必須重新驗證目前 Super Admin credential，並以目前已驗證 Email 完成 OTP，再以原子操作完成 authority 與 Recovery Email 切換。
- `Identity Admin` capability 的授予／撤銷、Super Admin 移交、Workspace Recovery、安全核心／OTP policy 等會影響 Workspace 最終控制權的操作只允許 Super Admin。
- Identity Admin 不得修改 Super Admin 的 Role、Application Access、CY Web Module Access、Email 或 authority pointer。

## 7. Public Source 與部署

- `CYapps` 是 Public repository；所有 source、commit、PR、Actions log、Artifact metadata 都必須視為外部可見。
- Public source 只保存 schema、generic contract、placeholder、adapter 與 deployment logic；Cloudflare resource ID、Workspace ID、Employee 資料、Email、實際 App access matrix、API Key、OTP pepper、backup key 與其他 secrets／營運配置不得 commit。
- 所有正式資源識別、provider target、secret、初始 Workspace／Employee／Application runtime data 必須由受控 Deployment Environment、secret store 或正式管理流程注入。
- Cloud Apps 優先透過 private Service Binding 使用 Identity；browser 不直接取得 provider secret 或 credential verifier。
- Production deploy、正式資料建立、付費方案啟用與任何不可逆 cutover 都必須另有明確使用者同意。

## 8. 成本與資源

- 架構不得默認需要付費 Cloudflare、Email 或 Backup tier 才能正常運作；若未來容量超出目前可用免費額度，先量測、最佳化並回報，再由使用者決定是否升級。
- Identity 查詢必須有索引；避免 full scan、高頻 polling、session heartbeat writes、無意義 Cron 或會快速消耗免費額度的背景工作。

## 9. CYInvoice 邊界

- 現階段只參考 CYInvoice 已確認的 Workspace／Employee／Credential／Super Admin／Email OTP／Recovery 行為建立獨立 authority。
- 不修改 CYInvoice Cloud 現有 source/runtime、D1 或 Device pairing 流程；CYInvoice 的 Device／Local→Cloud transition 保留在 CYInvoice 工作線，直到後續明確轉移。
- CYInvoice 後續轉接時，以 CYCloud Identity 的穩定三層 Role + Application Access contract 為目標，不把 CYInvoice-specific Device lifecycle 反向寫成所有 Cloud App 的共同規則。
