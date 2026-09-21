# CYInvoice 雲端架構現況與定案

> 本文件記錄 CYInvoice 雲端功能的目前工程狀態、已定案的產品邊界與後續實作方向。它是需求／狀態文件，不取代 `PROJECT_RULES.md`、`REPOSITORY_RULES.md` 或 `REPO_POLICY.md`。
>
> 本 repository 為 Public repository。本文不得包含任何實際 Cloud 帳號、資源 ID、私人端點、密鑰、Token、公司正式資料、Email、正式統編或其他營運／個資資訊。

## 1. 核心產品定位

CYInvoice 必須永久支援兩種運作模式：

1. **單機模式（Local Only）**
2. **雲端模式（Cloud Enabled / Cloud Preferred）**

單機模式是完整可用的產品模式，不是雲端模式的降級版。使用者若只需要單機使用，完成既有本機設定後即可使用，不需要任何雲端服務。

雲端模式是可選擴充層，目標是提供多裝置共同狀態、中央帳號／權限、跨機工作協調、防重與後續稽核能力。

## 2. Public Repo 的重要邊界

CYInvoice Windows client **不得直接綁定某一個實際雲端資料庫，也不得內建任何專案擁有者的私人 Cloud endpoint**。

正式設計必須符合：

- Windows client 不直接連 D1、PostgreSQL、MySQL 或其他資料庫。
- Windows client 只連一個由使用者設定的 **CYInvoice-compatible HTTPS API endpoint**。
- 該 API 背後使用何種資料庫、雲端平台、Server framework 或部署方式，不是 Windows client 的責任。
- repository 不得提交實際 Cloud 帳號 ID、Database ID、私人 Worker URL、API token、bootstrap secret、device token 或正式資料。
- Cloudflare Worker + D1 可以作為本專案目前的開發／參考實作，但不得成為 Public Windows client 的硬編碼依賴。

因此正式產品關係是：

```text
CYInvoice Windows client
        |
        | HTTPS
        v
User-configured CYInvoice Cloud API
        |
        v
Implementation-defined backend/database
```

## 3. 使用者模式切換

### 3.1 預設與單機模式

CYInvoice 預設必須為單機模式：

```text
Local SQLite + AMEGO
```

單機模式下：

- 不呼叫 Cloud API。
- 不要求 Cloud URL。
- 不要求 Workspace／Device 設定。
- 不要求 Cloud 帳號。
- 既有單機開票、查詢、同步、PDF、設定與本機快取流程維持不變。

### 3.2 啟用雲端模式

若使用者需要雲端版，流程應是：

1. 先以單機模式正常啟動 CYInvoice。
2. 到「設定」中選擇雲端模式。
3. 填寫 CYInvoice Cloud API 連線資訊。
4. 測試相容性與連線。
5. 完成 Workspace／管理員／裝置驗證流程。
6. 儲存後啟用 Cloud Preferred。

正式版不應要求一般使用者理解底層資料庫名稱或 Database ID。

## 4. 三層資料責任

CYInvoice 的資料責任分為三層，不能混為單一資料庫。

### 4.1 AMEGO：電子發票官方真相

AMEGO 仍是發票／折讓／註銷等正式交易狀態的權威來源。

CYInvoice Cloud 不得自行宣布 AMEGO 交易成功。所有交易結果仍需以 AMEGO 明確回覆或官方查詢結果確認。

### 4.2 Local SQLite：單機運作與快取

Local SQLite 永久保留，負責：

- 本機近期發票／查詢快取。
- 本機 UI 與同步狀態。
- 本機設定。
- 必要離線閱讀／fallback。
- 其他既有單機資料。

AMEGO App Key 與本機 Cloud device credential 等敏感資料仍由 Windows 安全儲存機制保護；不得上傳至 Public repository。

### 4.3 Cloud backend：跨裝置協作

Cloud backend 的責任是 CYInvoice 自己擁有的共同狀態，例如：

- Workspace。
- 員工／角色／enabled 狀態。
- Device 註冊與撤銷。
- Device credential hash。
- Email／OTP 驗證狀態（若正式採用）。
- Work item。
- Idempotency／operation lock。
- 跨機狀態協調。
- 未來 Cloud audit log。

Cloud backend 不應成為完整 AMEGO 發票資料鏡像，也不應保存不必要的 PDF、App Key 或大量本機 Cache。

## 5. Cloud API 是產品介面；技術實作不是

CYInvoice 最終對外應定義一份 **CYInvoice Cloud API compatibility specification**。

該規格描述 CYInvoice Windows client 需要的協定，例如：

- HTTPS endpoint 規則。
- API version／compatibility negotiation。
- Health check。
- Request／response JSON。
- Authentication／device credential。
- Workspace bootstrap／join。
- Employee／role。
- Verification／OTP（若採用）。
- Work items。
- Operation lock／idempotency。
- Audit contract。
- Error codes。
- Timeout／retry／offline semantics。

第三方可自行使用任何技術完成相容服務。CYInvoice 不規定必須使用 Cloudflare、D1、AWS、Azure、Supabase、Firebase、PostgreSQL、MySQL 或任何特定平台。

只要第三方服務符合 CYInvoice Cloud API specification，使用者即可在 CYInvoice 雲端設定中填入該服務 endpoint 使用。

## 6. 對外 Guide 的定案

在雲端功能、API contract、資料模型與錯誤語意全部定案後，Public repository 必須新增一份面向第三方的 Cloud integration guide。

Guide 的責任：

- 說明 CYInvoice Cloud API 必要介面與格式。
- 說明相容性／版本要求。
- 說明安全要求。
- 說明 Windows client 會如何呼叫 Cloud API。
- 提供可驗證的 contract examples／test expectations。

Guide **不負責**：

- 指定第三方一定使用 Cloudflare。
- 教第三方選擇雲端平台。
- 教第三方如何建立特定品牌資料庫。
- 替第三方設計其基礎設施。

目前先列入 TODO，等雲端功能定案後再撰寫正式版。

## 7. Cloud Preferred 與 fallback

啟用雲端模式後，CYInvoice 採 Cloud Preferred：

- Cloud 健康：使用 Cloud 協調跨裝置狀態。
- Cloud 暫時不可用：安全的本機功能仍可繼續使用。
- 需要跨裝置唯一性／鎖定的操作：若無法取得 Cloud lock，不得假裝取得成功。

使用者也可以主動切回單機模式。切回單機模式後，不應再呼叫 Cloud API。

「暫停使用 Cloud」與「解除此裝置的 Cloud 註冊」必須視為兩種不同操作；切回單機模式不應自動銷毀 Cloud 身分資料。

## 8. 帳號與裝置是兩個概念

正式雲端模型必須區分：

- **Employee / User**：誰正在執行操作。
- **Device**：哪一台受信任電腦正在執行操作。

兩者不可混為同一身分。

預定角色至少包含：

- `SUPER_ADMIN`
- `ADMIN`
- `EMPLOYEE`

正式的第一台裝置與後續裝置加入流程，將在中央員工／權限與驗證機制定案後完成。

## 9. 目前工程實作狀態

目前工作分支已完成 Cloud Foundation 的早期工程驗證，包括：

- 參考 Cloud API server。
- 參考資料庫 schema migration。
- Workspace／Device 基礎資料模型。
- Hashed device credential。
- 一次性 device pairing 基礎流程。
- Health／version endpoint。
- Windows Cloud client 基礎層。
- HTTPS-only endpoint validation。
- Windows 本機 protected credential storage。
- Cloud contract tests。
- Windows build／startup smoke validation。

目前參考實作使用 Cloudflare Worker + D1 作為開發環境，但這只是現階段的參考 backend。

## 10. 目前工程實作與最終產品設計的差距

目前早期測試流程仍包含為開發驗證而存在的做法，不能直接視為 Public 正式介面。

後續必須修正／完成：

- 移除 Windows client 中任何專案擁有者的預設 Cloud endpoint。
- 預設固定為 Local Only。
- 在正式設定 UI 中提供單機／雲端模式選擇。
- 只有選擇雲端模式才顯示／要求 Cloud API 設定。
- Cloud endpoint 由使用者自行填寫。
- 將 Windows client 對後端的依賴收斂成技術中立的 CYInvoice Cloud API contract。
- 將 Cloudflare/D1 特有設定留在 reference backend，而不是 Windows product contract。
- 完成 SUPER_ADMIN／Employee／Device 的正式 onboarding 模型。
- 決定並完成 Email OTP／其他驗證機制。
- 完成 work item、idempotency、operation lock、audit。
- 最後才撰寫第三方 Cloud integration guide。

## 11. 已定案不進 Cloud 的內容

目前已確定不以 Cloud backend 作為主要儲存位置的項目：

- AMEGO App Key。
- 完整發票資料鏡像。
- PDF Cache。
- 本機 runtime backup／export data。
- 不必要的 AMEGO response payload。

是否有其他欄位需要進 Cloud，必須依「跨裝置協作是否需要」與資料最小化原則逐項決定。

## 12. 下一步工程順序

在新增更多 Cloud 業務功能前，先完成 Public Repo 架構收斂：

1. 移除 Windows client 中的任何開發環境預設 endpoint。
2. 完成 Local Only / Cloud Enabled 模式選擇與設定保存。
3. 將 Cloud API endpoint 完全改為 user-configured。
4. 整理 reference backend 與 Windows client 的 contract 邊界。
5. 再進入中央員工／角色／裝置 onboarding。
6. 再進入跨機 work item／lock／idempotency。
7. 再進入 Cloud audit 與正式折讓 API。
8. 雲端全部定案後撰寫第三方 Cloud integration guide。

---

簡化後的正式定位：

```text
AMEGO        = 電子發票官方交易真相
Local SQLite = 單機運作、快取、fallback
Cloud API    = 可選的跨裝置協作介面
Backend DB   = 由 Cloud API 實作者自行決定
```
