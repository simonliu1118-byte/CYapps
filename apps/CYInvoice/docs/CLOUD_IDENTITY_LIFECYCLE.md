# CYInvoice V3.0 Workspace／Device／SUPER_ADMIN 身分生命週期定案

本文件記錄 CYInvoice V3.0 在 Workspace、Device、Device Token、SUPER_ADMIN／ADMIN、首次建立、裝置加入、Token 遺失、Email 復原與超管移交上的已定案產品與安全行為。

本文件是設計／需求基準，不取代 `PROJECT_RULES.md`。若與永久治理規則衝突，仍依 repository／project governance 優先順序處理。

## 1. 核心不變量

1. **Workspace 建立後長期存在。** 電腦重裝、程式重新下載、Device Token 遺失都不得因此重新建立 Workspace。
2. **Workspace 永遠只允許一名 `SUPER_ADMIN`。** V3.0 不支援多超管。
3. **`ADMIN` 可以多人。** `SUPER_ADMIN` 移交後，原超管自動降為 `ADMIN`，指定管理員升為 `SUPER_ADMIN`。
4. **Device 與 Employee 是兩種不同身分。** Device Token 只能證明「這台電腦已加入 Workspace」，不能單獨證明操作的人是管理員。
5. **Local SUPER_ADMIN 只代表單機版最高管理者。** 既有 Workspace 的加入授權只能由 Workspace 既有信任來源決定，不能由新機自己的 Local SUPER_ADMIN 自我授權。
6. **Cloud 是協作／身分服務，不是單機功能總開關。** AMEGO 發票／作廢／折讓官方狀態仍以 AMEGO 為準。
7. **Device Token 不寄 Email、不寫 log、不存 Cloud 明文。** Windows 以 DPAPI 保存；Cloud 只保存 Token hash。
8. **Token 或初始化結果不明時不得盲目建立第二個 Workspace／Device。** 必須先辨識既有 Cloud 狀態並安全恢復。

## 2. 第一次使用 CYInvoice

全新 CYInvoice 第一次啟動時提供兩條正式路徑：

```text
第一次使用 CYInvoice
        │
        ├─ 建立單機版
        │      ↓
        │   建立 Local SUPER_ADMIN
        │   姓名／密碼／Email／單機 Recovery Code
        │
        └─ 加入既有雲端空間
               ↓
          Pairing Code
          或 Workspace SUPER_ADMIN Email OTP
               ↓
          建立本機 Cloud Device
               ↓
          載入 Workspace 身分／權限資料
```

因此後續第二台全新電腦不必先建立一個多餘的 Local SUPER_ADMIN。

## 3. 第一台單機版切換成雲端版

典型狀態：

```text
A 機
Local SUPER_ADMIN = X
X 已有 Email
Cloud endpoint 尚無 Workspace
```

切換流程：

1. 使用者在設定中選擇「雲端版」。
2. Windows 驗證 CYInvoice-compatible HTTPS Cloud API。
3. Cloud 回報 `Workspace 尚未建立`。
4. 要求本機既有 `SUPER_ADMIN X` 完成本機身分驗證。
5. **不重新輸入 Email。** 直接讀取 X 既有 Email，畫面只顯示遮罩後地址，例如 `s***@example.com`。
6. 發送一次性 Email OTP 到 X 已登記 Email。
7. OTP 驗證成功後，才允許進入 Workspace 初始化。
8. 一次性 server-side bootstrap guard／雲端初始化碼仍可作為 reference backend 的第一次初始化保護；不得寫入 repo、log、安裝包或明文持久化設定。
9. 初始化成功後，X 成為該 Workspace 唯一的 `SUPER_ADMIN`。

Workspace Recovery／SUPER_ADMIN Email 的初始來源就是既有 Local SUPER_ADMIN 已驗證 Email，不在建立 Workspace 畫面提供任意 Email 輸入欄位。

## 4. Workspace ID、Device ID、Device Token 的來源

正式分工：

| 資料 | 產生端 | 保存方式 |
|---|---|---|
| `workspace_id` | Cloud | Cloud + Windows identity metadata |
| `device_id` | Cloud | Cloud + Windows identity metadata |
| Device Token | Windows | Windows DPAPI；Cloud 只存 hash |

### 4.1 為什麼 Device Token 由 Windows 先產生

若 Token 完全由 Cloud 產生，可能發生：

```text
Cloud 已建立 Workspace + Device + Token hash
        ↓
回傳 Token 的途中斷線
        ↓
Windows 永遠拿不到原 Token
```

因此正式流程採「先安全落地，再送網路」：

```text
Windows 產生高強度 Device Token T1
        ↓
DPAPI 加密並以 Pending 狀態持久化
        ↓
確認 Pending T1 已成功寫入本機
        ↓
才送 bootstrap request
        ↓
Cloud 產生 Workspace ID + Device ID
Cloud 保存 hash(T1)
        ↓
回傳 Workspace ID + Device ID
        ↓
Windows 使用 T1 呼叫 GET /v1/device
        ↓
確認 Workspace／Device 身分
        ↓
Pending identity 轉成正式 Cloud identity
```

### 4.2 Cloud 不得保存明文 Token

Cloud 只保存 `hash(DeviceToken)`；明文 Token 只存在於 Windows 安全儲存與當次 HTTPS 傳輸。

## 5. Bootstrap timeout／送出途中斷線

任何 bootstrap timeout、connection reset、程式中止或回應遺失都不得直接重新產生 Token 或重建 Workspace。

### 5.1 Cloud 完全沒收到

```text
Windows：仍有 Pending T1
Cloud：沒有 Workspace / Device
```

重新連線後：

1. 查 onboarding status。
2. 確認仍是 `uninitialized`。
3. **使用同一個 Pending T1 重送 bootstrap。**

### 5.2 Cloud 已完成，但回應途中遺失

```text
Cloud：Workspace + Device + hash(T1) 已完成
Windows：仍有 Pending T1，但沒有收到 IDs
```

重新連線後：

1. 查 onboarding status，發現 Workspace 已存在。
2. 使用 Pending T1 嘗試 `GET /v1/device`。
3. 若驗證成功，取得原本 Cloud 產生的 Workspace ID／Device ID。
4. Pending identity 轉正式，不建立第二個 Workspace／Device。

### 5.3 Workspace 已存在，但 Pending T1 無法驗證

視為異常／復原狀態：

- 不盲目 bootstrap。
- 不建立第二個 Workspace。
- 進入既有 Workspace 的 Device Join／Recovery 流程。

### 5.4 Cloud 原子性要求

第一次初始化的 Workspace 與第一台 Device 必須視為同一個原子建立單位；Cloud 不得留下：

```text
Workspace 已建立
但第一台可信任 Device 不存在
```

若發生競態或重試，reference backend 必須能以同一 Pending Token hash 辨識同一次初始化，而不是建立另一份 identity。

## 6. 只是重新下載程式

如果只刪除／更換 `CYInvoice.exe`，原本本機 Data／settings 與 DPAPI identity 仍存在：

```text
Workspace ID
Device ID
Device Token
```

重新下載後使用原 Token 呼叫 `GET /v1/device` 驗證即可：

- Token 不變。
- Device 不變。
- Workspace 不變。

## 7. Device Token／本機資料全部遺失

例如 Windows 重灌、整個 runtime data 被刪除、硬碟故障或 DPAPI 無法解密。

若 Cloud 已有 Workspace，而本機沒有有效 Device identity，Windows 必須顯示：

> 此雲端空間已建立，但這台電腦尚未加入。

不得顯示「建立新 Workspace」。

可用兩種恢復／加入方式：

1. 另一台已認證 Device 產生 Pairing Code。
2. Workspace 已登記的 `SUPER_ADMIN` Email OTP。

Workspace 永遠不因 Token 遺失而重建。

## 8. SUPER_ADMIN Email 復原

### 8.1 正常修改 Email

只要至少一台有效 Device 仍存在：

```text
SUPER_ADMIN 登入 CYInvoice
→ 修改自己的 Email
→ 新 Email OTP 驗證
→ Cloud 更新 Workspace Recovery / SUPER_ADMIN Email
```

如果 SUPER_ADMIN 忘記本機密碼，先使用既有單機版 Recovery Code 恢復本機 SUPER_ADMIN，再修改 Email。

### 8.2 所有 Device Token 都遺失

使用 Workspace 已登記的 SUPER_ADMIN／Recovery Email：

```text
要求 Device Recovery
        ↓
Cloud 對既有已登記 Email 發 OTP
        ↓
OTP 通過
        ↓
允許建立新的 Device
        ↓
新 Device Token 由 Windows 產生並安全保存
        ↓
加入原 Workspace
```

Windows 不得讓使用者任意指定一個新 Email 作為既有 Workspace 的復原目的地。

### 8.3 雙重災難最終解法

雙重災難定義：

```text
所有有效 Device Token 都遺失
+
Workspace 原 SUPER_ADMIN / Recovery Email 也無法使用
```

V3.0 **不再新增另一套 Emergency Recovery Secret**。

最終救援方式：

1. 由系統擁有者直接進 Cloudflare／reference backend 管理端人工確認。
2. 直接在後端維運層更新該 Workspace 的 Recovery／SUPER_ADMIN Email。
3. 回到 CYInvoice 後，使用新 Email OTP 走正常 Device Recovery。

這是人工維運救援，不做成 Windows Client 公開 API，也不建立一般使用者可自行呼叫的「任意更改 Recovery Email」端點。

如未來實作維運工具，應留下必要 audit，但不得記錄 OTP、Device Token 或其他秘密。

## 9. 新增第二台 Device

正式授權點放在「產生 Pairing Code 之前」，避免 B 機重複做無意義的人員認證。

A 機流程：

```text
設定 → 裝置管理 → 新增裝置
        ↓
按「產生配對碼」
        ↓
A Device Token 驗證
        +
SUPER_ADMIN / 具 Device 管理權限的 ADMIN 人員驗證
        ↓
Email OTP 通過
        ↓
Cloud 才產生一次性 Pairing Code
```

Pairing Code：

- 短效，例如 10 分鐘。
- 只能使用一次。
- 產生時已代表管理員批准新增一台 Device。

B 機流程：

```text
加入既有雲端空間
→ 輸入 Pairing Code
→ Windows 產生新的 Device Token
→ Cloud 建立 Device B 與 Token hash
→ GET /v1/device 驗證
→ 完成
```

B 機 **不再做第二次管理員 OTP**。

目前 prototype 的 `POST /v1/device-pairings` 只依 Device Token 即可發碼，屬 foundation prototype；正式 V3.0 必須在發碼前增加人員授權。

## 10. 全新 B 機與既有單機 B 機

### 10.1 全新 B 機

第一次啟動直接選：

> 加入既有雲端空間

然後使用 Pairing Code 或 Workspace SUPER_ADMIN Email OTP 加入，不建立多餘的 Local SUPER_ADMIN。

### 10.2 B 機已經使用過單機版

典型狀態：

```text
Workspace：已有 X = Cloud SUPER_ADMIN
B 機：已有 Y = Local SUPER_ADMIN
B 機：沒有 Cloud Device identity
```

B 切雲端版時，第一個判斷必須是：

```text
Cloud 有 Workspace
+
本機沒有 Device
```

因此先處理 Device Join：

- Pairing Code；或
- **Workspace X 已登記 Email** 的 OTP。

B 本機 Y 的 Email 不得拿來自我授權加入既有 Workspace。

## 11. Local SUPER_ADMIN Y 加入既有 Workspace 後的角色

Device B 合法加入後，如果該機原本已有唯一 Local SUPER_ADMIN Y：

```text
X = Workspace SUPER_ADMIN
Y = B 機原 Local SUPER_ADMIN
```

Y **自動以 `ADMIN` 身分加入 Workspace**，不使用 `PENDING_ROLE` 阻塞日常工作。

立即結果：

```text
X = SUPER_ADMIN
Y = ADMIN
```

帳號管理中可顯示：

> Y 原為 B 機單機版 SUPER_ADMIN，目前已以管理員身分加入 Workspace。

這只是管理提示，不限制 Y 的正常 ADMIN 權限。

這個自動轉換只適用於「合法加入 Workspace 的既有單機系統中原本唯一的 Local SUPER_ADMIN」，不能泛化成任何本機員工都自動取得 ADMIN。

## 12. 單一 SUPER_ADMIN 與超管移交

### 12.1 單一超管規則

任何時刻都必須維持：

```text
Workspace SUPER_ADMIN count = 1
```

V3.0 不支援多 SUPER_ADMIN，避免衍生互相取消、互相降級與最後無超管等權限問題。

### 12.2 帳號管理增加「移交超管權限」

目前 SUPER_ADMIN 可以對任一 Workspace `ADMIN` 執行：

> 移交超管權限

例如：

```text
目前：
X = SUPER_ADMIN
Y = ADMIN

移交給 Y 後：
X = ADMIN
Y = SUPER_ADMIN
```

### 12.3 必須是單一原子操作

不得實作成兩個獨立步驟：

```text
先把 X 降成 ADMIN
再把 Y 升成 SUPER_ADMIN
```

必須由 Cloud 以同一個原子 state transition／transaction 完成：

```text
X : SUPER_ADMIN → ADMIN
Y : ADMIN       → SUPER_ADMIN
```

這樣任何時刻都不會出現 0 名或 2 名 SUPER_ADMIN。

### 12.4 權限邊界

只有目前的 `SUPER_ADMIN` 可以執行超管移交。

一般 `ADMIN` 不得：

- 把自己升成 SUPER_ADMIN。
- 把其他人升成 SUPER_ADMIN。
- 取消／降低現任 SUPER_ADMIN。

現任 SUPER_ADMIN 本身也不提供獨立「降為管理員」操作；若要降級，唯一方式就是把超管權限移交給另一名 ADMIN。

## 13. Local Y 與既有 Cloud Y 的重複處理

如果 Workspace 本來已經有同一位 Y 的 Cloud Employee，而 B 機 Local SUPER_ADMIN 也是 Y，不建立第二個 Employee。

應透過穩定 Employee identity／明確驗證完成 mapping；姓名／Email 可作提示，但不得只靠姓名自動合併。

Employee 中央化完成後，Cloud `employee_id`／role／enabled 成為跨機權限真相，本機僅保留必要離線／相容資料。

## 14. Email OTP 最低安全要求

正式 Email OTP 至少需要：

- 短效，例如 10 分鐘。
- 單次使用。
- OTP 只保存 hash，不保存明文。
- 錯誤嘗試上限，例如 5 次。
- 重寄 cooldown。
- request rate limit。
- log 不記 OTP。
- Windows 顯示遮罩後 Email，不向未授權 Client 回傳完整 Recovery Email。
- Workspace 已存在時，Recovery OTP 的寄送目的地只能由 Cloud 已登記 Email 決定，不能由 Client 任意指定。

Email 只承載短效 OTP／核准資訊，**不寄 Device Token**。

## 15. Device Token 遺失後舊 Device 的處理

重新加入後不應由系統只靠裝置名稱自動判斷「新 Device 就是舊 Device」。

若舊 Device 仍存在，可由 SUPER_ADMIN／授權 ADMIN 在裝置管理中明確撤銷舊 Device。

```text
舊 Device → revoked
新 Device → active
```

避免誤把仍在使用的另一台裝置自動撤銷。

## 16. V3.0 身分狀態判斷順序

Cloud 模式切換／啟動時先依 Device／Workspace 狀態判斷，不先用 Local SUPER_ADMIN 角色猜 Cloud 權限：

```text
A. Cloud 無 Workspace
   + 本機有 Local SUPER_ADMIN
   → 首次 Workspace 初始化

B. Cloud 有 Workspace
   + 本機有有效 Device Token
   → GET /v1/device 驗證後直接使用

C. Cloud 有 Workspace
   + 本機沒有有效 Device identity
   → 加入既有 Workspace
      - Pairing Code
      - Workspace SUPER_ADMIN Email OTP
```

若 C 的電腦原本有 Local SUPER_ADMIN Y，先完成 Device Join；成功後 Y 才依第 11 節轉成 Workspace ADMIN。

## 17. 實作順序

依風險分批，不一次完成全部：

### Batch 1：安全 bootstrap contract

- Workspace ID／Device ID 由 Cloud 產生。
- Device Token 由 Windows 產生。
- 同一 Pending Token 可安全恢復 timeout／lost response。
- Workspace + 第一台 Device 原子建立。
- 不新增 Employee／Email schema 前先把 contract 做對。

### Batch 2：本機 Pending Device Token

- Token 送出前先以 DPAPI 持久化。
- 記錄 Pending onboarding state。
- 程式重開後能繼續安全 recovery。

### Batch 3：首次 Workspace + SUPER_ADMIN Email OTP

- 讀取既有 Local SUPER_ADMIN Email，不重新輸入。
- Email OTP challenge／verify。
- 新增必要 forward migration，不能改寫 `0001`／`0002`。
- 初始化完成後 X 成為唯一 Workspace SUPER_ADMIN。

### Batch 4：Windows 首次建立 UI

- `尚未建立雲端空間` → 建立流程。
- timeout／重開安全恢復。
- `GET /v1/device` 成功才視為完成。

### Batch 5：既有 Workspace Device Join／Recovery

- Pairing Code。
- SUPER_ADMIN Email OTP recovery。
- Device revoke／rejoin。
- 全新機可直接「加入既有雲端空間」。

### Batch 6：中央 Employee／角色遷移

- 既有 Local SUPER_ADMIN X → Workspace SUPER_ADMIN。
- 既有單機 B 機的 Y 合法加入後 → Workspace ADMIN。
- Employee identity mapping／中央 role／enabled／lockout。

### Batch 7：帳號管理超管移交

- 單一 SUPER_ADMIN invariant。
- `TransferSuperAdmin` 原子操作。
- 帳號管理「移交超管權限」。

### Batch 8：Pairing Code 前的人員授權

- 有效 Device + SUPER_ADMIN／指定 ADMIN OTP 通過後才可產生 Pairing Code。
- B 機使用已授權的一次性 Pairing Code 後不再重做管理員 OTP。

## 18. 目前不做／不得誤做

- 不因 Token 遺失重建 Workspace。
- 不允許本機 Local SUPER_ADMIN 自行取得既有 Workspace 的加入授權。
- 不允許第二名 SUPER_ADMIN 暫時存在。
- 不做 `PENDING_ROLE` 阻塞既有單機超管加入後的日常工作；Y 直接以 ADMIN 工作。
- 不寄送 Device Token 到 Email。
- 不把 Recovery Email 改成使用者可任意輸入的既有 Workspace recovery 目的地。
- 不把 Cloudflare emergency 維運修改 Recovery Email 做成一般 Windows API。
- 不回寫已執行的 `0001`／`0002` migration。
- 不在這一階段做多公司／`company_id`。
- 不因 Cloud 身分工程改變既有 AMEGO 發票安全不變量。

## 19. 最終定案摘要

> **Workspace 建立後不因 Device 遺失而重建。**

> **第一次從單機版建立 Cloud Workspace 時，直接沿用既有 Local SUPER_ADMIN Email，Email OTP 通過後建立 Workspace；不重新輸入超管 Email。**

> **Workspace ID／Device ID 由 Cloud 產生；Device Token 由 Windows 先產生並以 DPAPI Pending 保存，Cloud 只存 hash。**

> **既有 Workspace 的 Device 加入授權一定來自 Workspace：Pairing Code 或 Workspace SUPER_ADMIN Email OTP，不來自新機自己的 Local SUPER_ADMIN。**

> **既有單機 B 機合法加入 Workspace 後，原 Local SUPER_ADMIN Y 自動成為 Workspace ADMIN，可立即工作。**

> **Workspace 永遠只有一名 SUPER_ADMIN；換人只能由現任 SUPER_ADMIN 執行原子的「移交超管權限」，原超管降為 ADMIN、指定 ADMIN 升為 SUPER_ADMIN。**

> **所有 Device Token 與原 Recovery Email 同時遺失的雙重災難，最後由 Cloudflare／reference backend 管理端人工修改 Recovery Email，再回正常 OTP Recovery；V3.0 不增加第四套 emergency secret。**
