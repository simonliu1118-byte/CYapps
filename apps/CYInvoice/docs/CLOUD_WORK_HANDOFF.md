# CYInvoice 現行工作交接（2026-10-14）

> **單一現況入口。** 永久規則以根 `REPOSITORY_RULES.md`、`REPO_POLICY.md`、`apps/CYInvoice/PROJECT_RULES.md` 為準；此處只記錄正在執行的工作、驗證與操作界線。未完成項目統一在 [TODO.md](TODO.md)；現場驗收使用 [RC_TEST.md](RC_TEST.md)；歷史版本由 [CHANGELOG](../CHANGELOG.md)／[MIGRATION_HISTORY](MIGRATION_HISTORY.md) 與 Git PR 保存，不再在交接文件重複所有歷史對話。

## 1. 清楚區分三條程式線

| 線別 | 來源及版本 | 狀態／證據 |
| --- | --- | --- |
| 已公開正式 Windows | [V2.6.19](https://github.com/simonliu1118-byte/CYapps/releases/tag/cyinvoice-v2.6.19)，`main` | 2026-10-13 已發行；未被其他 Draft 改寫 |
| AutoInput 工程測試 | [CYInvoice Draft #445](https://github.com/simonliu1118-byte/CYapps/pull/445)，`cyinvoice/feat-autoinput-interface`，**V2.6.20 Build 0** | [Windows Build #38554728718](https://github.com/simonliu1118-byte/CYapps/actions/runs/38554728718)／Governance／Cloud checks 成功；[工程測試包](https://github.com/simonliu1118-byte/CYapps/actions/runs/38554728718/artifacts/11889940554)，Actions Artifact 只保留 3 天；未合併、不等於正式 Release |
| V3 災難復原／多機協同 | [CYInvoice Draft #440](https://github.com/simonliu1118-byte/CYapps/pull/440)，`cyinvoice/v3-recovery-invitation-schema` | 從正式線獨立開發、Draft；原 D1 migration 0014 與 optional issuer maintenance **尚未實際套用**，Windows／真 Email／多機 DR 不得視為已驗收 |

對應 CYWEB 管理入口：[CYWEB Draft #151](https://github.com/simonliu1118-byte/chihyuan-web/pull/151)，`cyweb/feat-cyinvoice-recovery-auth-gate`，head `c155baf4e0fda819d97fd950bff7cfeb55e1e0a5`。該邊已修 Operation ID 的 sessionStorage 定位、頁面恢復／查回、共用確認 Dialog、通用 failed 不冒充「已撤銷」，source CI 已通過，但**未部署**。跨 repo 交接：[CYWEB CYINVOICE_RECOVERY_HANDOFF](https://github.com/simonliu1118-byte/chihyuan-web/blob/cyweb/feat-cyinvoice-recovery-auth-gate/docs/development/CYINVOICE_RECOVERY_HANDOFF.md)。

## 2. AutoInput 已交接，並非目前 V3 的阻塞

- **V2.6.20** 已實作 `--autoinput-query`／`--autoinput-issue`、同 Windows 使用者及 portable 目錄內具名管道、原 `InvoiceService`／MO 店+／酷澎匯入、安全防重送、result JSON 原子寫入；CI/封裝通過。沒有正式光貿開票、雙行程／Excel COM 真機、MO→ERP E2E 證據。
- [AutoInput 端已收到專用交接文件](https://github.com/simonliu1118-byte/CYapps/blob/cyerp-auto-input/shopee-batch/apps/CYERPAutoInput/docs/CYINVOICE_IMPLEMENTATION_HANDOFF.md)，位於 AutoInput [PR #424](https://github.com/simonliu1118-byte/CYapps/pull/424)。完整契約以 #445 的 `apps/CYInvoice/docs/AUTOINPUT_INTERFACE_HANDOFF.md` 為準。
- 接口仍須由 AutoInput 用獨立測試目錄的實際 EXE 完成聯測；不可在正式 ERP 自動開票啟用前省略此 gate。AutoInput 的返修保持 **V2.6.20 Build N**，不可因此改動 #440 的 V3 Device／D1 schema 或回頭重做 CYID migration。

## 3. V3 當前已寫入 Draft 原始碼（未部署）

1. CYWEB：CYID verified SUPER_ADMIN／CSRF／Core Session gate，再由 CYInvoice private named `CyInvoiceRecoveryEntrypoint` 以自己的 `CYINVOICE` Application 和兩種 Workspace 即時再授權；簽發、操作狀態查回、撤銷分開。
2. CYInvoice Worker：`recovery-issuer.ts` 原 Operation ID、每 Workspace 單一進行中邀請、一次性雜湊碼、寄信接受後的 sent guard／Audit，送信或 D1 回覆不明維持 pending；不重寄、不自動新簽。
3. Windows：**首次啟動只有「使用單機版／直接加入雲端」兩個選項。** 災難復原在 `CloudDirectJoinForm` 內的「災難復原／繼續災難復原」按鈕，不是首次啟動第三選項。獨立 `CloudRecoveryForm`、HTTPS preview／claim、DPAPI Pending Token、同一 Token readback；一般 JOIN 不撤銷裝置。
4. **成功接管新裝置時**才在同一 D1 原子交易撤銷原 Workspace **全部舊 active Device**、使其餘邀請／Pairing 失效、逐項 Audit；失敗應整筆 rollback。原 Pairing／一般 JOIN 寫入時已有 Device active 的 D1 guard，不代表已攔住所有**跨服務、in-flight 業務 mutation**。
5. schema：forward 0014 和 optional CYID_ONLY recovery issuer 只在 Draft。CYID_ONLY 舊 Built-in D1 四張帳號／轉換表已於 2026-10-13 完成核准的原環境退役及私有備份／資料保留驗證，**不可重跑**；其他公司的新 Built-in／Self-hosted D1 必須保持支援。

設計參考：[CLOUD_DISASTER_RECOVERY](CLOUD_DISASTER_RECOVERY.md)、[CLOUD_IDENTITY_LIFECYCLE](CLOUD_IDENTITY_LIFECYCLE.md)、[CY_ID_INTEGRATION](CY_ID_INTEGRATION.md)。本公司原 development Cloud 基準 **0.9.3／API1／storage13／CYID_ENABLED=true／CYID_ONLY**，不是 V3 Draft deployment。

## 4. 回到原本工作的下一個切入點

**CYWEB→Invoice 第一阻塞已於 V3 Draft source 補齊（待 CI／雙端聯測）：**依 #151 的交接，`recovery-issuer.ts` 明確區分 `revoked`（只能由 `revoked_at` 或成功受 guarded D1 batch 撤銷證實）與 `failed`（consumed／expired／不存在等）；同操作重複 revoke 及失去 DB 回覆後的 status readback 可返回 `revoked`；revoke guard 同時比對 `consumed_at`／`expires_at`，防 claim 或過期競態。已加入 synthetic SQLite 測試：撤銷重讀、consumed、expired、D1 COMMIT 後回覆遺失、claim 與 revoke 競態；**尚未宣稱真 D1／Windows／CYWEB E2E 驗收完成**。CYWEB #151 需核對最新版契約。

**Work Item 最小核心（本輪 Draft，非已上線）：**新**待審草案** `cloud/drafts/0015_work_items.sql` 一張表，`work-items.ts` 建立／open→review→closed CAS、同 D1 write-time Device fence，僅存雜湊識別，不觸發光貿操作；雙 profile 合成測試加入 Cloud CI。下一步才接受控 API、Windows UI、正式權限與操作稽核；不把這版解讀為正式服務已上線。

**後續按序：** 已先在既有 Device 撤銷 endpoint 加 D1 commit-time 的操作裝置 active + Built-in 管理員 credential guard，並加 synthetic late-revoke regression；這不是尚未建立的 Work Item／光貿跨服務 Side Effect 的完整 fencing。V3 Work Item／revision 的 execution-time Device fencing／epoch，正式 Cloudflare Binding／原 D1 schema／Email 設定的**唯讀**核對與 0014/maintenance rollback/readback 計畫，DR-01～DR-13 的跨服務、多機、Windows 及真 Email 驗收。V3 折讓人工歷史欄位缺漏留在 Work Item／Audit 正式重做，不當作舊 V2 已修復。

## 5. 禁止跨越的界線

- #440／#151／#445 均是 Draft，不自行 merge、正式 Release、正式 API deploy、Cloudflare D1 寫入、寄真 Email 或真 Device 全數撤銷。
- 不建立／上傳／部署新的 Cloudflare **測試 Worker**、version 或 handler，也不重試先前刪除失敗的測試 Worker；V3 可在 source／synthetic SQLite／Windows CI 上測試。
- CYID 是中央人員身份權威，CYInvoice 業務 Workspace／Device／Work Item 留自己的 D1；不可把本公司舊 Built-in Account D1 恢復當 CYID 的 fallback。真實 Secret、Workspace ID、Token、備份、客戶資料不入 Public Git。
- **每一份 CI 成功僅證明相應 commit 的自動驗證**，不得冒充既有 production Cloud、真 Email、多人多裝置或客戶資料已驗收。
