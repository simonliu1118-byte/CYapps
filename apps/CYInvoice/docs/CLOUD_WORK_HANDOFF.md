# CYInvoice Cloud Work Handoff

更新日期：2026-09-28

本文件只保存**目前工作狀態、最新已確認決策與下一步**。歷史細節由 Git history、PR #73／#100／#121、`CLOUD_IDENTITY_LIFECYCLE.md`、`CLOUD_ARCHITECTURE_STATUS.md` 與 `TODO.md` 保留；不要再以較舊階段敘述覆蓋本文件目前狀態。

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

## 2. Development remote 現況

2026-09-28 staged deployment 已完成：

1. 先唯讀確認舊 remote 為 Cloud 0.8.3 / API 1 / Schema 8。
2. repository canonical migration `0009_device_invites_and_audit.sql` 由 Wrangler 套用一次成功，沒有手動 SQL。
3. migration 後原 Workspace / Device / Employee 關聯正常。
4. 再以 repository Wrangler deploy 部署 `cyinvoice-cloud-dev` 至 Cloud 0.8.5。
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

下列均已實機通過：

- PR #100 V2.6.5 Build 4 / Run229 + 原 `Data` 恢復舊 development Cloud。
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

因此已確認目前 B 等其他 Device 存在 **Cloud Employee snapshot refresh timing 缺口**，中央資料本身沒有遺失。

目前程式的 Cloud Employee 權限驗證仍有路徑會直接使用本機 `CloudEmployeeCacheStore`；不能只修「帳號管理開窗刷新」。

### 已定案的目標行為

Cloud 可連線時：

```text
需要 Employee 密碼／權限驗證
  ↓
取得最新 Cloud Employee authority / snapshot
  ↓
以目前 Cloud credential / role / enabled 狀態驗證
  ↓
更新本機 cache
  ↓
只授權此次操作
```

也就是：

- 新增 Employee 後，其他 Device 下一次需要驗證時即可立即工作。
- role / enabled / password 變更亦應在其他 Device 下一次驗證時生效。
- 不得要求等待 5 分鐘背景同步或重開程式。

只有 Cloud **真正不可達**時，才使用最後一次成功同步的 protected offline credential cache。

## 5. Offline cache 完整性風險與決策

目前 Cloud Employee cache 的 credential verifier 有 Windows secure storage / DPAPI 保護；但 SQLite 內的 `role`、`enabled` 等 metadata 沒有整筆 server signature。

因此若有人能直接修改本機 SQLite，理論上可竄改離線 cache metadata。這項風險已討論，但目前決策是：

- 不在現在導入完整 server-signed snapshot。
- Cloud 在線時以最新 Cloud authority 消除一般操作時對被改 cache 的信任。
- 真正離線時仍使用最後 cache，接受目前剩餘的本機竄改風險。
- **Server-signed snapshot／等效完整性簽章列入長期 TODO，非目前 Build / V3 上線阻塞。**
- 若未來出現實際竄改事件、威脅模型提高或有正式稽核需求，再提高優先級。

## 6. Cloud → Local 已重新定案為破壞性本機重置

「切回單機版」不再是單純把 `CloudMode` 改成 `LocalOnly`，也不允許舊 Local authority 復活。

正式目標流程：

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
選擇單機版
  ↓
重新建立 Local SUPER_ADMIN
```

此流程只清除**目前這台 Windows 電腦的 CYInvoice 本機資料**：

- 不刪 Cloud Workspace。
- 不刪其他 Device。
- 不刪中央 Employee。

之後如果這台電腦再選雲端版：

- 視為新的 Local authority。
- 重新走正常 Local → Cloud transition。
- 建立新的 Device identity。
- 不得復活舊 Device Token、舊 Cloud cache 或舊 Local authority。

Device revoke / retire 因此不只用於測試 Device 清理，也是 Cloud → Local 安全退場的必要基礎。

若退出 Cloud 前無法安全完成必要 revoke / retire，正式實作必須 fail-closed 或保留可恢復狀態；不可先刪本機唯一 Device Token，再留下無法管理的 active Device。

## 7. Device 管理目前決策

Device 不採直接刪除歷史 row 的方式；正式行為採 revoke / retire：

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

目前 Worker 是直接呼叫 Brevo transactional email API；沒有刻意排程 7 分鐘後才寄的程式邏輯。若後續要定位延遲，可加入不含 Email / OTP / token 的 provider timing telemetry，例如只記 provider accepted latency 與結果；不把敏感內容寫入 log。

這項目前不是 identity 核心阻塞，先記錄觀察結果，後續再視需要診斷 Brevo / 收件端 delivery latency。

## 10. 下一輪規劃前的工作清單

目前**只定案與整理，尚未開始實作以下新修改**：

1. Cloud 在線 Employee execution-time authentication / latest snapshot refresh。
2. Device revoke / retire API + UI + token invalidation + audit。
3. Cloud → Local destructive reset + 雙重確認 + 本機全清 + first-run restart。
4. 邀請碼新裝置加入完整實機驗收。
5. 邀請撤銷／重寄／result-unknown recovery 驗收。
6. A/B Employee CRUD、role、enabled、password 即時與背景同步驗收。
7. Cloud Offline → reconnect 行為驗收。
8. Security audit viewer。
9. all-device-token-loss Recovery Device flow。
10. Offline cache server signature：**長期擱置 TODO，非目前阻塞。**

下一步應先討論上述項目的依賴關係、版本範圍與測試順序，再開始程式修改。

## 11. 安全與工作邊界

- Public repo 不得提交真實 Employee Email、runtime secret、Device Token、OTP、密碼、AMEGO App Key、正式營運資料。
- Device identity 與 Employee identity 分離；Device Token 不代表管理員身分。
- Cloud Employee 全域異動仍為 Online-only。
- Cloud Offline 不得回復舊 Local EmployeeStore 當第二套 authority。
- API timeout / unknown result 不得盲目重送高風險操作。
- Cloud 是 coordination / identity service；AMEGO 仍是發票／作廢／折讓官方結果唯一真相。
- 未經使用者明確授權，不 merge、不 tag、不建立正式 Release。
