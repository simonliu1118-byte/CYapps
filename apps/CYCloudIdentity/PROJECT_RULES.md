# CYCloud Identity Project Rules

本文件只記錄 **CYCloud Identity** 的專案級永久規則。共通規則依 repository root `REPOSITORY_RULES.md`；repo-specific 規則依 root `REPO_POLICY.md`。

## 1. 產品定位

- CYCloud Identity 是志遠各 Cloud App 共用的 Workspace／Employee／Credential／Application Access／Session／Email OTP／Recovery 身分權威。
- 各 App 的業務資料、模組權限與 domain-specific authorization 不放進 CYCloud Identity。
- CYInvoice 後續若轉接 CYCloud Identity，必須由 CYInvoice 自己的工作線安全切換；本專案不得直接修改 CYInvoice runtime／Device lifecycle。

## 2. Workspace、最高管理權與身分組

- Employee 身分以 Workspace 為邊界；同一 Employee No 或 Email 可在不同 Workspace 各自存在，但同一 Workspace 內必須唯一。
- 每個啟用中的 Workspace 必須維持恰好一名有效的最高管理者（SUPER_ADMIN authority）。此最高管理權是 Workspace 安全不變量，不得依賴可任意刪改的普通身分組名稱來判斷。
- Workspace Recovery Email 必須對應目前最高管理者已驗證的 Email；最高管理權移交完成時 Recovery Email 必須一併切換。
- 最高管理權移交必須重新驗證目前最高管理者 credential，並以目前已驗證 Email 完成 OTP，再以原子操作完成 authority 與 Recovery Email 切換。
- 一般「身分組／Role Group」必須資料驅動並保留擴充能力；未來應可新增、重新命名、停用或調整身分組，而不需要修改 schema 或重新部署程式。
- 不得以 `CHECK role IN (...)`、固定 enum 或其他 schema-level 封死方式，將普通身分組永久限制為某幾個名稱。
- 普通身分組與 Workspace 最高管理權必須分離：最高管理權有保護性系統語意；其餘群組可依實際管理需要演進。
- 各 App 內的細部權限仍由各 App 自己管理；CYCloud Identity 的身分組不得演變成所有 App 共用的細部 permission 清單。

## 3. Credential／Session／OTP

- Password 明文不得儲存、寫 log、進 Git、進 Audit 或進 backup metadata；credential verifier 只能存在 Shared Identity authority。
- Browser session token 只在 client cookie 保存原值；server 只保存不可逆 hash。Session 必須有 application、workspace、employee 與 expiry 邊界。
- 一般 session resolve 不採 sliding-write heartbeat；避免無意義 D1 writes。Logout、停用、credential version 變更與 application access 失效必須可使 session 失效。
- OTP 必須 purpose-scoped、single-use、有 expiry、錯誤次數限制與 resend cooldown；不同 purpose 的 OTP 不得互相重放。
- Email transport 必須 provider-neutral。Brevo、Resend 或未來 provider 都只能作寄送 adapter，不得改變 Identity API contract。

## 4. Application Access

- Shared Identity 只決定「這個 Employee 是否可進入某個已註冊 App」；App 內的細部 module/business permission 仍由該 App 自己管理。
- Application registry 必須是 generic/data-driven contract；不得因目前有哪些系統就把實際 App 清單永久寫死在 schema。
- Public migration、fixture、source 不預置志遠目前實際啟用的 App catalog、Workspace→App 啟用矩陣或 Employee→App access 清單；這些屬 deployment/runtime data，除非未來使用者另行明確決定公開。
- App 名稱本身不一定是 secret，但仍採資料最小化原則：Public source 只保存必要的 generic contract，不因方便而暴露不需要的實際營運配置。
- Workspace 最高管理者不得因 application grant 誤刪而失去該 Workspace 已啟用 App 的管理入口；其他 Employee 使用明確 application access grant 或後續核准的群組式 grant。
- application access、employee enabled、credential version、最高管理 authority 與身分組成員關係都是 server-side authority；前端顯示不是權限來源。

## 5. Public Source 與部署

- `CYapps` 是 Public repository；所有 source、commit、PR、Actions log、Artifact metadata 都必須視為外部可見。
- Public source 只保存 schema、generic contract、placeholder、adapter 與 deployment logic；Cloudflare resource ID、Workspace ID、Employee 資料、Email、實際 App access matrix、API Key、OTP pepper、backup key 與其他 secrets／營運配置不得 commit。
- 所有正式資源識別、provider target、secret、初始 Workspace／Employee／Application runtime data 必須由受控 Deployment Environment、secret store 或正式管理流程注入。
- Cloud Apps 優先透過 private Service Binding 使用 Identity；browser 不直接取得 provider secret 或 credential verifier。
- Production deploy、正式資料建立、付費方案啟用與任何不可逆 cutover 都必須另有明確使用者同意。

## 6. 成本與資源

- 架構不得默認需要付費 Cloudflare、Email 或 Backup tier 才能正常運作；若未來容量超出目前可用免費額度，先量測、最佳化並回報，再由使用者決定是否升級。
- Identity 查詢必須有索引；避免 full scan、高頻 polling、session heartbeat writes、無意義 Cron 或會快速消耗免費額度的背景工作。

## 7. CYInvoice 邊界

- 現階段只參考 CYInvoice 已確認的 Workspace／Employee／Credential／最高管理權／Email OTP／Recovery 行為建立獨立 authority。
- 不修改 CYInvoice Cloud 現有 source/runtime、D1 或 Device pairing 流程；CYInvoice 的 Device／Local→Cloud transition 保留在 CYInvoice 工作線，直到後續明確轉移。
- CYInvoice 後續轉接時，以 CYCloud Identity 的穩定 contract 為目標，不把 CYInvoice-specific Device lifecycle 反向寫成所有 Cloud App 的共同規則。
