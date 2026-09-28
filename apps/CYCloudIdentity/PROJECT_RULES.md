# CYCloud Identity Project Rules

本文件只記錄 **CYCloud Identity** 的專案級永久規則。共通規則依 repository root `REPOSITORY_RULES.md`；repo-specific 規則依 root `REPO_POLICY.md`。

## 1. 產品定位

- CYCloud Identity 是志遠各 Cloud App 共用的 Workspace／Employee／Credential／Application Access／Session／Email OTP／Recovery 身分權威。
- 主要 consumer 包含 CY Web、CYAccountingWeb；CYInvoice 後續由其自己的工作線安全轉接，不由本專案直接修改 CYInvoice runtime。
- 各 App 的業務資料、模組權限與 domain-specific authorization 不放進 CYCloud Identity。

## 2. Workspace 與帳號權威

- Employee 身分以 Workspace 為邊界；同一 Employee No 或 Email 可在不同 Workspace 各自存在，但同一 Workspace 內必須唯一。
- 每個 Workspace 必須維持恰好一名啟用中的 `SUPER_ADMIN`；角色只有 `SUPER_ADMIN`、`ADMIN`、`EMPLOYEE`。
- Workspace Recovery Email 必須對應目前 `SUPER_ADMIN` 已驗證的 Email；SUPER_ADMIN 移交完成時 Recovery Email 必須一併切換。
- `SUPER_ADMIN` 移交必須重新驗證目前 SUPER_ADMIN credential，並以目前已驗證 Email 完成 OTP，再以原子操作完成角色與 Recovery Email 切換。

## 3. Credential／Session／OTP

- Password 明文不得儲存、寫 log、進 Git、進 Audit 或進 backup metadata；credential verifier 只能存在 Shared Identity authority。
- Browser session token 只在 client cookie 保存原值；server 只保存不可逆 hash。Session 必須有 application、workspace、employee 與 expiry 邊界。
- 一般 session resolve 不採 sliding-write heartbeat；避免無意義 D1 writes。Logout、停用、credential version 變更與 application access 失效必須可使 session 失效。
- OTP 必須 purpose-scoped、single-use、有 expiry、錯誤次數限制與 resend cooldown；不同 purpose 的 OTP 不得互相重放。
- Email transport 必須 provider-neutral。Brevo、Resend 或未來 provider 都只能作寄送 adapter，不得改變 Identity API contract。

## 4. Application Access

- Shared Identity 只決定「這個 Employee 能否進入某個 App」；App 內的細部 module/business permission 仍由該 App 自己管理。
- Workspace `SUPER_ADMIN` 不得因 application grant 誤刪而失去該 Workspace 已啟用 App 的管理入口；一般 `ADMIN`／`EMPLOYEE` 使用明確 application access grant。
- application access、employee enabled、role、credential version 都是 server-side authority；前端顯示不是權限來源。

## 5. Public Source 與部署

- Public source 只保存 schema、generic contract、placeholder、adapter 與 deployment logic；Cloudflare resource ID、Workspace ID、Employee 資料、Email、API Key、OTP pepper、backup key 與其他 secrets 不得 commit。
- CY Web／CYAccountingWeb 優先透過 private Service Binding 使用 Identity；browser 不直接取得 provider secret 或 credential verifier。
- Production deploy、正式資料建立、付費方案啟用與任何不可逆 cutover 都必須另有明確使用者同意。

## 6. 成本與資源

- 架構不得默認需要付費 Cloudflare、Email 或 Backup tier 才能正常運作；若未來容量超出目前可用免費額度，先量測、最佳化並回報，再由使用者決定是否升級。
- Identity 查詢必須有索引；避免 full scan、高頻 polling、session heartbeat writes、無意義 Cron 或會快速消耗免費額度的背景工作。

## 7. CYInvoice 邊界

- 現階段只參考 CYInvoice 已確認的 Workspace／Employee／Credential／SUPER_ADMIN／Email OTP／Recovery 行為建立獨立 authority。
- 不修改 CYInvoice Cloud 現有 source/runtime、D1 或 Device pairing 流程；CYInvoice 的 Device／Local→Cloud transition 保留在 CYInvoice 工作線，直到後續明確轉移。
- CYInvoice 後續轉接時，以 CYCloud Identity 的穩定 contract 為目標，不把 CYInvoice-specific Device lifecycle 反向寫成所有 Cloud App 的共同規則。
