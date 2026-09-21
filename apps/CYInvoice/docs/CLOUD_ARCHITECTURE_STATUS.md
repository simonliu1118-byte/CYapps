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
- First-bootstrap Pending Device Token 以 DPAPI 保護並可跨程式重啟沿用。
- Device Join 另有獨立 Pending Device Token／endpoint／裝置名稱／開始時間，亦以 DPAPI 保護並可跨重新啟動恢復；Bootstrap Pending 與 Join Pending 不允許同時存在。
- Local Only／Cloud Preferred 設定與 Cloud 異常 fallback 顯示。
- Windows → development Worker → D1 live connection 已驗證。
- development D1 尚未建立正式 Workspace。
- Provider-neutral Email transport boundary 已建立，reference Worker 含 Brevo／Resend adapters。
- 目前 development reference Email Provider 定為 Brevo；實際 API Key 尚待帳號手機驗證後設定，因此尚未做 live Email delivery test。
- Email OTP foundation 已實作：6 碼、HMAC-SHA256、10 分鐘有效、5 次錯誤上限、60 秒重寄冷卻、每 Email／用途每小時限流、一次性消耗。
- first Workspace bootstrap 要求有效 Email challenge + OTP，並把已驗證 Email 寫入 Workspace Recovery Email。
- bootstrap retry／lost response 以同一 Pending Device Token 復原既有 Cloud Workspace／Device，避免第二套 identity。
- WinForms 首次 Workspace onboarding UI 已接上：本機 SUPER_ADMIN 驗證、既有 Email 遮罩顯示、寄送 OTP、輸入 OTP、Pending Token 安全落地、bootstrap、`GET /v1/device` 最終確認。
- `GET /v1/device` 驗證成功且 Workspace／Device ID 與 bootstrap 結果一致後，Pending identity 才轉為正式 Cloud identity。
- Cloud 已有 Workspace 且本機沒有 Device identity 時，`雲端連線設定` 改顯示「加入雲端空間」，不會再嘗試建立第二個 Workspace。
- B 機 Pairing Claim 已改為 **Device Token 由 Windows 產生、Device ID 由 Cloud 產生**；Cloud 只存 Token hash，不再回傳唯一一份明文 Device Token。
- Pairing Claim 對 timeout／lost response 可使用同一 Pairing Code + 同一 Pending Token 找回既有 Device；不同 Token 不可重用已消耗的 Pairing Code。
- B 機 WinForms Device Join UI 已接上：先 DPAPI 保存 Pending Token，再送 claim，最後以同一 Token 呼叫 `GET /v1/device` 驗證後才提交正式 identity。
- A 機「裝置管理」UI 已接上，可先寄 Workspace Recovery Email OTP，OTP 成功後才產生短效、單次 Pairing Code。
- `POST /v1/device-pairings/authorization-email` 要求有效 Device bearer token，收件者固定取自該 Workspace 已驗證 Recovery Email，Windows 不能任意指定 Email。
- `POST /v1/device-pairings` 現在要求同一可信任 Device 綁定的 Email challenge + OTP；僅持有 Device Token 已不足以產生 Pairing Code。
- Settings 切換 Cloud endpoint 時若仍有 Pending bootstrap／Device Join credential，會阻止直接丟棄該 Pending 狀態，避免斷線結果不明時失去唯一復原 Token。
- `雲端初始化碼` 只存在於當次密碼式輸入／HTTPS request，不寫入 settings、repo、log 或 artifact。
- Settings 視窗與 Cloud onboarding 共用同一個 in-memory Settings instance，避免父視窗舊資料覆寫剛建立的 Cloud identity。
- Public Windows client 不內建 project-owner Cloud endpoint。

## 4. 第一個 Workspace + 第一台 Device contract

已定案並已進入 Core／Worker／WinForms 的 bootstrap model：

```text
Workspace ID → Cloud 產生
Device ID    → Cloud 產生
Device Token → Windows 產生
```

Windows 在送出 bootstrap 前先把 Device Token 以 DPAPI 保存成 Pending；Cloud 只保存 Token hash。

第一次 Workspace 增加 Email authorization：

```text
既有 Local SUPER_ADMIN
        ↓ 本機先驗證本人
沿用其既有 Email（UI 只顯示遮罩）
        ↓
POST /v1/onboarding/bootstrap-email
        ↓
6 碼 OTP
        ↓
Windows 先保存 Pending Device Token
        ↓
POST /v1/bootstrap
        ↓
Cloud 建立 Workspace + 第一台 Device
並綁定 verified Recovery Email
        ↓
GET /v1/device
        ↓
核對 Workspace／Device
        ↓
Pending → 正式 Cloud identity
```

使用者不需要再輸入第二個 Cloud Email。

若 bootstrap 回應 timeout／中斷而結果不明，Windows 保留原 Pending Token，不重新產生 Token；可使用同一 Token 嘗試找回已建立的 Device identity。若 Cloud 已存在 Workspace，Windows 不會盲目重建 Workspace。

## 5. Existing Workspace：Pairing Code 與 Device Join

目前 Pairing Code 正式 foundation 已收斂成兩段授權：

```text
可信任 A 機
  ↓ Device Token 驗證
POST /v1/device-pairings/authorization-email
  ↓
寄 OTP 至 Workspace 已驗證 Recovery Email
  ↓
A 機輸入 OTP
  ↓
POST /v1/device-pairings
  ↓
Cloud 產生短效一次性 Pairing Code
  ↓
B 機輸入 Pairing Code
  ↓
B 機先產生並 DPAPI 保存 Pending Device Token
  ↓
POST /v1/device-pairings/claim
  ↓
Cloud 產生 Device ID、只保存 Token hash
  ↓
B 機 GET /v1/device
  ↓
核對成功後 Pending → 正式 Device identity
```

Pairing Code 授權的是 **Device 加入 Workspace**，不是人員角色。B 機原本若有 Local SUPER_ADMIN Y，這個流程不使用 Y 的 Email 自我批准，也不會把 Y 自動升成 Cloud SUPER_ADMIN。

Pairing Claim 的 retry anchor 是 B 機事前保存的 Device Token。若 Cloud 已建立 Device 但回應途中斷線，同一 Token 可重新驗證／找回既有 Device；不建立第二台孤兒 Device。

## 6. OTP 與 Email 安全邊界

- OTP 由 Web Crypto 產生，不使用 `Math.random()`。
- D1 不保存明文 OTP，只保存 `HMAC-SHA256(OTP_PEPPER, challengeId:otp)`。
- `OTP_PEPPER` 必須是 Worker Secret，不進 repo／log／artifact。
- OTP Email Provider Key 同樣只存在 Worker runtime secret。
- Provider delivery failure不記錄 provider response body、收件 Email、OTP 或信件本文。
- Recovery Email 是 Workspace 必要私有後端資料，但不得進 public log／artifact。
- OTP challenge 使用後立即 consumed；錯誤次數、重寄 cooldown 與 rate limit 都由 Cloud enforce。
- Pairing authorization challenge 綁定 Workspace + 發起的可信任 Device，不能拿另一台 Device 的 challenge 直接產生 Pairing Code。
- Challenge ID 不必跨重啟持久保存；真正必須事前安全落地的是 Device Token。

## 7. 已定案但尚未全部實作的身分流程

- 第一次從單機版建立 Workspace：Windows UI／Core／Worker foundation 已接通；尚待 Brevo runtime credential 可用後做真實 Email delivery／OTP live test。
- **中央 Cloud Employee／Role／single-SUPER_ADMIN schema 尚未實作。** 目前 Recovery Email OTP gate 不等於中央 Employee 角色已完成。
- Workspace 建立後不因 Token 遺失、Windows 重灌或程式重新下載而重建。
- Existing Workspace 的 Pairing Code Device Join 已完成 foundation／UI；「所有 Device Token 都遺失時直接用 Workspace Recovery Email OTP 建立 Recovery Device」尚未實作。
- 新機自己的 Local SUPER_ADMIN 不得自行授權加入既有 Workspace。
- 既有單機 B 機合法加入後，原唯一 Local SUPER_ADMIN Y 後續由中央 Employee 層建立／對應為 Workspace ADMIN；在 X 決定前 Y 只擁有 ADMIN，不影響日常工作。
- Workspace 永遠只有一名 SUPER_ADMIN。
- 超管換人使用原子 `TransferSuperAdmin`：原超管降 ADMIN、指定 ADMIN 升 SUPER_ADMIN。
- 中央 Employee 完成後，Pairing Code 的人員授權再由「Workspace Recovery Email OTP」收斂為「SUPER_ADMIN 或具 DEVICE_MANAGE 權限 ADMIN + OTP」，Pairing Code／B 機 claim contract 不需要改。
- 所有 Device Token + 原 Recovery Email 同時失效時，以 Cloudflare／reference backend 管理端人工修改 Recovery Email 作最終維運救援，不增加 Windows emergency secret。

完整流程與安全邊界見 `CLOUD_IDENTITY_LIFECYCLE.md`。

## 8. Public Repo 與 Provider-neutral 邊界

CYInvoice Windows client：

- 不直接連 D1、PostgreSQL、SQL Server 或其他資料庫。
- 不內建專案擁有者私人 Cloud endpoint。
- 只連使用者設定的 CYInvoice-compatible HTTPS API。
- 不要求一般使用者理解 Cloudflare／D1 等底層技術。

Cloudflare Worker + D1 只是目前 reference backend。未來第三方可使用其他技術，只要符合 Cloud API contract。

Email Provider 同樣屬於 backend implementation detail：目前 reference deployment 使用 Brevo；未來有自有網域時可切到 Resend，Windows 與 OTP contract 不變。

## 9. Cloud Preferred 與 fallback

Cloud 健康時，用於跨裝置協調、中央身分、Work Item 與 Audit。

Cloud 暫時不可用時，安全的既有本機業務仍可繼續；只有真正依賴 Cloud 的管理功能停止，例如：

- 新 Device 註冊／配對／撤銷。
- 中央帳號／角色變更。
- SUPER_ADMIN 移交。
- Workspace 管理。

不採「所有敏感業務都必須先拿 Cloud Lock」的 blanket lock 設計。

## 10. 帳號與裝置是不同概念

正式模型必須區分：

- Employee / User：誰正在執行操作。
- Device：哪一台可信任電腦正在執行操作。

預定角色：

- `SUPER_ADMIN`：每個 Workspace 恰好一名。
- `ADMIN`：可多人。
- `EMPLOYEE`：可多人。

Local SUPER_ADMIN 只代表原單機系統最高管理者，不代表可以自行取得既有 Workspace 管理權。Device Pairing 完成也不代表任何本機帳號取得 Cloud 人員角色。

## 11. 目前仍屬未完成／不得誤認為正式 V3.0 行為

- 尚無中央 Employee／Role／single-SUPER_ADMIN Cloud schema。
- B 機 Local SUPER_ADMIN Y → Workspace ADMIN 的中央 Employee 建立／mapping 尚未實作。
- 帳號管理的原子 SUPER_ADMIN 移交尚未實作。
- Pairing Code 目前的人員授權暫以 Workspace verified Recovery Email OTP 為 gate；中央 Employee 完成後才加入 SUPER_ADMIN／授權 ADMIN permission 檢查。
- 尚無「全部 Device Token 遺失」時直接使用 Recovery Email OTP 建立新 Device 的 Recovery UI／API。
- 尚無 Device revoke 正式 UI。
- Brevo runtime API Key 尚未設定，因此尚未做 live Email delivery test。
- 尚無 Work Item／Audit／跨機同步正式實作。

## 12. 已定案不進 Cloud 的內容

- AMEGO App Key。
- 完整發票資料鏡像。
- PDF Cache。
- 本機 runtime backup／export data。
- 不必要的 AMEGO response payload。
- 明文 Device Token／OTP／密碼／單機 Recovery Code。

## 13. 下一步工程順序

依 `CLOUD_IDENTITY_LIFECYCLE.md` 分批：

1. Brevo 手機驗證恢復後，設定 runtime Secret 並做 live Email transport／OTP 測試；不阻塞其餘工程。
2. 中央 Employee／單一 SUPER_ADMIN／B 機 Y→ADMIN mapping／原子超管移交。
3. 全 Device Token 遺失時的 Recovery Email OTP Device Recovery，以及 Device revoke／管理。
4. Pairing authorization 接上中央 Employee permission gate。
5. 再進入 Work Item／Audit／多機 OrderID／正式折讓 API。

多公司、`company_id`、跨公司權限不在 V3.0 當前工程範圍。
