# CYInvoice V3.1：非折讓跨機協作交接

更新日期：2026-10-18（Asia/Taipei）

## 現行狀態（以 GitHub main 為準）

- **已合併的原始碼基準**：`CYapps/main` `d3ce35560bc9168ff38ca176397d49e796d49fa9`，最後一筆為 3.1-C3 [#491](https://github.com/simonliu1118-byte/CYapps/pull/491) 的稽核草稿合併；正式 Windows Release 仍是 [V3.0.0 Build 0](https://github.com/simonliu1118-byte/CYapps/releases/tag/cyinvoice-v3.0.0)（`VERSION=3.0.0`, `BUILD=0`）。**3.1 並未發布**。
- **來源與避免重工**：沿用舊 Draft [#440](https://github.com/simonliu1118-byte/CYapps/pull/440) 的可用片段，但不可整包合併。原本重複的 #481、#484 已關閉，#488 未合併；已驗證的正式 Source 請只看 #482／#483／#485／#486／#487／#489／#490／#491 的 `main` 結果。
- **3.0 前置**：使用者明確回報 3.0 Cloudflare 安全升級已完成（本對話沒有實際本機 MCP readback）；真 Email 與雙 Windows 復原 DR 實測往後安排，不阻擋 3.1 Source。**不可重跑 3.0 migrations／CYID 裝置帳號清理**。
- **3.1 僅非折讓**：`VOID` 紙本未收回的人工覆核、`UPLOAD_ISSUE` 上傳問題／官方狀態不明的例外通報、管理員跨機待辦／人工封存／Audit。Work Item 並非官方 AMEGO 發票／作廢狀態；不自動重送或批量複製發票、買方資訊。3.2 才處理 g0401/g0501 與折讓 API，3.3 其他延後。

## GitHub 已交付並合併（全部 Source／CI，不代表 Cloudflare 已啟用）

| 片段 | 已合併 PR | 內容 |
| --- | --- | --- |
| 3.1-A | [#482](https://github.com/simonliu1118-byte/CYapps/pull/482) | `cloud/src/work-items.ts`、非折讓 D1 0016 草稿、Workspace／Device fencing、revision CAS 與隔離 SQLite |
| 3.1-B1 | [#483](https://github.com/simonliu1118-byte/CYapps/pull/483) | CYID SUPER_ADMIN／ADMIN 唯讀管理員 API；schema16／flag 預設關閉，讀後重驗權限 |
| Windows 唯讀 | [#485](https://github.com/simonliu1118-byte/CYapps/pull/485) | `CloudWorkItemClient` Get／List、分頁及合成契約 |
| 3.1-B2 API | [#486](https://github.com/simonliu1118-byte/CYapps/pull/486) | USER 作廢申請／上傳問題報告、ADMIN／SUPER_ADMIN 管理 CAS 覆核／封存 |
| Windows 寫入 | [#487](https://github.com/simonliu1118-byte/CYapps/pull/487) | VOID／UPLOAD_ISSUE 回報、管理 transition、嚴格非官方 receipt 與結果不明處理 |
| 3.1-C1 | [#489](https://github.com/simonliu1118-byte/CYapps/pull/489) | WinForms「跨機待辦」管理員視窗與 Local／CYID 模式隔離 |
| 3.1-C2 | [#490](https://github.com/simonliu1118-byte/CYapps/pull/490) | 原「上傳問題」視窗明確回報、穩定 HMAC opaque ID、保護本機識別資訊 |
| 3.1-C3 | [#491](https://github.com/simonliu1118-byte/CYapps/pull/491) | 0016 草稿內 append-only `work_item_audit_events` 與 DB trigger，角色／Device／revision／人工處理原因 |

- Cloud Check／隔離 SQLite／CYID 合成測試已通過；[#490](https://github.com/simonliu1118-byte/CYapps/pull/490) 的 Windows x64 Build、Governance／Cloud Check 全通過，[#491](https://github.com/simonliu1118-byte/CYapps/pull/491) 的精確 head Cloud／Governance／部署預檢通過，合併 `main` 的 [Cloud Check #39112276145](https://github.com/simonliu1118-byte/CYapps/actions/runs/39112276145) 通過。
- 部分舊交接曾寫「3.1-B2 待 CI」「WinForms 尚無操作按鈕」「B1 只有 get/list」「SQL 沒有 Audit」，**已被後續合併取代**；請勿據此重寫 Work Item 程式。

## 2026-10-18 Source 審查補修（PR [#493](https://github.com/simonliu1118-byte/CYapps/pull/493)）

- 沿既有 C1／C2／C3 實際程式核對本機非折讓回報、匿名摘要、雙次 CYID 身分重驗、Device SQL 防撤銷、Workspace 隔離、主管 CAS 流程與回覆不明不重送，保留原本 SQLite／Cloud contract 測試。
- Windows 跨機待辦 smoke 改為真正控制項（不讀正式環境設定、Token 或網路），並在失去權限、查詢失敗或覆核結果不明後清除舊清單及操作狀態；完整 WinForms／Cloud／Governance exact-head CI 仍以 PR 檢查結果為準。
- 此批只改原始碼和文件，沒有改 SQL 草稿／Cloud Worker／正式發行。真人角色、雙 Windows 和 DPI 實測仍是後續驗收。

## V3.1 結果不明的 ID 查回（工程 PR [#494](https://github.com/simonliu1118-byte/CYapps/pull/494)）

- 上傳問題回報及主管覆核若發生斷線／D1 回覆不明，既有程式會保留原工作項目 ID，不得再次送出業務 mutation。原管理員視窗只有分頁清單，難以查回已封存或不在目前篩選的項目；本 PR 加入直接輸入 ID 的**純查詢**入口。
- 每次直接查回都走同一個 CYID 當次登入、active Device／Workspace 防護及後端 `get` 權限重驗；僅顯示人工狀態及 revision，不涉及正式發票查詢、重送或變更，禁止用清單快取充當最新權限。若是原視窗人工操作結果不明，會自動填入原 ID 供查回。
- 3.1 的 schema16 原 D1、Worker feature flag、真雙 Windows／不同角色驗收一律留最後本機 MCP 與實機階段。本 PR 是否合併以精確 Windows／Cloud／Governance CI 為準。

## 接下來（先 Source；D1 一律排最後）

1. **先完成 Source review 與跨模組回歸**：核對使用者本機手動回報→管理員遠端待辦→兩次 CAS 覆核／封存的端到端契約；確認每次 CYID/Device 重新驗權、使用者不能替管理員核准、HTTP redirect 不洩漏密碼、重複 ID／網路回覆不明只查回、不重送 AMEGO、原 Local 發票／人工折讓不回歸。有缺口再建立單一短期 branch／PR 修正；沒有缺口勿重新做一套。
2. **Windows 實際 UI 檢視**：確認緊湊版面、按鈕／列表密度及可操作性；必要時做已知差異修正。CI 的 WinForms smoke 不等同使用者真機 UI 驗收。
3. **最後才執行 3.1-D D1／Cloudflare 操作**：由使用者**本機 MCP** 核對原 CYInvoice 業務 D1 的備份可回復性、既有 schema15/CYID_ONLY、同一 Workspace／Device，審核 `cloud/drafts/0016_non_allowance_work_items.sql` 後才升成 canonical migration，受控 apply；再更新原 Worker、`SCHEMA_VERSION>=16` 與 `WORK_ITEMS_ENABLED=true`，核對私人 readback。**此批文件變更不執行任何一步**。
4. **最後真人跨機驗收與 Release 判定**：本機報告、兩台 Windows／不同 CYID 角色、網路中斷／Token 撤銷／CAS 競態、Audit 不可篡改與 AMEGO 不重送。正式發行須使用者**當次明確授權**，不得把 V3.0.0 的授權沿用到 V3.1。

## 安全界線

- 3.1 的 SQL **仍在 `cloud/drafts/0016_non_allowance_work_items.sql`，沒有進 `cloud/migrations/`**；`WORK_ITEMS_ENABLED` 預設關閉。正式 D1 未因 GitHub 合併變更；CI 不需要本機 MCP，更不准另外建立 Cloudflare 測試 Worker。
- 僅處理例外 metadata／雜湊：買方、發票號碼、OrderID、秘密與未授權資料不得傳給 Work Item 清單。Work Item `closed` 是人工封存，不是官方成功。
- 2026-10-18：依使用者指示，已過時的單次對話交接／3.0 切換待辦文件自工作樹移除，Git 歷史仍可追溯；保留已合併 PR、稽核依據、正式 migration 與本設計文件。現行唯一待辦以 [TODO.md](TODO.md) 最新 3.1 章節為準。
