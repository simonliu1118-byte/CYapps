# 單一登入入口設計（CY Web 登入入口）

> 狀態：**設計稿，待使用者確認後才實作**（使用者 2026-11-18 同意採方案 B）。本文件描述設計與驗收，不是規則來源；定案後的契約寫入 `AUTH_CONTRACT.md` 與 `CONSUMER_INTEGRATION_STANDARD.md`，並依 consumer 版本規則升版。

## 1. 背景與目標

CY Web、CYAccountingWeb、CYPriceDesk 都使用 CYID 帳號，但各自實作登入頁；樣式、記住我、錯誤訊息、首次登入導向要改三次，容易不一致（使用者 2026-11-18）。

目標：

- 只有 CY Web 一個登入頁。其他 App 需要登入時導向 CY Web，登入後帶一次性交換碼回原 App，原 App 再向 CYID 換成自己的 Session。
- 已在 CY Web 登入者開其他 App 不必再輸入密碼（仍逐 App 檢查 Application Access）。
- 安全性不低於現況：密碼只在 CY Web 輸入；各 App Session 互相獨立；不共用跨子網域 cookie。

不做：

- 跨子網域共用 Session cookie（方案 C）——一個子網域出問題會波及全部。
- 第一階段不做「全部登出」；各 App 維持各自登出（之後另議）。
- CYID 不開公開網頁或公開 API；交換仍只走 private Service Binding。

## 2. 名詞

| 名詞 | 意義 |
| --- | --- |
| 入口（portal） | CY Web（CYID 已登記為 account portal，`ACCOUNT_PORTAL_URL`） |
| 目標 App | 要登入的 App（例：CYPRICEDESK、CYACC） |
| 交換碼（handoff code） | 入口發給目標 App 的一次性代碼，只能透過 CYID 換 Session |
| state | 目標 App 出發前產生的一次性核對值，防 login CSRF |
| 回跳網址（return URL） | 目標 App 接收交換碼的固定網址，須事先登記 |

## 3. 流程

```
使用者      目標 App（例 price）          CY Web（入口）               CYID（private）
  │ 開 /           │                            │                          │
  │──────────────▶│ 無 Session                  │                          │
  │               │ 產生 state，存 HttpOnly cookie（10 分鐘）               │
  │◀── 303 ───────│ → admin…/sso?app=CYPRICEDESK&state=…                   │
  │──────────────────────────────────────────▶│ 未登入 → 顯示 CY Web 登入頁 │
  │                                            │ 已登入 ↓                   │
  │                                            │── handoff/issue ────────▶│ 驗 Session、App 已登記、
  │                                            │                          │ 有 Application Access
  │                                            │◀── code（60 秒、一次）───│ 存 code 雜湊
  │◀── 303 → 登記的回跳網址?code=…&state=… ─────│                          │
  │──────────────▶│ /auth/sso/callback          │                          │
  │               │ 比對 state cookie（常數時間），刪除                      │
  │               │── handoff/redeem（自己的 IDENTITY binding）────────────▶│ 驗雜湊、未用、未過期、
  │               │                                                        │ App 相符；標記已用；
  │               │◀── 新 Session token＋principal ───────────────────────│ 重驗員工與 Access
  │               │ 設自己的 HttpOnly cookie                                │
  │◀── 303 → / ───│（網址不留 code）             │                          │
```

說明：

1. 回跳網址**不由網址參數決定**：入口只收 `app` 與 `state`；回跳網址由 CYID 依該 App 的登記值決定，入口照 CYID 回傳的網址轉址。這樣根本沒有開放式轉址的參數可以被竄改。
2. 首次登入（first-login-required）、忘記密碼、變更 Email 一律在 CY Web 完成，完成後才繼續發交換碼。
3. 沒有該 App 的 Application Access：CY Web 顯示「此帳號沒有 <App 名稱> 的使用權限」，不轉址。
4. 交換碼兌換時 CYID **重新檢查**員工、Workspace、App、Application Access 與憑證版本——發碼後被停用或撤銷權限的人換不到 Session。

## 4. CYID 變更

### 4.1 App 回跳網址登記

- `applications` 新增 `sso_return_url`（TEXT，可空）：一個完整 HTTPS 網址，必須是該 App 自訂網域上的固定路徑（例 `https://price.chihyuancm.com/auth/sso/callback`）。
- 只接受 `https:`、無帳密、無 query、無 fragment；比對採**完整字串相等**，不用前綴或萬用字元。
- 由 CY Web 既有的 App 登記功能（Super Admin，`CONSUMER_INTEGRATION_STANDARD.md` §17.1）設定，寫 Audit。
- 未登記回跳網址的 App 不能使用單一登入（維持原登入方式）。

### 4.2 新增接口（只走 Service Binding）

| 方法 | 路徑 | 呼叫者 | 作用 |
| --- | --- | --- | --- |
| POST | `/v1/identity/sso/handoff/issue` | 入口（CY Web，帶自己的 Session token） | 驗入口 Session 有效；目標 App 存在、Workspace 啟用、有登記回跳網址；使用者有該 App 的 Application Access。回 `{code, returnUrl}` |
| POST | `/v1/identity/sso/handoff/redeem` | 目標 App（`x-identity-application` 為自己） | 帶 `code`；驗雜湊、未用、未過期、綁定 App 等於呼叫者；原子標記已用；重驗權限；建立該 App 的新 Session（同 login）。回 `{sessionToken, expiresAt, principal}` |

- 交換碼：32 bytes 隨機（base64url），只存 SHA-256；有效 60 秒；單次使用（`UPDATE … WHERE used_at IS NULL AND expires_at > now` 並檢查 `changes()`）。
- 新表 `sso_handoffs(code_hash PK, application_id, employee_id, workspace_id, issued_session_id, created_at, expires_at, used_at)`；過期資料由既有排程清除。
- 只有入口 App（core account application）能呼叫 issue；其他 App 呼叫回 403。
- redeem 失敗（無此碼、過期、已用、App 不符）一律回同一個錯誤碼 `SSO_HANDOFF_INVALID`，不透露原因；依 App＋來源做失敗次數限制。
- Audit：issue、redeem 成功與失敗都記錄（App、員工、結果、原因代碼），**不記交換碼本身**。

## 5. CY Web（入口）變更

- 新路由 `/sso?app=<APPLICATION_ID>&state=<state>`：
  - `state` 只接受 32–128 字元 base64url，原樣帶回，不存。
  - 未登入：顯示既有登入頁（同一份），登入成功後回到 `/sso` 繼續。
  - 已登入：呼叫 `handoff/issue`，以 303 轉到 CYID 回傳的 `returnUrl`，附 `code`、`state`。
  - 回應加 `Referrer-Policy: no-referrer`、`Cache-Control: no-store`。
- App 登記頁新增「單一登入回跳網址」欄位。
- 登入頁維持 CY Web 現有樣式；之後其他 App 的登入入口一律看這一頁。

## 6. 目標 App 變更（以 CYPriceDesk 為試行）

- 未登入的頁面請求：產生 state（32 bytes base64url），存 `__Host-<app>_sso_state` cookie（HttpOnly、Secure、SameSite=Lax、Path=/、10 分鐘），303 到 `https://admin.chihyuancm.com/sso?app=…&state=…`。API 請求仍回 401（前端再導向）。
- `/auth/sso/callback`：
  - 比對 query `state` 與 cookie（常數時間），不符或缺少 → 錯誤頁，不兌換。
  - 立即刪除 state cookie。
  - 以 IDENTITY binding 呼叫 `handoff/redeem`，成功後設自己的 Session cookie（維持現有 HttpOnly、Secure、SameSite=Lax、不設 Domain），303 到 `/`（網址不留 code）。
  - 回應加 `Referrer-Policy: no-referrer`、`Cache-Control: no-store`。
- 舊登入頁（`/login`）第一階段保留為備援，不再是預設入口；穩定後移除（第 9 節）。
- 登出：維持只登出本 App（呼叫 provider logout）。

## 7. 已知限制與取捨

- **交換碼會出現在網址列與 Cloudflare 請求紀錄**（Worker observability 會記 URL）：交換碼 60 秒、單次、綁 App、只能經 private binding 兌換，外部取得也無法使用；紀錄屬帳號私有。可接受。
- **已登入 CY Web 就自動進入其他 App**：與在 CY Web 內操作同等風險；仍受各 App Application Access 與 Session 逾時限制。若某 App 需要每次重新輸入密碼，之後可在登記上加「強制重新驗證」（第一階段不做）。
- **CY Web 成為登入的單一關鍵點**：CY Web 停機時其他 App 無法新登入（已登入者不受影響）。第一階段保留各 App 舊登入頁作備援即可緩解。
- 這是 consumer 契約新增（向下相容：未採用的 App 照舊），consumer 版本升 minor，PR 標示 `CYID Consumer Impact: BACKWARD_COMPATIBLE`。

## 8. 安全驗收清單（每項都要有自動測試）

1. 竄改 `app`、附加任何回跳參數 → 只會轉到該 App 登記的網址；未登記 → 拒絕。
2. 回跳網址比對為完整相等：`https://price.chihyuancm.com.evil.com/…`、多斜線、大小寫變體、加 query 都被拒。
3. 交換碼重放（第二次兌換）→ `SSO_HANDOFF_INVALID`；兩個請求同時兌換只有一個成功。
4. 過期（>60 秒）→ 拒絕。
5. 用 A App 的 binding 兌換發給 B App 的碼 → 拒絕。
6. state 缺少、不符、cookie 過期 → 不兌換。
7. 發碼後停用員工或撤銷 Application Access → 兌換失敗。
8. 非入口 App 呼叫 issue → 403。
9. 沒有 Application Access → CY Web 顯示無權限，不轉址、不發碼。
10. first-login-required → 先在 CY Web 完成，不發碼。
11. Audit 有 issue／redeem 成功與失敗紀錄，且任何紀錄、錯誤訊息都不含交換碼與 Session token。
12. Session cookie 不設 Domain、維持 HttpOnly／Secure／SameSite=Lax；callback 與 /sso 回 `no-store`、`no-referrer`。
13. 兌換失敗次數限制生效。

上線前另做一次獨立安全審查。

## 9. 分階段

| 階段 | 內容 | 驗收 |
| --- | --- | --- |
| 1 | CYID：`sso_return_url`、`sso_handoffs`、issue／redeem、Audit、清除排程；consumer 標準與 AUTH_CONTRACT 更新 | 第 8 節 1–5、7、8、11、13 |
| 2 | CY Web：`/sso` 入口、App 登記欄位 | 第 8 節 6、9、10、12（入口端） |
| 3 | CYPriceDesk 試行：callback、未登入導向入口、舊登入頁保留為備援 | 實機：已登入／未登入／無權限／首次登入四種情境 |
| 4 | CYAccountingWeb 改用 | 同上 |
| 5 | 穩定一段時間後，移除各 App 舊登入頁 | 使用者確認 |

時程：等 CY Web V0.22.0（BOT 管理中心改版）部署後再開始第 2 階段，避免同時改 CY Web。

## 10. 待使用者確認

1. 已登入 CY Web 時，開其他 App **自動進入**（建議），還是每次顯示「以 <姓名> 身分前往 <App>？」確認一下？
2. 各 App 舊登入頁在第 5 階段**移除**（建議，避免兩套入口又不一致），還是長期保留作備援？
