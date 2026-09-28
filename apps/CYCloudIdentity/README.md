# CYCloud Identity

CYCloud Identity 是志遠 Cloud App 共用的身分服務。它從既有已驗證帳號行為中抽出共用能力，但第一階段不修改 CYInvoice 現行 runtime。

## Shared authority

CYCloud Identity 負責：

- Workspace
- Employee
- Credential
- Workspace 最高管理 authority
- 可新增／修改／停用的 Identity Groups（身分組）
- Generic Application Registry / Application Access
- Browser Session
- Email OTP
- Employee password recovery
- Workspace Recovery Email
- 最高管理 authority transfer
- Identity audit

各 App 自己負責自己的業務資料與細部權限。

## Public repository boundary

`CYapps` 是 Public repository。CYCloud Identity 的 Public source 只保存 generic schema、contract、placeholder、adapter、deployment logic 與合成測試資料。

實際啟用哪些 App、哪些 Workspace／Employee 可進哪些 App、正式 Workspace／Employee／Email、Cloudflare resource ID、provider target、API key、OTP pepper、backup key 等，都不得當成 Public source 或 migration seed 提交。

`applications` 是 runtime registry，Public migration 預設為空；正式 App catalog 由受控部署／管理流程建立。

## Current extraction principle

第一階段不搬 CYInvoice production data，也不修改 CYInvoice source/runtime。以已確認的 Workspace／Employee／Credential／Email OTP／Recovery 行為為 reference，建立乾淨的新 Identity D1／Worker。

CYInvoice-specific Device pairing、Device Token、Local→Cloud Employee transition 不在第一階段抽出；它們留在 CYInvoice 工作線，後續再透過穩定 Identity contract 接上。

## Cost mode

目前以 Cloudflare／Email provider／Google Cloud 可用免費額度為前提設計。程式不得自動啟用付費方案。具體 quota 與部署狀態記錄於 `TODO.md`／部署文件，不在本 README 寫死為永久數字。

## Documentation

- `PROJECT_RULES.md` — project-level permanent rules
- `docs/ARCHITECTURE.md` — current extraction architecture / lifecycle
- `docs/AUTH_CONTRACT.md` — executable login/session consumer contract
- `docs/APPLICATION_ROLE_MAPPING.md` — approved coarse per-Application role mapping design, including CYInvoice USER/ADMIN compatibility and protected SUPER_ADMIN semantics
- `TODO.md` — current implementation status and next sequence
- `migrations/` — forward D1 schema source of truth
