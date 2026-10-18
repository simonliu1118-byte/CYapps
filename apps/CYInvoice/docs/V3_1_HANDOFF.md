# CYInvoice V3.1：非折讓跨機協作交接

更新日期：2026-10-18（Asia/Taipei）

## 基準與範圍

- 正式產品仍為 CYInvoice V3.0.0 Build 0，開發基準 `CYapps/main` SHA `71463bbdff43269a12869611bc7ce86d26457974`；3.1 開發分支 `cyinvoice/v3.1-nonallowance-workitems`。
- 舊 PR #440 (`cyinvoice/v3-recovery-invitation-schema`) 只作**精準復用**來源；不得整包合併，也不得把舊 0015_work_items.sql 直接套到已有 3.0 schema15 的 D1。
- 3.1 只有非折讓：未收回紙本作廢管理員覆核、上傳問題與官方狀態不明、管理員跨機審查與可稽核封存。業務正常發票仍在 Windows 原清單，不把整套發票資料建成 D1 表；來源只用代碼／SHA256 避免買方個資與 Secret。
- 3.2 才有 AMEGO g0401/g0501、折讓配號／核准／跨機；3.3 其餘。原 3.0 公司卡／CYID／Device／Recovery 不重寫。
- 使用者說明 Cloudflare 3.0 安全升級已完成，真 Email／雙機 DR 驗收後做；本工作不從 GitHub CI 推定 Cloudflare 狀態，也不新增 Worker／D1 部署。本機 MCP 是遠端更動唯一執行途徑。

## 已實作（3.1-A，尚非正式環境）

- `cloud/src/work-items.ts`：僅 `VOID`／`UPLOAD_ISSUE` 的建立、匿名來源 SHA256 防重、查詢、管理員預設未結工作分頁和 `open→review→closed` revision CAS；SQL 隔離 active Device／Workspace、阻擋過期 revision 與關閉後再開。
- `cloud/drafts/0016_non_allowance_work_items.sql`：隔離 SQL 草案，沒有進 canonical migrations；有事件代碼白名單、原報告人／審查者／封存者、無 Delete／關閉後 Update，沒有 ALLOWANCE 種類。保持與 3.0 migrations 0014/0015 不互相覆蓋。
- `cloud/tests/v3.1-work-items.test.mjs`：在記憶體 SQLite 載入真 0014/0015 後隔離建立草案、驗證資料保留、revoked Device fencing、重複來源／Workspace CAS、敏感欄位不在管理員列表、封存 immutable 與折讓不能混入。
- `cloud/package.json` 已加入此測試到 `npm test`。尚未建立／註冊 Worker HTTP 入口、任何 WinForms UI 或正式 0016 migration；**目前不可直接部署這片**。

## 3.1-B1 追加完成的程式（待 CI 驗收）

- `cloud/src/work-items-route.ts` 目前僅管理員／超管 `get/list`，其他 API 尚未新增；Worker `app.ts` 需 `WORK_ITEMS_ENABLED=true` 且 `SCHEMA_VERSION>=16` 才會在完成請求位元組上限檢查後啟動 CYID Session。官方發票或客戶詳細資料不進列表。
- `cloud/tests/v3.1-work-items-route.test.mjs` 以隔離 SQL 驗證 disabled／schema15 拒絕、User 不能查、被撤銷裝置拒絕、權限在讀取期間下降不返回資料，以及分頁欄位隱私。
- 尚未開啟旗標、正式 D1 沒有 0016 migration。User 建立及管理員寫入仍須 3.1-B2。

## 下一片開發順序

1. 3.1-B 復用 #440 `work-items-route.ts`，只提供 VOID／UPLOAD_ISSUE；CYID Session/Device 每次重驗、USER 提交異常、ADMIN／SUPER_ADMIN 審核；schema>=16 + feature flag 預設關閉，請求上限在 CYID Login 前生效。
2. 3.1-C 復用 #440 `CloudWorkItemClient.cs` 的非折讓操作並整合原 Windows 上傳問題／作廢 UI，不新增獨立帳號權威，不改既有 Local 發票開立與人工折讓。
3. 3.1-D 在獨立本機 MCP 核對原 D1／schema15 的私有備份＋還原後，經核准把草案 0016 升為 canonical，再部署原 Worker 及啟用旗標、用第二台 Windows 真實覆核並檢驗失敗情境。遠端資料庫不用 Cloud CI 綠燈代替。
4. 未獲當次明確同意不得建立 V3.1 正式 Release；測試包須標示 engineering，不與 V3.0.0 正式版本混淆。


## 2026-10-18 Windows B1 唯讀客戶端（待當次 CI）

- 從 #440 的 Windows `CloudWorkItemClient` 精準摘取 **Get／List**、有限分頁與敏感欄位防洩漏；不把尚未在 main 開放的 Create／Report／Transition 寫入方法提早帶進 Windows 客戶端。搭配既有 CYID Login/Device Token 的 HTTPS POST（呼叫端須建立 `AllowAutoRedirect=false` 的 HttpClient，不能將帳密送到重新導向網址）。
- 在現有 Windows Cloud Tests 加上 list/get／錯誤資料／跨 Workspace 偽造回覆合成測試。**尚未接入 WinForms UI、未啟用 Worker Work Items 旗標，不宣稱可從 Windows 遠端覆核**。
