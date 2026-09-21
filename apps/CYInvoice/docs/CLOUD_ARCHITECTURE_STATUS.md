# CYInvoice Cloud Architecture Status

此文件只描述目前已實作／已驗證的雲端工程狀態；長期產品藍圖見 `CLOUD_ROADMAP.md`，Workspace／Device／SUPER_ADMIN 身分生命週期定案見 `CLOUD_IDENTITY_LIFECYCLE.md`。

> 本 repository 為 Public repository。本文不得包含任何實際 Cloud 帳號、私人 endpoint、密鑰、Token、OTP、正式公司資料、真實 Email 或其他營運／個資資訊。

## 1. 目前產品與工程基準

- CYInvoice Windows 正式產品線：C#／WinForms。
- 目前工程版本：V2.6.3 Build 0。
- Cloud backend：Cloudflare Worker + D1 reference implementation。
- Windows Client：provider-neutral，只接受使用者設定的 CYInvoice-compatible HTTPS endpoint。
- 預設模式：`local_only`。
- Cloud schema：2（`0001` + `0002`）。
- 已執行 migration 不回寫；後續 schema 一律使用 `0003+` forward migration。

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
- 每台 Device 獨立 token-hash foundation。
- Windows Cloud API client、HTTPS-only 驗證與 bounded timeout。
- Windows Device Token protected-storage abstraction。
- Pending Device Token／onboarding state protected persistence foundation。
- Local Only／Cloud Preferred 設定與 Cloud 異常 fallback 顯示。
- Windows → development Worker → D1 live connection 已驗證。
- development D1 尚未建立正式 Workspace；Windows 正確顯示 `連線正常｜尚未建立雲端空間`。
- Foundation bootstrap／device／pairing endpoints 與 .NET contract tests 已建立。
- Provider-neutral Email transport boundary 已建立，reference Worker 目前含 Brevo／Resend adapters。
- 目前 development reference Email Provider 定為 Brevo；實際 API Key 尚待帳號手機驗證後設定，因此尚未做 live Email delivery test。
- Public Windows client 不內建 project-owner Cloud endpoint。

## 4. 目前正在收斂：第一個 Workspace + 第一台 Device

已定案的 bootstrap contract：

```text
Workspace ID → Cloud 產生
Device ID    → Cloud 產生
Device Token → Windows 產生
```

Windows 在送出 bootstrap 前必須先把 Device Token 以 DPAPI 保存成 Pending；Cloud 只保存 Token hash。同一 Pending Token 必須能在 timeout／lost response 後找回已建立的 Cloud Workspace／Device identity，而不是建立第二個 Workspace。

PR #73 已先把 Core／Worker contract 朝此方向調整；Email OTP、正式 Windows onboarding UI 尚未完成，不能把目前 foundation endpoint 視為 V3.0 最終 UX。

## 5. 已定案但尚未全部實作的身分流程

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

## 6. Public Repo 與 Provider-neutral 邊界

CYInvoice Windows client：

- 不直接連 D1、PostgreSQL、SQL Server 或其他資料庫。
- 不內建專案擁有者私人 Cloud endpoint。
- 只連使用者設定的 CYInvoice-compatible HTTPS API。
- 不要求一般使用者理解 Cloudflare／D1 等底層技術。

Cloudflare Worker + D1 只是目前 reference backend。未來第三方可使用其他技術，只要符合 Cloud API contract。

Email Provider 同樣屬於 backend implementation detail：目前 reference deployment 使用 Brevo；未來有自有網域時可切到 Resend，Windows 與 OTP contract 不變。

## 7. Cloud Preferred 與 fallback

Cloud 健康時，用於跨裝置協調、中央身分、Work Item 與 Audit。

Cloud 暫時不可用時，安全的既有本機業務仍可繼續；只有真正依賴 Cloud 的管理功能停止，例如：

- 新 Device 註冊／配對／撤銷。
- 中央帳號／角色變更。
- SUPER_ADMIN 移交。
- Workspace 管理。

不採「所有敏感業務都必須先拿 Cloud Lock」的 blanket lock 設計。

## 8. 帳號與裝置是不同概念

正式模型必須區分：

- Employee / User：誰正在執行操作。
- Device：哪一台可信任電腦正在執行操作。

預定角色：

- `SUPER_ADMIN`：每個 Workspace 恰好一名。
- `ADMIN`：可多人。
- `EMPLOYEE`：可多人。

Local SUPER_ADMIN 只代表原單機系統最高管理者，不代表可以自行取得既有 Workspace 管理權。

## 9. 目前仍屬 prototype、不得誤認為正式 V3.0 行為

- `POST /v1/device-pairings` 目前仍只依有效 Device Token 即可發配對碼；正式版尚需人員授權／OTP gate。
- `POST /v1/device-pairings/claim` 目前 foundation 仍由 Cloud 產生 Device Token；後續 Device Join batch 需收斂成與正式生命週期一致的 Windows-generated Token。
- 尚無中央 Employee／Role／single-SUPER_ADMIN schema。
- 尚無 Recovery Email／OTP challenge schema；Email transport adapter 已有，但尚未接到 OTP endpoint。
- Brevo runtime API Key 尚未設定，因此尚未做 live Email delivery test。
- 尚無 Device revoke／recovery 正式 UI。
- 尚無 Work Item／Audit／跨機同步正式實作。

## 10. 已定案不進 Cloud 的內容

- AMEGO App Key。
- 完整發票資料鏡像。
- PDF Cache。
- 本機 runtime backup／export data。
- 不必要的 AMEGO response payload。
- 明文 Device Token／OTP／密碼／單機 Recovery Code。

## 11. 下一步工程順序

依 `CLOUD_IDENTITY_LIFECYCLE.md` 分批：

1. 驗證安全 bootstrap contract CI。
2. Pending Device Token／onboarding state DPAPI 持久化。
3. Brevo runtime Secret 完成後做 Email transport live test；同時可先完成 Local SUPER_ADMIN + existing Email OTP schema／邏輯。
4. Windows 首次 Workspace onboarding UI／timeout recovery。
5. Device Join／Recovery。
6. 中央 Employee／單一 SUPER_ADMIN／超管移交。
7. Pairing Code 前的人員授權。
8. 再進入 Work Item／Audit／多機 OrderID／正式折讓 API。

多公司、`company_id`、跨公司權限不在 V3.0 當前工程範圍。
