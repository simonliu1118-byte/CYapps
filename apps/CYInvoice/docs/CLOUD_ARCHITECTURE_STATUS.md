# CYInvoice Cloud Architecture Status

此文件只描述目前已實作／已驗證的雲端工程狀態；長期產品藍圖見 `CLOUD_ROADMAP.md`，Workspace／Device／SUPER_ADMIN 身分生命週期定案見 `CLOUD_IDENTITY_LIFECYCLE.md`。

> 本 repository 為 Public repository。本文不得包含任何實際 Cloud 帳號、私人 endpoint、密鑰、Token、OTP、正式公司資料、真實 Email 或其他營運／個資資訊。

## 1. 目前產品與工程基準

- CYInvoice Windows 正式產品線：C#／WinForms。
- 目前工程版本：V2.6.3 Build 0。
- Cloud backend：Cloudflare Worker + D1 reference implementation。
- Windows Client：provider-neutral，只接受使用者設定的 CYInvoice-compatible HTTPS endpoint。
- 預設模式：`local_only`。
- Cloud schema：3（`0001` + `0002` + `0003_workspace_recovery_email_otp.sql`）。
- 已執行 migration 不回寫；後續 schema 一律使用 forward migration。

CYInvoice 永久支援兩種模式：

1. 單機模式（Local Only）。
2. 雲端模式（Cloud Enabled / Cloud Preferred）。

單機模式是完整產品模式；Cloud 是可選的協作層，不是發票業務總開關。

## 2. 資料責任

```text
AMEGO        = 電子發票／作廢／折讓官方交易真相
Local SQLite = 單機運作、快取、fallback
Cloud API    = 可選的跨裝置協作介面
Backend DB   = 由 Cloud API 實作者自行決定
```

AMEGO App Key、本機 Device Token 等敏感憑證不進 Public repository；Cloud 不保存不必要的完整發票鏡像／PDF Cache。

## 3. 已完成／已驗證

- Cloud API health／version／storage compatibility。
- D1 `workspaces`、`devices`、`device_pairing_codes` foundation schema。
- D1 Workspace Recovery Email + `email_otp_challenges` schema foundation。
- 每台 Device 獨立 token-hash foundation。
- Windows Cloud API client、HTTPS-only 驗證與 bounded timeout。
- Windows Device Token protected-storage abstraction。
- Pending Device Token／onboarding state protected persistence foundation。
- Local Only／Cloud Preferred 設定與 Cloud 異常 fallback 顯示。
- Windows → development Worker → D1 live connection 已驗證。
- development D1 尚未建立正式 Workspace。
- Foundation bootstrap／device／pairing endpoints 與 .NET contract tests 已建立。
- Provider-neutral Email transport boundary 已建立，reference Worker 含 Brevo／Resend adapters。
- 目前 development reference Email Provider 定為 Brevo；實際 API Key 尚待帳號手機驗證後設定，因此尚未做 live Email delivery test。
- first-bootstrap Email OTP foundation 已實作：6 碼、HMAC-SHA256、10 分鐘有效、5 次錯誤上限、60 秒重寄冷卻、每 Email／purpose 每小時 5 次 challenge 上限、一次性消耗。
- first Workspace bootstrap 現在要求有效 Email challenge + OTP，並把已驗證 Email 寫入 Workspace Recovery Email。
- bootstrap retry 仍以同一 Pending Device Token 先復原既有 Cloud Workspace／Device，避免 lost response 造成第二套 identity。
- Public Windows client 不內建 project-owner Cloud endpoint。

## 4. 第一個 Workspace + 第一台 Device contract

已定案並已進入 Core／Worker contract 的 bootstrap model：

```text
Workspace ID → Cloud 產生
Device ID    → Cloud 產生
Device Token → Windows 產生
```

Windows 在送出 bootstrap 前先把 Device Token 以 DPAPI 保存成 Pending；Cloud 只保存 Token hash。

第一次 Workspace 另增加 Email authorization：

```text
既有 Local SUPER_ADMIN
        ↓ 本機先驗證本人
沿用其既有 Email
        ↓
POST /v1/onboarding/bootstrap-email
        ↓
6 碼 OTP
        ↓
POST /v1/bootstrap
        ↓
建立 Workspace + 第一台 Device
並綁定 verified Recovery Email
```

使用者不需要再輸入第二個 Cloud Email。Windows 正式 UI 尚未接上這套 contract。

## 5. OTP 與 Email 安全邊界

- OTP 由 Web Crypto 產生，不使用 `Math.random()`。
- D1 不保存明文 OTP，只保存 `HMAC-SHA256(OTP_PEPPER, challengeId:otp)`。
- `OTP_PEPPER` 必須是 Worker Secret，不進 repo／log／artifact。
- OTP Email Provider Key 同樣只存在 Worker runtime secret。
- Provider delivery failure 不記錄 provider response body、收件 Email、OTP 或信件本文。
- Recovery Email 是 Workspace 必要私有後端資料，但不得進 public log／artifact。
- OTP challenge 在 Workspace 建立成功時一併標記 consumed。

## 6. 已定案但尚未全部實作的身分流程

- 第一次從單機版建立 Workspace：沿用既有 Local SUPER_ADMIN Email，不重新輸入；完成既有超管驗證 + Email OTP 後才初始化。
- Workspace 建立後不因 Token 遺失、Windows 重灌或程式重新下載而重建。
- 已有 Workspace、沒有本機 Device identity：使用 Pairing Code 或 Workspace 已登記 SUPER_ADMIN Email OTP 加入。
- 新機自己的 Local SUPER_ADMIN 不得自行授權加入既有 Workspace。
- 既有單機 B 機合法加入後，原唯一 Local SUPER_ADMIN Y 自動成為 Workspace ADMIN，可立即工作。
- Workspace 永遠只有一名 SUPER_ADMIN。
- 超管換人使用原子 `TransferSuperAdmin`：原超管降 ADMIN、指定 ADMIN 升 SUPER_ADMIN。
- Pairing Code 正式版必須在有效 Device + SUPER_ADMIN／指定 ADMIN 人員驗證 + OTP 成功後才產生；B 機輸入已授權 Pairing Code 後不再重做 OTP。
- 所有 Device Token + 原 Recovery Email 同時失效時，以 Cloudflare／reference backend 管理端人工修改 Recovery Email 作最終維運救援，不增加 Windows emergency secret。

完整流程與安全邊界見 `CLOUD_IDENTITY_LIFECYCLE.md`。

## 7. Public Repo 與 Provider-neutral 邊界

CYInvoice Windows client：

- 不直接連 D1、PostgreSQL、SQL Server 或其他資料庫。
- 不內建專案擁有者私人 Cloud endpoint。
- 只連使用者設定的 CYInvoice-compatible HTTPS API。
- 不要求一般使用者理解 Cloudflare／D1 等底層技術。

Cloudflare Worker + D1 只是目前 reference backend。未來第三方可使用其他技術，只要符合 Cloud API contract。

Email Provider 同樣屬於 backend implementation detail：目前 reference deployment 使用 Brevo；未來有自有網域時可切到 Resend，Windows 與 OTP contract 不變。

## 8. Cloud Preferred 與 fallback

Cloud 健康時，用於跨裝置協調、中央身分、Work Item 與 Audit。

Cloud 暫時不可用時，安全的既有本機業務仍可繼續；只有真正依賴 Cloud 的管理功能停止，例如：

- 新 Device 註冊／配對／撤銷。
- 中央帳號／角色變更。
- SUPER_ADMIN 移交。
- Workspace 管理。

不採「所有敏感業務都必須先拿 Cloud Lock」的 blanket lock 設計。

## 9. 帳號與裝置是不同概念

正式模型必須區分：

- Employee / User：誰正在執行操作。
- Device：哪一台可信任電腦正在執行操作。

預定角色：

- `SUPER_ADMIN`：每個 Workspace 恰好一名。
- `ADMIN`：可多人。
- `EMPLOYEE`：可多人。

Local SUPER_ADMIN 只代表原單機系統最高管理者，不代表可以自行取得既有 Workspace 管理權。

## 10. 目前仍屬 prototype、不得誤認為正式 V3.0 行為

- `POST /v1/device-pairings` 目前仍只依有效 Device Token 即可發配對碼；正式版尚需人員授權／OTP gate。
- `POST /v1/device-pairings/claim` 目前 foundation 仍由 Cloud 產生 Device Token；後續 Device Join batch 需收斂成與正式生命週期一致的 Windows-generated Token。
- 尚無中央 Employee／Role／single-SUPER_ADMIN Cloud schema。
- Bootstrap Email OTP backend／Core contract 已有，但尚未接正式 WinForms onboarding UI。
- Brevo runtime API Key 尚未設定，因此尚未做 live Email delivery test。
- 尚無 Device revoke／recovery 正式 UI。
- 尚無 Work Item／Audit／跨機同步正式實作。

## 11. 已定案不進 Cloud 的內容

- AMEGO App Key。
- 完整發票資料鏡像。
- PDF Cache。
- 本機 runtime backup／export data。
- 不必要的 AMEGO response payload。
- 明文 Device Token／OTP／密碼／單機 Recovery Code。

## 12. 下一步工程順序

依 `CLOUD_IDENTITY_LIFECYCLE.md` 分批：

1. Windows 首次 Workspace onboarding UI：驗證既有 Local SUPER_ADMIN、取用既有 Email、發 OTP、輸入 OTP、提交 bootstrap。
2. 完成 UI 的 timeout／restart recovery 與 Pending Token／challenge state 銜接。
3. Brevo 手機驗證恢復後，設定 runtime Secret 並做 live Email transport／OTP 測試。
4. Device Join／Recovery。
5. 中央 Employee／單一 SUPER_ADMIN／超管移交。
6. Pairing Code 前的人員授權。
7. 再進入 Work Item／Audit／多機 OrderID／正式折讓 API。

多公司、`company_id`、跨公司權限不在 V3.0 當前工程範圍。
