# CYEnvelope × CY ID 與 Built-in Cloud 設計草案

更新日期：2026-09-30
狀態：**設計草案，尚未實作。** 實作等第一階段（Local 版 V0.3.0）完成試印並發布正式版之後才開始。

本檔為設計／狀態文件，不是規則；永久規則仍只放在 `REPOSITORY_RULES.md`、`REPO_POLICY.md`、`PROJECT_RULES.md`。設計定案、開始實作前，需另走 `governance/*` 分支更新 `PROJECT_RULES.md`（見第 12 節）。

參考：`apps/CYCloudIdentity/docs/`（AUTH_CONTRACT、ROLE_AND_ACCESS_MODEL、ARCHITECTURE）與 `apps/CYInvoice/docs/CY_ID_INTEGRATION.md`。CY ID 的契約以其自己的文件為準；本檔只寫 CYEnvelope 端的消費方式。

## 1. 目標與範圍

- CYEnvelope 從單機工具擴充為可多人共用客戶資料庫的雲端版：登入、角色權限、共用資料庫、待確認流程。
- 三種使用方式（比照 CYInvoice）：

| 模式 | 帳號來源 | 資料位置 |
|---|---|---|
| Local | 無登入 | 本機 SQLite（現行 V0.3.0） |
| CY ID Cloud | CY ID（Employee／Credential） | CYEnvelope 雲端資料庫，本機加密快取 |
| Built-in Cloud | CYEnvelope 自己的帳號（供自行部署的第三方） | 同上 |

- Local 模式永遠保留，不因雲端而移除。
- 與 CYInvoice 的差異：**CYEnvelope 雲端模式要求先登入才能使用**；CYInvoice 只在關鍵操作驗證權限。
- 本檔的第一優先是 CY ID 模式；Built-in Cloud 的帳號、密碼、OTP 另立文件（見第 11 節）。
- 不在範圍：CYInvoice 綁定 CY ID（屬其自己的工作線）、CY ID 內部 schema。

## 2. 架構

CY ID 只允許 Cloudflare Worker 以私有 Service Binding 呼叫，桌面程式不能直接呼叫。因此需要 CYEnvelope 自己的雲端後端：

~~~text
CYEnvelope（Windows）
  └─ HTTPS ─► CYEnvelope Worker ─ Service Binding ─► CY ID
                  └─ D1（共用輕量桌面程式資料庫，env_ 前綴）
                  └─ R2（各程式自己的備份）
~~~

- 桌面程式只保存不透明（opaque）session token 與加密快取；絕不接觸 CY ID 的密鑰、credential verifier、D1 識別碼。
- CYEnvelope Worker 每個受保護請求都向 CY ID `session/resolve` 重新確認 session、Employee 狀態、App Access 與角色，不自行快取授權結果。
- CY ID 的 `applicationId`、Workspace 綁定、D1／R2 的實際識別碼，由部署環境於部署時注入，不進公開 repo（REPO_POLICY §3）。公開 repo 只放 placeholder／template。

## 3. 身分與角色

CY ID 的角色與 CYEnvelope 一對一：

| CY ID | CYEnvelope 權限 |
|---|---|
| SUPER_ADMIN | 全部，另可設定離線天數（第 7 節） |
| ADMIN | 審核、編輯資料庫、信封格式、方框文字清單（權限與 SUPER_ADMIN 相同，除離線天數設定外） |
| USER | 檢視、搜尋、列印；儲存行為變成「待確認提案」 |

- 使用 CY ID 提供的不可變 `workspaceId` 與 `employeeId` 作為關聯鍵，不用姓名或工號。
- CYEnvelope 不提供帳號管理介面（建立員工、密碼、Email 驗證、App Access 都在 CY Web／CY ID）。第一次登入的 Email 驗證與改密碼流程也在 CY Web 完成；CYEnvelope 遇到「需先改密碼」只提示使用者到 CY Web。
- 沒有 CYEnvelope 的 App Access 者，CY ID 會拒絕；CYEnvelope 顯示「沒有使用權限，請洽管理員」，不維護第二份清單。
- CYEnvelope 的 Workspace 保存自己穩定的識別碼，與 CY ID Workspace 以 binding 關聯，不假設兩者永遠共用同一個 ID（比照 CYInvoice）。

## 4. 資料模型（共用 D1，`env_` 前綴）

多個輕量桌面程式共用一個 D1，資料表以前綴區分；CY ID 的 Identity D1 與 CYInvoice 既有 D1 不併入。

| 表 | 內容 |
|---|---|
| `env_workspace` | 與 CY ID Workspace 的 binding、`offline_days`（0–7）、設定版本 |
| `env_customer` | 客戶（名稱、正規化名稱、revision、更新者、時間） |
| `env_address` | 地址、三碼郵遞區號、標籤、所屬客戶 |
| `env_phone` | 電話、備註、所屬客戶 |
| `env_format` | 信封格式（版型）：位置、字級、字體、預設旗標 |
| `env_frame_text` | 方框文字清單 |
| `env_proposal` | 待確認提案（第 5 節） |
| `env_audit` | 稽核紀錄：誰、何時、做了什麼（不含完整個資內容以外的敏感資料） |

- 每張表都帶 `workspace_id`，所有查詢在 Worker 內強制以主體的 Workspace 過濾。
- 各程式用自己的 migration 記錄表（wrangler `migrations_table`），migration 只能動自己前綴的表。
- 「不會有同名客戶」的規則改為：新增客戶時若已有同名（依相同的名稱正規化），一律成為需人工處理的衝突，不自動建立。

## 5. 儲存行為與待確認流程

列印按鈕仍是「印出＝這筆存進去」的觸發點，但寫入方式依角色不同：

- **ADMIN／SUPER_ADMIN**：直接寫入資料庫（與 Local 模式相同的新增／覆蓋詢問視窗），並記稽核。
- **USER**：同一個詢問視窗，結果變成一筆提案，不直接寫入。列印本身不受待確認影響，永遠可以繼續印。

提案種類：新增客戶、新增地址、覆蓋地址、新增電話、覆蓋電話。提案保存：目標項目 id、目標當時的 revision、內容、提出者 `employeeId`、時間。

- **USER 看得到自己的待確認資料**：搜尋結果合併「已核准資料」與「自己提出的待確認資料」，後者標示「待確認」，只顯示給提出者本人。
- **審核**：管理員在審核畫面看到提案、與現有資料的差異，可「核准」、「修改後核准」、「退回」（可附原因）。
- **過期偵測**：覆蓋提案帶著目標當時的 revision；核准時若目標已被別人改過，顯示新舊差異，由管理員決定，不靜默覆蓋。
- **同名新客戶**：兩人同時提案同一個新客戶，或與既有客戶同名時，審核畫面提示「已有同名」，管理員選擇合併到既有客戶或退回。
- 提案核准與套用在同一個資料庫交易內完成。
- 管理員的其他編輯（客戶、地址、電話、格式、方框文字）只能在連線時進行。

## 6. 信封格式與字體

- 格式（版型）存雲端，每個格式包含位置、字級與字體（V0.3.0 Build 14 起已是此結構），管理員維護，所有裝置同步，使用者換電腦不需重新調整。
- 使用者本機只保存與電腦相關的項目：印表機名稱、輸入方式、「重印上一筆」。

## 7. 離線與快取

- 離線天數 `offline_days` 為 **0–7 的整數，存在雲端、所有裝置相同，只有 SUPER_ADMIN 可設定**（ADMIN 不可）。**0 ＝ 離線不可用（離線鎖定）**。CY ID 與 Built-in Cloud 兩種雲端模式都使用此設定。
- 本機保存：不透明 session token、客戶／格式的加密快取、最後一次成功連線的伺服器時間。**不保存密碼**。
- 加密：快取檔以隨機資料金鑰加密，金鑰由 Windows DPAPI（目前使用者範圍）保護。已知殘餘風險：同一個 Windows 使用者帳號下的其他程式仍可能取得金鑰；本機磁碟遺失或被複製到他機時，快取不可讀。
- 判斷可否離線使用：`現在時間 − 最後成功連線的伺服器時間 ≤ offline_days`，且 `offline_days > 0`。連線恢復後以雲端授權為準，重新解析 session；被停用者立即失效並清除本機快取。
- 時鐘回撥：本機時鐘可被使用者調整。除了比對伺服器時間，另記錄本機「最後看到的時間」，發現時間倒退就視為離線期限已過。這只能防範明顯的回撥，無法完全防止，屬已接受的限制。
- 登出、被停用、`offline_days = 0` 且離線、超過天數：清除本機快取（或保持不可讀）並回到登入畫面。
- 離線期間可用：檢視、搜尋、列印。離線期間的儲存動作寫入本機佇列，連線後上傳：USER 變成待確認提案；管理員的佇列項目上傳後套用，但仍做過期偵測，衝突時轉成待審。
- 離線期間不可：資料庫編輯、格式修改、審核、設定變更（避免多台電腦離線各自修改後合併）。

## 8. API 草案（CYEnvelope Worker）

僅列方向，實際欄位在實作時定稿；錯誤碼以穩定的 `error.code` 為準，UI 不從隱藏控制項推斷授權。

- `POST /v1/auth/login`：工號與密碼，轉交 CY ID；回傳 principal 與 session。
- `POST /v1/auth/logout`：撤銷 session。
- `GET /v1/session`：目前主體與 `offline_days`、時間。
- `GET /v1/sync?since=<revision>`：客戶、地址、電話、格式、方框文字的增量。
- `POST /v1/proposals`、`GET /v1/proposals`（管理員看全部，USER 只看自己的）、`POST /v1/proposals/{id}/decide`。
- 管理員：客戶、地址、電話、格式、方框文字的建立／修改／刪除。
- `PUT /v1/workspace/offline-days`：僅 SUPER_ADMIN。

## 9. 桌面程式的調整

- 把現有 `Repository` 抽成資料存取介面：`LocalStore`（現行 SQLite，行為不變）與 `CloudStore`（加密快取＋同步）。主畫面、客戶資料管理視窗、格式設定視窗改用介面，不直接依賴 SQLite。
- 啟動流程新增模式判斷與登入畫面（雲端模式）；Local 模式流程不變。
- 依角色隱藏或停用功能：USER 沒有客戶資料管理的編輯、格式設定、審核；畫面上要有「你目前是使用者，儲存會送出待確認」的清楚提示。
- 新增管理員的審核畫面與 SUPER_ADMIN 的離線天數設定。
- 既有 Local 資料上雲：由管理員在第一次連上雲端時選擇「匯入本機資料」，屬批次匯入；USER 不能匯入。詳細流程見第 13 節待決。
- 視覺仍遵守 CY Desktop Visual Guide（原生控制項、2 px 圓角按鈕、可感知的焦點、就地驗證訊息）。

## 10. 備份

- 各程式各自定期（Cron Trigger）把「自己前綴的表」在同一個交易內匯出到 R2 的專屬路徑，保留天數用 R2 生命週期規則管理。
- D1 時間點還原是整庫還原，只當作整庫故障的最後手段；單一程式的回復以自己的 R2 備份為準，不影響其他程式。
- 備份含客戶個資：R2 不得公開，不進 Git／Artifact／log；還原流程需事先演練。
- 需對照 Cloudflare 官方現行的 D1／R2 額度與限制後，才能寫入治理規則（目前只沿用「免費版最多 10 個 D1」的說法，未核實）。

## 11. Built-in Cloud

- 給自行部署的第三方公司：CYEnvelope 自己有 Employee、Credential、SUPER_ADMIN／ADMIN／USER，權限行為與第 3、5、6、7 節相同。
- 工程量主要在帳號、密碼、Email OTP、SUPER_ADMIN 移交等，比照 CYInvoice Built-in Cloud，另立文件，排在 CY ID 模式之後。
- 第一階段之後的第一個雲端版本先做 CY ID 模式，介面與後端預留 Built-in 的接口；同一個 Workspace 同一時間只能有一套 Employee authority（比照 CYInvoice，不同時有 Built-in 與 CY ID）。

## 12. 對治理規則的影響

實作前需另走 `governance/*` 分支：
- `PROJECT_RULES.md`：CYEnvelope 新增雲端模式（性質從單機工具改為含雲端）、三種模式、離線規則、資料與個資邊界、R2 備份、共用 D1 的資料表前綴與 migration 規則、正式 Release 流程。
- 共用 D1 的資料表前綴與備份規則，需同時涵蓋 CYInvoice 與其他桌面程式，可能屬共通規則的範圍。
- 更新 `GOVERNANCE_VERSION` 與 `GOVERNANCE_CHANGELOG.md`。

## 13. 待決事項

1. `offline_days = 0` 時，本機加密快取是否仍保留（只在連線時當加速快取用），還是登出／離線時直接刪除？傾向：保留但離線時不可讀，登出時清除。
2. 既有 Local 資料上雲的流程與範圍：完整匯入、只匯入客戶、或只允許管理員逐筆。
3. 時鐘回撥的偵測強度，以及是否記錄異常事件。
4. 提案的保留期限與退回後的顯示方式。
5. Built-in Cloud 的帳號與 OTP 細節（另立文件）。
6. CY ID 0.3.0（首次登入 Email 驗證）在 development 的部署狀態；接入前需確認已部署，並在 CY ID 註冊 CYEnvelope 這個 Application、對員工開使用權。
7. 稽核紀錄要保留多久、記錄哪些欄位（避免把過多個資寫入稽核）。

## 14. 分期

1. **第一階段（進行中）**：Local 版 V0.3.0，試印通過後發正式版。
2. 資料存取介面抽象（`LocalStore`／`CloudStore`），Local 行為不變。
3. CYEnvelope Worker、D1 表、CY ID 登入，桌面程式唯讀同步。
4. 待確認提案與審核畫面。
5. 加密快取、離線天數與離線佇列。
6. Built-in Cloud。
7. R2 備份與還原演練。

每一期都需 Windows 實機驗證；跨機情境（兩台電腦、離線與恢復）需多台實機。
