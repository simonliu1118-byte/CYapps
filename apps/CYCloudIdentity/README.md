# CYCloud Identity

CYCloud Identity 是志遠 Cloud App 共用的身分服務。它從 CYInvoice Cloud 已驗證的帳號模型中抽出共用能力，但不修改 CYInvoice 現行 runtime。

目前目標 consumer：

- CY Web
- CYAccountingWeb
- CYInvoice（後續由 CYInvoice 工作線另行切換）

## Shared authority

CYCloud Identity 負責：

- Workspace
- Employee
- Credential
- Shared role：`SUPER_ADMIN` / `ADMIN` / `EMPLOYEE`
- Application Access
- Browser Session
- Email OTP
- Employee password recovery
- Workspace Recovery Email
- SUPER_ADMIN transfer
- Identity audit

各 App 自己負責自己的業務資料與細部權限。

## Current extraction principle

第一階段不搬 CYInvoice production data，也不修改 CYInvoice source/runtime。以 CYInvoice 已確認的帳號與 OTP 行為為 reference，建立乾淨的新 Identity D1／Worker。

CYInvoice-specific Device pairing、Device Token、Local→Cloud Employee transition 不在第一階段抽出；它們留在 CYInvoice 工作線，後續再透過穩定 Identity contract 接上。

## Cost mode

目前以 Cloudflare／Brevo／Google Cloud 可用免費額度為前提設計。程式不得自動啟用付費方案。具體 quota 與部署狀態記錄於 `TODO.md`／部署文件，不在本 README 寫死為永久數字。

## Documentation

- `PROJECT_RULES.md` — project-level permanent rules
- `docs/ARCHITECTURE.md` — current extraction architecture / lifecycle
- `TODO.md` — current implementation status and next sequence
- `migrations/` — forward D1 schema source of truth
