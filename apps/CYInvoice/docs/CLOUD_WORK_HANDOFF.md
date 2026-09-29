# CYInvoice Cloud Work Handoff

更新日期：2026-09-29

本文件只保存**目前工作狀態、最新已確認決策與下一步**。歷史細節由 Git history、PR #73／#100／#121、`CLOUD_IDENTITY_LIFECYCLE.md`、`CY_ID_INTEGRATION.md`、`CLOUD_ARCHITECTURE_STATUS.md` 與 `TODO.md` 保留；不要再以較舊階段敘述覆蓋本文件目前狀態。

永久規則仍依：`REPOSITORY_RULES.md` → `REPO_POLICY.md` → `apps/CYInvoice/PROJECT_RULES.md`。

## 1. 目前 Git / 版本基準

- Repository：`simonliu1118-byte/CYapps`
- 專案：`apps/CYInvoice/`
- Draft PR：#121
- Branch：`cyinvoice/feat-cloud-security-audit`
- Base：`cyinvoice/fix-v264-build2-ui-review-details`（PR #100）
- 目前工程版本：**CYInvoice V2.6.6 Build 4**
- Cloud：`0.8.5`
- API：`1`
- D1 migration：`0009`
- storage Schema：`9`
- 未 merge、未 tag、未正式 Release。

Build 4 的 Windows / Cloud CI 基準為 workflow Run #255：TypeScript、Worker dry-run、D1 migration validation、跨版本 contract regression、Windows x64 build、WinForms startup smoke、Windows Cloud contract tests 與 engineering package 均成功。

Engineering artifact：`CYInvoice_cloud-foundation_engineering-run255`
SHA-256：`5ba818446d357e054dbefe13b04bca2cb6ba6f43637fbc85ccfe431bebc1ea43`

本輪 2026-09-29 只整理設計文件；**尚未修改 CYInvoice source、未升 Build、未部署新 Cloud、未做 CY ID binding**。

## 2. Development remote 現況

2026-09-28 staged deployment 已完成：

1. 舊 remote 為 Cloud 0.8.3 / API 1 / Schema 8。
2. canonical migration `0009_device_invites_and_audit.sql` 由 Wrangler 套用一次成功，沒有手動 SQL。
3. migration 後原 Workspace / Device / Employee 關聯正常。
4. repository Wrangler deploy 已將 `cyinvoice-cloud-dev` 部署至 Cloud 0.8.5。
5. 沒有再次執行 migration。

目前 `/v1/health` 已確認：

- service：`cyinvoice-cloud`
- storage：`ok`
- Cloud：`0.8.5`
- API：`1`
- legacy compatibility `schemaVersion=8`
- actual `storageSchemaVersion=9`
- minimum client：`2.6.5`
- recommended client：`2.6.6`

## 3. A / B 實機驗收狀態

### A 機

已實機通過：

- PR #100 V2.6.5 Build 4 / Run229 + 原 `Data` 恢復 development Cloud。
- PR #121 V2.6.6 Build 4 / Run255 對舊 Schema 8 Worker 向下相容。
- migration `0009` 後，Schema 9 + 舊 Worker 0.8.3 中間狀態正常。
- Worker 0.8.5 部署後，Cloud 連線、中央帳號、已開立清單、重新整理、完整關閉再重開正常。

### B 機

**配對碼加入既有 Workspace 已實機通過。**

已確認：

- B 使用乾淨 Run255，不複製 A 機 `Data`。
- 配對碼可解析既有 Workspace，使用者先確認 Workspace 名稱再加入。
- B 加入後 Cloud 模式／中央帳號正常。
- B 完整關閉再重開仍可連回原 Workspace。

仍待：

- 邀請碼加入。
- 邀請撤銷／重寄。
- result-unknown / retry-safe recovery。
- backend 對 B Device / audit event 的唯讀驗證。

Run229 先保留作 rollback 基準，待新裝置加入與後續 identity flow 再穩定一階段後再決定是否刪除。

## 4. 目前已確認的 Employee snapshot defect

實機重現：

1. A 機新增中央 Employee 成功。
2. B 機在既有 cache 尚未刷新時，帳號管理看不到新 Employee。
3. B 完整關閉再重開後，新 Employee 正常出現。

因此已確認目前其他 Device 存在 **Cloud Employee snapshot refresh timing 缺口**，中央資料本身沒有遺失。

目前程式仍有權限驗證路徑直接使用本機 `CloudEmployeeCacheStore`；不能只修帳號管理開窗刷新。

Built-in Cloud 已定案目標：

```text
需要 Employee 密碼／權限驗證
  ↓
取得最新 Built-in Cloud authority / snapshot
  ↓
以 current credential / role / enabled 驗證
  ↓
更新本機 cache
  ↓
只授權此次操作
```

新建 Employee、role / enabled / password 變更不得要求其他 Device 等 5 分鐘背景同步或重開程式。

只有 Cloud 真正不可達時才使用最後可信 protected offline cache。

## 5. Offline cache 完整性風險與決策

目前 Built-in Cloud Employee cache 的 credential verifier 有 Windows secure storage / DPAPI 保護；但 SQLite 內的 `role`、`enabled` 等 metadata 沒有整筆 server signature。

目前決策：

- 不在現在導入完整 server-signed snapshot。
- Cloud 在線時以最新 authority 消除一般操作時對被改 cache 的信任。
- 真正離線時仍使用最後 cache，接受目前剩餘的本機竄改風險。
- **Server-signed snapshot／等效完整性簽章列入長期 TODO，非目前 Build / V3 上線阻塞。**
- 若未來出現實際竄改事件、威脅模型提高或有正式稽核需求，再提高優先級。

## 6. Cloud → Local 已定案為破壞性本機重置

「切回單機版」不再是單純把 `CloudMode` 改成 `LocalOnly`，也不允許舊 Local authority 復活。

```text
使用者選擇切換單機版
  ↓
第一次警告：這台電腦的 CYInvoice 本機資料會全部清除
  ↓
第二次明確確認：清除後不可由本機復原，需重新建立單機版 SUPER_ADMIN
  ↓
停止同步／敏感操作
  ↓
依正式 Device revoke / retire 規則處理目前 Device
  ↓
清除目前電腦的 Data / Cache / settings / Local EmployeeStore /
Cloud Employee cache / Cloud identity / Device Token / pending state
  ↓
重新啟動成首次使用
  ↓
選擇單機版並重新建立 Local SUPER_ADMIN
```

此流程只清除目前 Windows 電腦的 CYInvoice 本機資料，不刪 Cloud Workspace、其他 Device 或中央 Employee。

之後再次加入 Cloud 時，視為全新裝置並建立新的 Device identity；不得復活舊 Device Token、舊 cache 或舊 Local authority。

若退出 Cloud 前無法安全完成必要 revoke / retire，實作必須 fail-closed 或保留可恢復狀態。

## 7. Device 管理目前決策

Device 正式行為採 revoke / retire，不直接刪除歷史 row：

- Device 歷史紀錄可保留。
- revoked Device Token 立即失效。
- SUPER_ADMIN / 授權管理流程才能撤銷。
- 撤銷需留下 security audit event。
- 日後同一電腦重新加入，建立新的 Device identity，不復活舊 token。

目前 Device revoke UI/API 尚未完成。

## 8. 新裝置加入正式路徑

正式新裝置加入只保留：

1. **立即配對**：A 端超管驗證後產生 API URL + 約 10 分鐘一次性 pairing code；B 端確認 Workspace 名稱後加入。
2. **Email 新裝置邀請**：A 端寄 API URL + 72 小時一次性 invitation code 至已驗證超管 Email；可撤銷／重寄；B 端使用 invitation code + SUPER_ADMIN credential，確認 Workspace 名稱後加入，不再寄第二封 OTP。

Workspace ID + SUPER_ADMIN 直接加入已淘汰，不得恢復成第三條一般入口。

## 9. Brevo / Email 觀察

實機曾觀察到 Employee 驗證信約 7 分鐘才收到。

目前 Worker 是直接呼叫 Brevo transactional email API，沒有刻意排程 7 分鐘後才寄的程式邏輯。若後續要定位延遲，可加入不含 Email / OTP / token 的 provider timing telemetry，只記 provider accepted latency 與結果。

這項目前不是 identity 核心阻塞。

## 10. 2026-09-29：CY ID / Self-hosted 架構已定案

專門設計基準：`CY_ID_INTEGRATION.md`。

CYInvoice 必須保留三種正式使用方式：

```text
Local
Built-in Cloud / Self-hosted
CY ID Cloud
```

### Local

- Local EmployeeStore 是唯一 authority。
- 不建立 Cloud Workspace。
- 不需要 CY ID。

### Built-in Cloud / Self-hosted

- 保留目前 CYInvoice 自己的 Workspace、Employee authority、Credential、Email OTP、Device 與權限庫。
- 第三方公司可以依 User Manual 在自己的 Cloud 環境部署，不需要 CY ID。
- Built-in Cloud 是正式一等模式，不是 CY ID fallback。

### CY ID Cloud

- CYInvoice Workspace 繼續存在，負責 Device／Pairing／Token／Sync／Work Item／Invoice business state。
- CY ID Workspace 是 Employee／Credential／共通身分範圍；兩種 Workspace 以 binding 關聯，不合併成同一個實體。
- CY ID 成為 Employee／Credential authority；CYInvoice 不維護第二套中央帳號 authority。
- CYInvoice 的「帳號管理」功能在 CY ID 模式**直接隱藏**；帳號 CRUD／Email／Password／SUPER_ADMIN／CYInvoice access／CYInvoice role 統一由 CYWEB / CY ID 帳號中心管理。
- CY ID 的 Group、App Access schema、管理 UI 與內部實作不在本工作線決定。
- App Access 是上層入口控制；CYInvoice 不建立第二套 App Access 管理頁。
- Device lifecycle 永遠屬於 CYInvoice，不交給 CY ID。

## 11. Role 命名已定案

CYInvoice 正式 role 改為：

```text
SUPER_ADMIN
ADMIN
USER
```

`EMPLOYEE` 不再作為 role 名稱。

CY ID 對接時直接 1:1：

```text
CY ID SUPER_ADMIN → CYInvoice SUPER_ADMIN
CY ID ADMIN       → CYInvoice ADMIN
CY ID USER        → CYInvoice USER
```

`SUPER_ADMIN` 仍然是 `SUPER_ADMIN`，不降階、不做「視同 ADMIN」。

目前實際使用資料沒有已持久化的 `EMPLOYEE` role 需要相容，因此 **不需要 role 資料 migration**；後續只需修改 source/schema fixtures/tests/docs/UI 命名。

## 12. CY ID Migration 與 provider 擴充範圍

目前不為 Built-in → CY ID 建立複雜 migration framework。

現有 development Workspace 實際使用資料少；AMEGO 仍是發票官方真相，後續正式接回光貿可依既有兩期同步策略重建發票資料。真正 cutover 前只需做 acceptance check，確認沒有只能存在本機、無法由 AMEGO 重建的 pending／結果不明狀態。

也不需要預先支援 Entra ID、LDAP、Google Workspace 或任意第三方 Identity Provider。CYInvoice 第一版只做 Built-in + CY ID；其他人若 fork 後需要不同 provider，自行擴充即可。

## 13. CY ID Offline 尚待技術設計

產品方向已定，但 CY ID 模式 Windows Offline credential/cache 的協定仍屬後續實作階段技術設計。

不得：

- 直接讀 CY ID D1；
- 要求 CY ID 將其 credential verifier 當成一般 consumer data 回傳；
- 產生 Built-in + CY ID 雙 authority。

必須維持：

- Online 時最新 CY ID authority 為準；
- Offline 只使用最後可信 protected local cache；
- reconnect 後最新 CY ID authority 重新生效。

目前不提前鎖死 API 形式。

## 14. 下一輪規劃前工作清單

目前**只完成定案與文件整理，尚未開始以下 source 修改**：

1. Built-in Cloud 在線 Employee execution-time authentication / latest snapshot refresh。
2. Device revoke / retire API + UI + token invalidation + audit。
3. Cloud → Local destructive reset + 雙重確認 + 本機全清 + first-run restart。
4. 將 CYInvoice role 正式由 `EMPLOYEE` 改名為 `USER`。
5. 邀請碼新裝置加入完整實機驗收。
6. 邀請撤銷／重寄／result-unknown recovery 驗收。
7. A/B Built-in Employee CRUD、role、enabled、password 即時與背景同步驗收。
8. Cloud Offline → reconnect 行為驗收。
9. Security audit viewer。
10. all-device-token-loss Recovery Device flow。
11. 等 CY ID consumer contract 穩定後，再進入 CYInvoice CY ID 對接工作。
12. Offline cache server signature：**長期擱置 TODO，非目前阻塞。**

下一步應先討論上述項目的依賴關係、版本範圍與測試順序，再開始程式修改。

## 15. 安全與工作邊界

- Public repo 不得提交真實 Employee Email、runtime secret、Device Token、OTP、密碼、AMEGO App Key、正式營運資料。
- Device identity 與 Employee identity 分離；Device Token 不代表管理員身分。
- 每個模式只能有一套 Employee authority。
- Cloud Employee 全域異動仍為 Online-only。
- Cloud Offline 不得回復舊 Local EmployeeStore 當第二套 authority。
- API timeout / unknown result 不得盲目重送高風險操作。
- Cloud 是 coordination / identity / device service；AMEGO 仍是發票／作廢／折讓官方結果唯一真相。
- 未經使用者明確授權，不 merge、不 tag、不建立正式 Release。