# CYInvoice 待辦與驗證

更新日期：2026-10-16（Asia/Taipei）。本檔為唯一未完成工作清單。Windows 正式版 **V2.6.19 Build 0** 已發布；AutoInput **V2.6.20 Build 0** 僅在獨立 [PR #445](https://github.com/simonliu1118-byte/CYapps/pull/445) 有 CI 通過的工程測試包（[run 38554728718](https://github.com/simonliu1118-byte/CYapps/actions/runs/38554728718)，非正式 Release）。V3 原工作仍在 Draft #440／CYWEB #151，尚未部署。原 development Cloud **0.9.3／API1／compatibility marker8／storage13**，**CYID_ENABLED=true／CYID_ONLY storage profile**。

- [V2.6.19 Release](https://github.com/simonliu1118-byte/CYapps/releases/tag/cyinvoice-v2.6.19) 與 [Release CI #38336787968](https://github.com/simonliu1118-byte/CYapps/actions/runs/38336787968) 已完成。沿用使用者接受本批本地／隔離測試及精確 source CI 的決定；人工折讓歷史缺欄位留 V3.0，不重新打開本批 Release gate。
- 本公司 CYID activation 已完成；2026-10-13 另經獨立批准完成私有加密備份／本地還原核對 → 原 Worker 相容升級 → 原 D1 四張舊帳號／轉換表退役 → 保留資料逐筆核對。部署證據：[Development Deploy #38396129643](https://github.com/simonliu1118-byte/CYapps/actions/runs/38396129643)；完成範圍見 §7.7。這是原 development 維護，不是另一個 production 環境或新 Windows Release。
- 本公司現使用 CYID；2026-10-15 使用者決定 V3 停止正式支援 Built-in／Self-hosted，僅保留 Local／CYID Cloud。此前其他公司自架的設計為歷史資料，不再是 V3 維護目標；不能復活本公司已退役的舊帳號表。
- 現行交接：[CLOUD_WORK_HANDOFF.md](CLOUD_WORK_HANDOFF.md)；實機證據清單：[RC_TEST.md](RC_TEST.md)。歷史 checkpoint 不覆蓋本節。原始筆數、ID、row fingerprints、Token hashes、備份／Time Travel bookmark 與本機路徑留在私有紀錄，不進 Public repo。
- 不建立／上傳／部署 Cloudflare 測試 Worker、測試版本或 handler；舊測試資源不重試刪除，不為補驗變更真實帳號或撤銷原裝置。本次維護授權不延伸為持續刪表／發布授權。

## 2026-10-16 V3 逐項交付索引（最新權威快照）

> **新對話與後續工作先看本節，再看舊歷史紀錄。** 精確基準：CYapps Draft PR [#440](https://github.com/simonliu1118-byte/CYapps/pull/440)，分支 `cyinvoice/v3-recovery-invitation-schema`，前次已驗證**功能** HEAD `4c897a44257de764c87d6ba3130e89327f5a1d56`；該 HEAD 的 Cloud Validate ×2、Windows Client、Windows x64、Governance CI **全部通過**，Cloud 部署作業為 **skipped**。這是 **source／synthetic CI**，絕不代表正式 D1、真光貿、WinForms 全流程或 DR 已驗收。每次新 commit 後必須以**新的 exact HEAD** CI 重新判定，不能沿用這筆綠燈。

### 已合入此 Draft 分支的功能（尚未上線）

- [x] V3 已按產品定案只保留 Local 與 CYID Cloud；Built-in／Self-hosted 不再列正式支援。
- [x] g0401 折讓：使用者帶入原發票後自行編輯／刪除折讓品項，前後端核對本次含稅總額不得超過「原發票－官方已生效未作廢折讓」；摘要缺漏、未完成及金額異常仍 fail closed。CYID Manager 核准 Grant、D1 永久配號與 send-once CAS、原號查回維持原設計，逐品項歷史推算**不是**開立資格。
- [x] CYID 折讓跨機申請、預覽、管理員覆核、配號、首次送出及原號查回的 Cloud／Windows Core 契約與主要 WinForms Manager Queue（`CloudAllowanceManagerForm`），僅 source CI／模擬資料驗證。
- [x] g0501 作廢：`amego-allowance-void.ts`、`amego-allowance-status.ts`、`amego-allowance-list.ts`；唯讀官方核對、單次作廢、雙日期 List 異常定位器；`drafts/0019_allowance_void_workflow.sql` 提供不可變申請／核准／Claim／官方狀態稽核及官方證據後結案柵欄。
- [x] g0501 的 **Cloud CYID routes 已接通於 Draft 原始碼**：`allowance-void-applications-route.ts` + `allowance-void-manager.ts` 包括 create／get／list、manager approve、send-once、status-readback、close；測試核對同一工作單唯一 Claim、逾時不重送、官方 `D0501/99` 後才可結案。**不要再把這組 endpoints 寫成「尚未建立」。**

- [x] **CYID Cloud 申請接線（Draft，已通過 4c897a44 CI）**：原 `RecordDetailForm` 在 CYID Cloud Mode 直接使用既有 `CloudAllowanceApplicationClient` 建立不可變 g0401 草案；先持久化同一 `wi_` ID／不含密碼的草案到原發票 metadata，保留原 `open` 申請，由 CYID 管理員在原核准視窗執行 Work Item `open → review`（一般 USER 不具送審權限）。暫時失聯只能原號查回，絕不以新 ID 重送；已結案的舊 Work Item 才允許在重新查核官方餘額後另提下一張。Local 保留原人工待辦。**真 Worker/D1 與光貿送出均尚未實機驗證**。
- [x] **原發票帶入折讓編輯介面（Draft）**：既有 `AllowanceRequestForm` 改為商品清單直接帶入、刪除品項、修改數量與單價、含稅／未稅輸入切換、依課稅別自算小計與稅額，畫面顯示「原發票－已生效折讓」剩餘額。使用者編輯後的商品清單與總額保存在現有人工折讓工作單，由管理員在原待辦明細查看；提交前仍由 `invoice_query` 重查歷史餘額，不新增獨立工作流程。**Local 人工待辦仍需管理員完成光貿操作；CYID Cloud create／manager review 已接通 Draft UI，但真 D1、光貿 g0401 與實機含稅四捨五入仍待驗收**。
- [x] **管理員唯讀查驗 UI 清理（Draft）**：刪除不適用的歷史逐品項分攤／推算上限敘述，保留官方折讓摘要、原發票總額、本次申請總額與後端真正的核准原因；不更動發票歷史資料、CYID 權限、send-once 或 D1。
### 一次只完成一個可驗證工作（完成後更新本節並結束當輪）

- [x] **2026-10-16 折讓需求校正：以總額餘額為上限，取消逐品項相符的送出限制**：依使用者明確定案，商品及折扣由使用者編輯；後端僅驗證必要的 g0401 payload 算式、原發票最新有效狀態，與 `原發票含稅金額－官方已確認且未作廢折讓含稅金額` 的總額上限。本機 `invoice_query.allowance[]` 已保留原本發票明細查回與歷史顯示，現在額外保存官方摘要是否明確存在，於輸入及提交重查時驗證餘額；Cloud 折讓核准及送出前驗證將改為同一限額並移除 `detail_vat=1`／已有折讓／逐項來源數量匹配的錯誤封鎖。官方摘要缺漏、待處理、重複、超額仍拒絕，不得誤認零折讓。**舊逐品項歷史覆蓋推算不再作為一般折讓開立前置條件。** 開立編輯介面及正式資料測試另待確認；不部署、不 Release。


- [x] **步驟 1 — Windows g0501 Core 客戶端已完成**：`214063e1810d55746d77d22c483de42135f4f907` 新增 `src/CYInvoice.Core/CloudAllowanceVoidClient.cs`、`tests/CYInvoice.Cloud.Tests/CloudAllowanceVoidClientTests.cs` 並登錄到 `tests/CYInvoice.Cloud.Tests/Program.cs`；與 Cloud CYID create/get/list/approve/send-once/status-readback/close 契約對齊。測試涵蓋 HTTPS／Device bearer、來源欄位不可信任、版本比對、歷史折讓號碼、錯誤結果拒絕、自動重送禁止、網路逾時不額外重試。該**功能提交** Cloud Validate ×2、Windows Client、Windows x64、Governance 全數 CI 通過，Cloud deploy skipped。**只有 Core／合成測試，不是 WinForms 操作或正式開立**。
- [x] **步驟 2 — Windows g0501 WinForms 操作入口與流程已完成（Draft Source）**：`275ec1c652c629aa9ff6eb95877148ff2ce70daa` 新增 `CloudAllowanceVoidManagerForm.cs` 並在 `SyncIssuesForm` 的 CYID「處理中」頁接入「CYID 折讓作廢」，包含一般使用者建立申請／原 ID 查回、管理員清單、`open→review`、官方驗證後核准、明確二次確認的唯一 g0501 送出、`allowance_status` 原號查回、官方完成後二次查驗並結案。建立前會顯示原 Work Item ID、禁止同一視窗重送；`e7efd90b1a7b25ab862d24efb3bd3daa7214c7a6` 加上依 Workspace+Cloud URL 隔離的**本機原 Work Item ID 耐久保存**，網路未回覆／重啟後仍禁止換號重複申請，只能原號查回；`1875b3132f770bc7fd6dae2aee58b5e0e18bfd1b` 修正 C# Nullable 保護。Windows `--startup-smoke-test` 驗證畫面、申請窗、離線原 ID 保存／重啟／跨 Workspace 隔離與確認後清除。此最後**功能** SHA 的 Cloud Validate ×2、Windows Client、Windows x64、Governance **全綠**，Worker deploy skipped。**這不代表真人 WinForms 驗收或 V3 已上線。**
- [x] **步驟 3 — D1 0015～0019 隔離 Migration 演練（Draft Source）**：新增 `cloud/tests/v3-migration-rehearsal.test.mjs` 並納入 Cloud `npm test`；以現有 0013→CYID_ONLY 退役→0014 歷史合成資料先建立 SQLite 備份，再依 0015→0019 的順序逐項交易升級。針對五個草案逐一注入 SQL 故障，驗證全交易回復、原 Workspace／Device／Invitation／Audit 不變、FK／integrity 正常、僅新增十一張例外／公司設定及稽核表且一般成功發票不可存入 Work Item；從未升級快照進行本機還原。**測試程式已提交，是否通過以本次 exact-HEAD CI 為準**；不代表正式 D1 備份、遠端 migration 或 DR 實機驗收。
- [ ] **步驟 4 — 官方清單與跨機例外對帳（進行中）**：g0401／g0501 雙日期官方 List、Work Item 原號選擇性查回與服務端 Audit 已於 Draft Source 完成；g0401 Windows「官方對帳」按鈕於 `bdcec96`，Cloud Validate ×2／Windows Client／Windows x64／Governance 全綠。本輪沿用既有 `CloudAllowanceVoidManagerForm` 加入 g0501「官方對帳」按鈕和 `CloudAllowanceVoidClient.ReconcileAsync`，只傳原 Work Item／revision／當次 CYID 管理員憑證，清單命中才驗證原號官方 `status-readback`，查無只能人工核實；Windows Core 模擬測試拒絕變號、虛假可重送／自動結案。`VerifySmokeLayout` 同步驗證九個操作按鈕。**尚待 Windows 與跨機人工驗收、正式 D1／光貿、DR、是否需要背景排程的必要性評估；本輪 CI 以 exact-head 為準。** 不新增表格、Cron 或平行頁面。
- [ ] **步驟 5 — 複雜折讓與其他整合缺口（進行中）**：使用者 2026-10-16 授權逐項開發、最後統一 CI／取 Artifact，不自行 Release 或正式部署。已實作 CYID 上傳問題跨機回報及管理員封存、本機失敗發票持久封存與 Retention 防誤刪，以及 g0401 三張以內歷史折讓的唯讀原發票分攤與保守完整性診斷。**本輪增加「已核對歷史分攤金額與剩餘上限」唯讀參考**：只有本次官方原發票／歷史折讓狀態有效、無 pending、所有**目前列出的**原號均逐張詳查且 7 位小數核對一致，才從原發票官方含稅總額扣除核實過的該原發票分攤額，得出**截至本次查詢的保守剩餘上限**。Cloud 以 BigInt 精算；Windows Core 由官方最小投影獨立重算，拒絕假上限；WinForms 明確提示此上限**不是可折餘額或核准**。其餘缺漏、重複、異動、未查齊或超額情形回傳 NULL，不推算數字；超出原發票上限有獨立診斷狀態。**`historicalAllocationsComplete:false`、`safeToInferRemainingBalance:false`、`canApprove:false` 不變**，仍不能以此自行核准/開立部分折讓；此批程式待最後 CI/Artifact，尚有含稅/部分折讓、完整歷史覆蓋證明、DR、實機整合。
- [x] **步驟 5 小項 — 含稅原發票唯讀來源核對（Draft Source）**：依光貿 DetailVat=1 的含稅價與有拆稅情形之 5% 公式，新增以 7 位小數 BigInt 驗證原發票品項、總額及官方稅額的有限診斷。無拆稅稅額（可能為 B2C）、缺漏、重複品項、金額或稅額不一致均給明確原因。**所有含稅來源即使自洽仍固定 fullyMatched=false／verifiedLines=0，不能拿含稅單價對照 g0401 未稅單價，也不變更 canApprove=false 或歷史可折餘額安全旗標**。只修改 Draft 診斷、合成測試與原 WinForms 提示，未完成含稅轉換／部分折讓核准；本次 exact-head CI 另行確認。
- [x] **步驟 5 小項 — 歷史折讓明細數量與未稅單價保守驗證（Draft Source）**：於既有 `allowance_query` 唯讀原號查回的 `originalAllocation` 中，先確認每一行官方原發票日期、品名、課稅別、正數數量、未稅單價、小計及整元稅額均存在且符合 `Quantity × UnitPrice` 七位 BigInt 四捨五入；再驗證全單的金額／稅額等於官方 header，才保留該原發票的已知分攤金額。缺任一選填欄、數量不符、稅額不整數、非應稅卻有稅，均 `ITEM_AMOUNTS_UNVERIFIED`、原分攤金額為 null。**這是歷史明細可靠性前置條件，尚未從原發票品項扣算逐項剩餘數量，更不是可折餘額證明**；無新資料表、API、正式部署或光貿 mutation；以 exact-HEAD CI 驗證。
- [x] **步驟 5 小項 — 歷史折讓對應原發票逐品項數量（Draft Source）**：在原 `allowance_query` 唯讀查回中，以本次 `invoice_query` 原發票日期、明確未稅 `detail_vat=0`、品名／稅別／七位小數單價，將核實過的歷史折讓行對應至同一次官方原品項快照；拒絕原品項相同鍵重複、欄位缺漏、原品項算式不符、日期不同、單張或跨多張已知歷史折讓數量累加超過原數量。僅在 Cloud 記憶體累加快照內的**已知使用數量**，不向 Windows 回傳品名／可折數量、也不宣稱歷史範圍完整；不可核對時將既有原分攤驗證與金額上限改為未知 `NULL`。官方含稅來源、部分折讓完整核准、所有歷史覆蓋證明與實機 DR 仍待後續；保留 `historicalAllocationsComplete=false`／`safeToInferRemainingBalance=false`／`canApprove=false`。
- [ ] **步驟 6 — DR／實機／正式 Release**：DR-01～DR-13、跨裝置權限與斷線、Windows 套件驗收、正式資料備份及遷移計畫、V3.0 發布。未全數驗收前保留 Draft，不合併、不對真實發票進行未批准操作。

**作業節奏（使用者 2026-10-15 指示）：** 先同步本 `TODO.md` 和 `CLOUD_WORK_HANDOFF.md`，往後每次只處理一個步驟／小型可驗證 commit；回報 exact commit、CI 與尚未完成點，避免連續長時間工具操作造成對話逾時。只更新文件時，**不**順手改功能程式。

## 2026-10-15 V3 最新續作：跨機折讓作廢與雙日期 List 對帳基礎（Draft）

- [x] **g0501 單次送出傳輸**：既有 Windows `AmegoClient.VoidAllowanceAsync` 已具備光貿 `g0501`，Cloud 新增 `amego-allowance-void.ts`：只發一次官方 `[{CancelAllowanceNumber}]`，沿用官方 form-urlencoded 簽章與 8 秒逾時；支援 1～16 字元的既有官方單號（不強迫 `AyyyyMMddXXX`）。回傳 `accepted_unconfirmed / definite_rejection_manual / unknown_result`，**任何結果不得自動重送或視為已作廢**。新增合成簽章／逾時／拒絕／無重送測試。
- [x] **官方作廢狀態唯讀查回**：`amego-allowance-status.ts` 支援光貿 `allowance_status` keyed object／array 兩種回覆及精確號碼比對，只有 `D0501/B0501/B0201/B0202` 且上傳狀態 99 才標 `officialCancelled`。回覆固定 `safeToAutoResend=false`／`safeToAutoClose=false`，**沒有寫入 D1**；待處理／NOT_FOUND／狀態不明一律不作完成認定。
- [x] **D1 0019 Draft 與 SQLite 合成防重試**：`cloud/drafts/0019_allowance_void_workflow.sql` 新增僅限真正例外的 `ALLOWANCE_VOID` 申請（原因、原單號、申請人）、不可變管理員核准稽核、**唯一的首次送出 Claim**、官方狀態檢查及結案前的官方完成證據柵欄。SQL Trigger 檢查 Work Item kind/revision、Workspace、Device active、公司設定版次；核准到期不得修改原紀錄延長，send claim 一經取得不能刪除／重建。合成 SQLite 測試驗證重複申請、跨裝置、逾期核准、二次送出與早期結案拒絕。
- [x] **官方 List 異常定位器**：`amego-allowance-list.ts` 僅以官方 `allowance_list` 的折讓日／建立日（`date_select=1/2`）分頁讀取，回傳必要單號／類型／上傳狀態，不傳買受人資料；`selectAllowanceExceptionHints` 僅對原 D1 例外工作單比對，查到異動才建議進行單號 `allowance_query`／`allowance_status` 詳查，**清單查無不是「發生失敗」的證據，不能解除 sending／重送／自動結案**。
- [ ] **尚未接通、不可冒稱完成：** 0019 正式環境 Migration、完整 CYID 保護的 ALLOWANCE_VOID create/manager approval/send-once/status-readback/close routes、Windows 跨機折讓作廢 UI、官方雙 List 定期排程和全量分頁比對、例外 Work Item 查核的正式 Audit 寫入、已有折讓含稅／跨原發票餘額核對、公司設定安全同步、其他上傳異常跨機、全部 DR-01～DR-13 實機、V3 合併／正式發布。現行 0015～0019 **全部仍是 Draft SQL**，不得套用到正式 D1；新增 Cloud API 模組目前**不是已上線操作端點**。原始碼測試完全合成且沒有真實光貿或 Worker mutation。
- [ ] 安全 Gate：完整管理員操作必須先在 D1 原子取得唯一 `allowance_void_send_claims`，確認原工作單版本／有效核准／公司綁定，再呼叫**恰好一次** g0501；掉線後只能用原單號查回，官方 99 + D0501 也須經可信 Audit 確認才允許 Work Item 關閉。任何其他情況不得重送。

## 2026-10-15 V3 接續：折讓稅額算式及原單號官方唯讀查回（Draft）

- [x] **品項核對加嚴**：原發票每列及申請每列均以 `FixedDecimal` 對應的 BigInt ×10^7 計算 `Quantity × UnitPrice` 與 `Amount`，要求原發票淨額+稅額等於官方總額，應稅 `TaxType=1` 以固定 5%／整數元四捨五入比對明細與整筆稅額，不允許不合理的稅額數字單憑「尚未超過原稅額」通過。舊版過度寬鬆的合成測試已改成真實七位小數、合法 5% 稅額案例，新增錯單價、錯行金額、錯稅額、拆列重複等失敗案例。這仍只涵蓋**資料明確的未稅模式**，若官方四捨五入或歷史分攤不明，保留人工確認。
- [x] **原始折讓號碼唯讀查回**：`amego-allowance-query.ts` 使用光貿簽章、`allowance_query`、8 秒 timeout、256 KB 上限與精確單號／原發票／日期／狀態驗證；官方未找到（例如 code 51）、查詢逾時或非確定成功，**不允許自動換號或重送**。處理同單號被其他系統占用及跨原發票折讓的情形，單號相符也不能當作「本次確定開立」。
- [x] **CYID 管理員查回 API 與 Windows Core**：`/v1/allowance-applications/number-readback` 只能從原 D1 ledger 取出確實保存的 `sending`／`confirmed` 折讓號碼，由服務端公司設定解密 App Key；要求同 CYID manager／有效裝置、公司版次、Workspace／環境與 frozen request 一致。只回傳最小狀態證據、原發票一致與含稅總額比對；**`safeToAutoResend=false`、`safeToAutoConfirm=false`、`requiresManualReconciliation=true`**，不修改 D1 status，不呼叫 g0401。Windows `ReadbackNumberForManagerAsync` 拒絕虛假的自動送出／完成旗標。增加 Cloud SQLite 與 Windows 合成測試。
- [ ] **未完成**：真正可使用的管理員核准與 g0401／g0501 執行、歷史 allowance_query 逐項扣除及跨原發票餘額分攤、含稅 `detail_vat=1`、WinForms 操作介面、正式 migrations、公司憑證輪替／跨機同步、雙 List 官方對帳、DR-01～DR-13 實機及 V3 Release。以上均未部署；當次已存在的 `sending` 必須以原號查回，不能重送。只完成合成測試不得宣稱正式發票業務完成。

## 2026-10-15 V3 續作：原品項七位小數保守核對（Draft）

- [x] `cloud/src/allowance-item-validation.ts` + Cloud Synthetic Test：比對同原發票日期、品名、課稅別、未稅單價、原品項數量與小計，申請 `Amount`／`Tax`、淨額 `TotalAmount`、`TaxAmount`、申請含稅總額，以 BigInt ×10^7 避免浮點誤差；重複請求品項累加數量與金額，拒絕超出原始數量／金額及重複而沒有穩定行 ID 的官方原明細。
- [x] CYID 管理員 `official-inspection` 回傳最小必要 `itemCheck`（`fullyMatched`、原因代碼、合格行數），不回傳原始明細或公司密鑰；`wait[]` 任何未處理異動（含 D0401／C0501）都標 `officialPendingMutation` 並阻止安全剩餘額判斷。Windows `CloudAllowanceApplicationClient` 也會拒絕格式錯誤、待處理仍宣稱 matched 或假冒核准的回覆。
- [ ] `fullyMatched=true` **僅表示官方 `detail_vat=0` 未稅原品項與此單的數量、價額保守符合上界，不等於官方授權或法定稅額確認**；任何已有折讓、`detail_vat=1` 含稅來源、官方缺欄或 Pending mutation 都 fail closed。需要補含稅換算與 rounding、B2C 零稅額差異、跨原發票的歷史折讓剩餘額、逐項稅額審核與最終官方 preflight。仍固定 `requiresPerItemVerification=true`、`canApprove=false`，沒有管理員 Grant、配號或 g0401。
- [ ] 本輪只改 PR #440 Draft Source／合成測試，0015～0018 尚未套入既有 D1；不得開正式折讓、部署 Worker 或 Release，實機 DR 未驗收。最新 CI 以 [PR #440 checks](https://github.com/simonliu1118-byte/CYapps/pull/440/checks) 為準。

## 2026-10-15 V3 新增：光貿官方 invoice_query 唯讀查驗（Draft，尚未批准折讓）

- [x] `cloud/src/amego-invoice-query.ts` 新增官方 **`POST /json/invoice_query` 唯讀查詢**：固定官方 HTTPS host，依光貿規格將 `{type:"invoice",invoice_number}` JSON 字串與 Unix 秒數及 App Key 做 MD5 wire 簽章，採 `application/x-www-form-urlencoded`，只呼叫一次、8 秒 timeout、限制 256 KB、嚴格核對原發票號碼／狀態／金額、保留 allowance[] 是否明確存在、計算回應 SHA256；`wait[]` 有 C0501 待處理時不得視為未作廢。僅於**服務端**讀取加密公司設定，**不經用戶端傳入憑證**。官方規範查詢僅涵蓋約 180 天內發票，舊發票 code 51／缺欄位皆不可當成無折讓；參考 https://invoice.amego.tw/api_doc/ 。
- [x] 新增 `POST /v1/allowance-applications/official-inspection`（CYID ADMIN／SUPER_ADMIN、Device active、frozen Work Item 正確 revision／公司設定版次／environment 驗證）。API **只顯示**官方上傳、作廢／待作廢、折讓摘要是否明確、已存在折讓筆數、原始總額、查詢時間及雜湊；固定 `requiresPerItemVerification=true`、`canApprove=false`、`status=OFFICIAL_READ_ONLY_NOT_APPROVED`。不會寫入 Approval、配號或觸發 g0401／g0501。
- [x] Windows Core `CloudAllowanceApplicationClient.InspectOfficialForManagerAsync` 新增同一個唯讀契約，強制拒絕宣稱 `canApprove=true`、狀態不一致或不合格式的回覆；Cloud synthetic 用 Node Crypto 驗證簽章與隔離官方回覆、錯號、漏 allowance[]、重複結果、不完整資料、已存在折讓、無權限不發 API 及無自動重試。
- [ ] **仍須完成**：官方品項及稅額核對、曾有折讓的同原發票剩餘額分攤、金額四捨五入規則、動態公司憑證版本／環境治理、即時核准寫入與其最終 preflight、Windows WinForms 覆核 UI、g0401 執行與結果不明查回、正式 migration／DR 實機。以上皆是**未部署的 Source Draft**，此次未向光貿傳送任何真實 API 請求。

## 2026-10-15 V3 進度：管理員唯讀覆核預覽與折讓 D1 不可竄改閘門（Draft）

- [x] 新增受 CYID 管理員驗證的 `POST /v1/allowance-applications/review-preview`：從已進 `review` 的原 Work Item 及服務端已加密不可變折讓申請讀取明細，核對 revision、公司設定版本、環境、SHA256，僅傳回管理員必要品項與摘要；回覆固定 `officialVerification=NOT_PERFORMED`、`canApprove=false`，**不呼叫光貿、不核准、不配號、不產生折讓**。一般使用者、錯 Workspace、過期 revision 都拒絕；附 Cloud 合成測試。
- [x] Windows `CloudAllowanceApplicationClient.PreviewForManagerAsync` 新增唯讀 preview 契約及回覆驗證：如回傳誤稱可批准／非 NOT_PERFORMED，客戶端 fail closed。合成測試已加入原 Windows Cloud suite；無 WinForms UI。
- [x] D1 Draft 0016 新增保障：`allowance_execution_approvals` 不可 UPDATE/DELETE；`allowance_number_ledger` 不可 DELETE 或改號；同工作單尚有 `reserved`／`sending` 配號時不得直接關閉，但 `confirmed` 可正常結案。原 approval TTL 測試改用合成時間流逝驗證，不修改已核准歷史。對應 SQLite 合成測試保留。
- [ ] **仍未接通**：官方 invoice_query／品項與剩餘折讓額真正驗證、能記錄核准 Grant 的受保護管理員 API、g0401 官方安全送出、官方結果不明查回、WinForms 申請與覆核操作 UI、正式 D1 migration、跨裝置實機及全套 DR。只有上述唯讀預覽與 SQL 防護通過原始碼測試，不代表已可正式覆核或開折讓。

## 2026-10-15 V3 接續：CYID protected routes／Windows 公司卡 Client／跨機覆核清單（Draft）

- [x] **CYID 保護的折讓例外申請入口**：Worker `app.ts` 經 CYID 應用範圍登入和每次的 Device Token 驗證後，提供 `POST /v1/allowance-applications/create|get|list`。僅由服務端 binding 決定 business Workspace／公司統編／正式或測試環境；不接受客戶端自訂 authority 或雜湊。USER 可以原 ID 提出申請並唯讀查自己的結果；ADMIN／SUPER_ADMIN 可在別台 Device 查看例外工作單（依建立時間+ID 游標分頁，每頁最多 50），沒有一般發票清單／所有使用者全表。返回最小必要摘要，不含密碼、金鑰、加密模板。
- [x] **SQL batch 的第二段寫入保護**：`0018_allowance_applications.sql` 新增 BEFORE INSERT source guard，確認同一 Work Item、首次建立時間、申請 actor、Device、environment、reference SHA256、公司設定 revision 及有效狀態；不能以既有不同工作單或被撤銷裝置形成半筆申請。結果不明時 GET 原 Work Item ID，不能另建新單／自動重送。Cloud synthetic regression 覆蓋兩 Workspace mapping、角色、錯誤 scope／環境、分頁、沒有 secrets 的回覆與 batch rollback。
- [x] **公司卡受保護 API 與 Windows 傳輸**：`POST /v1/company-settings/get|update` 同樣以 CYID revalidate、business Workspace binding、Cloud Device、SUPER_ADMIN 當次 marker／Email 確認；ADMIN 唯讀不含 Secrets 的設定狀態。Windows `CloudCompanySettingsClient` 提供 HTTPS／Bearer／CYID 當次帳密／預期 revision、明確 keep/replace/clear、無自動重送與安全 JSON 回覆驗證；工程合成測試已加入 Windows suite。通用 Windows `CloudWorkItemClient` 不再接受 ALLOWANCE 偽建立。
- [ ] **仍未完成：**WinForms 公司設定與折讓申請／覆核 UI、現行本機 DPAPI 快取版本同步、服務端光貿官方完整 item／餘額驗證、manager approval route、g0401／g0501 的真正安全送出與查回、其他作廢和失敗事件跨機、背景雙 List 官方對帳、DR-01～DR-13。雲端公司卡與折讓的受保護入口是 **Draft，0015～0018 尚未正式套用**，不授權立即供日常使用。最新 CI 與工程包以 #440 exact-head 為準；不自動 deploy／Release。

## 2026-10-15 V3 折讓申請不可變來源（Draft-only）

- [x] 新增 `cloud/drafts/0018_allowance_applications.sql`、`src/allowance-applications.ts` 與隔離測試：只在 CYID 業務 D1 的**例外折讓申請**建立資料，沒有一般發票總表。由 CYID USER／ADMIN／SUPER_ADMIN 當次身分確認後，使用**原 Work Item ID** 執行 `D1.batch` 原子建立 Work Item＋申請；賣方統編／設定版次來自服務端公司卡、environment 由可信 Worker runtime 注入；原發票號碼、原因、正整數金額、申請 actor 與時間持久化，未編號 g0401 模板使用 Workspace＋Work Item AES-GCM AAD 加密儲存，雜湊由服務端依固定欄位建立，不信任前端 referenceHash。
- [x] 讀取待覆核內容必須比對 Work Item `review`／revision、申請 reference hash、公司設定版次與原來統編、解密後模板 SHA256，任何不符 fail closed；申請資料有 D1 UPDATE／DELETE 禁止 trigger。通用 `/v1/work-items/create` 拒絕 ALLOWANCE 人工雜湊建立；正式申請專用 route 尚未開放。測試涵蓋 USER 建立、錯誤模板、重複申請、Device 撤銷、D1 批次 rollback、公司設定變動與不可變約束。
- [ ] **尚未完成**：申請正式 UI／CYID 專用 API、管理員官方 live 查核及真實餘額分攤、Windows 管理員核准、持久化申請與 `approveAllowanceAfterOfficialReview` 的受保護整合、g0401 安全首次送出及事後唯讀 reconciliation。0015～0018 都是 Draft，禁止部署到原 D1 或開放寫入。D1 batch 回覆不明時必須依原 Work Item ID 查回、不可用新單號盲重送。

## 2026-10-15 V3 公司設定權威 Draft（尚未接 Windows／正式 Worker）

- [x] 新增 `cloud/drafts/0017_company_settings.sql`、`src/company-settings.ts`：只供內部 Worker 使用的 CYID 公司設定權威草案。SUPER_ADMIN＋當前超管 marker／Email 驗證前後重新驗權；Workspace＋Device active 寫入 SQL 柵欄；revision CAS；AES-256-GCM 隨機 nonce＋Workspace／欄位 AAD；密鑰由部署時私有設定注入，絕不放 D1；原設定以明確 keep／replace／clear 避免空白覆蓋；Audit 同一 D1 batch 保存異動欄位／員編／裝置，不記秘密。ADMIN 唯讀統編＋設定狀態，無解密接口；服務內部可經金鑰解密供未來 Gateway。
- [ ] 尚未建立正式公司卡片 UI／CYID auth route／DPAPI Device Cache，亦未處理憑證輪替後舊 Device 收斂；0017 只是 Draft，禁止套入原 D1 或對外啟用。要使用於折讓覆核，仍需服務端 frozen application 與官方 Gateway final preflight；先完成完整測試與授權整合。

## 2026-10-15 V3 接續：管理員官方覆核 Grant 寫入者（Draft-only）

- [x] **內部 approval writer 已新增**：`cloud/src/allowance-review-approval.ts` 的 `approveAllowanceAfterOfficialReview` 只接受 Worker 內部**可信任**三項 callback（CYID manager 重新驗證、從服務端讀取 frozen application、由光貿 Gateway 查詢及完整驗證單張原發票）。核准前後重新驗證同一管理員、Workspace；官方查詢資料必須在 60 秒內、具明確 allowance 陣列、發票上傳狀態 99、未作廢，原發票金額與申請品項分攤均被服務端確認。**暫時遇到任何已存在的官方折讓，都 fail closed**，留待品項分攤／剩餘額正式核對，絕不把多原發票折讓單的總額直接視為此張可折。
- [x] **D1 簽核證據改成 append-only**：`cloud/drafts/0016_allowance_number_ledger.sql` 的 `allowance_execution_approvals` 增 `approval_id` 與索引；核准時間到期後重新查官方可新增簽核，而非覆寫前筆 actor／時間。寫入採單條 INSERT SELECT 檢查當下 Work Item `review` 與 revision、CYID Cloud Device／Workspace active，且此前號碼不可處於 `sending`／`confirmed`；同 revision 已簽過的公司、環境、日期、固定 template SHA256 不可偷改。核准本身**不會配號或開折讓**，配號／首次送出還有有效 grant fence。隔離 D1 SQLite 測試涵蓋缺官方折讓欄位、已有折讓、品項不明、過期官方快照、權限撤銷、競態、逐次 audit、核准後資料不變。
- [ ] **仍為 Draft／不可對外呼叫**：三個 trusted callbacks 目前尚未接上實際公司設定卡片、持久化的含申請人/原因/明細之完整 frozen Work Item、真實光貿 Gateway 或 CYID session／route。沒有 Windows 管理員核准畫面與可操作 `g0401` 的安全執行路由，也未驗證已存在折讓的精確品項分攤。此進度不可當「折讓覆核功能已完成」；0015／0016 依舊**沒有套用正式 D1**。有任何官方狀態不明／無法判定餘額，禁止核准自動開立。下階段先完成可信 company settings 與 frozen request source，再接 route。
- [ ] **已送出後 reconciliation 的獨立追蹤**：目前 D1 ledger 仍以 Device active／Work Item `review` revision 限制 `sending→confirmed`；若外部成功後 Work Item 被其他管理員關閉／Device 撤銷，需另設具核對證據的唯讀查回及 audit 寫入路徑，不能為了失去本機 Device 狀態而盲送 g0401 或重配號。

## 2026-10-15 插隊 AutoInput 後接續 V3：官方覆核先於配號（Draft Source）

- [x] **V3 Cloud 草案配號加上 explicit approval fence**：`drafts/0016_allowance_number_ledger.sql` 新增 `allowance_execution_approvals`，以 Workspace／Work Item revision／公司統編／環境／日期／未編號的固定 g0401 template SHA256／官方狀態快照 SHA256／CYID 核准人及核准時間／有效期限記錄最小覆核證據；`allowance-number-ledger.ts` 的占號 SQL 與 `reserved→sending` SQL 均要求 approval grant 存在、內容一致且未逾期。後續官方確定拒絕的撞號與查回結果入帳不受時間過期妨礙，避免處理中的舊操作永遠無法記錄。新裝置可以在原 Device 撤銷後接手仍有效的工作單，但每次業務寫入仍須檢查**執行者自己**的 Device active、Workspace 與 Work Item revision。新增無核准／核准過期／重新核准／雙裝置安全測試。
- [ ] **上述 Draft 尚缺官方覆核核准寫入者及安全開立執行路由**：目前沒有 API 能把 client 宣稱的「已查光貿」直接寫成 approval，將來必須由 CYID manager 線上 revalidate、Cloud server 信任的公司設定與唯讀官方發票／折讓餘額查詢證據建立 grant；申請人與覆核者身分／申請內容須在 D1 真正可驗證，最終 g0401 前須核對 Work Item revision + grant 未逾期、完整固定 payload 與已占號一致。**不可將 `review` 當 `approved`，也不准先送光貿再補配號。** 0015／0016 仍僅在 Draft，未部署原 D1。
- [x] **AutoInput 插隊交接已處理，與 V3 互不混用**：獨立 PR #445 V2.6.21 Build 0 `--autoinput-pdf`／工程包／Windows CI 完成；已將 Artifact 與四項確認寫回 AutoInput PR #424 的 `CYINVOICE_PDF_HANDOFF.md`。主 V3 不 cherry-pick 該支 V2 功能，後續版本整合另按正式 main 基準處理。

## 2026-10-15 CYWEB 交回確認與 CYID Cloud D1 配號草案（待整合）

- [x] **CYWEB Recovery handoff 原阻塞解除並回報**：已核對 [CYWEB #151 的 `CYINVOICE_RECOVERY_HANDOFF.md`](https://github.com/simonliu1118-byte/chihyuan-web/blob/cyweb/feat-cyinvoice-recovery-auth-gate/docs/development/CYINVOICE_RECOVERY_HANDOFF.md)，其中「CYInvoice 還缺 explicit revoked」為 10/14 舊快照。CYInvoice `recovery-issuer.ts` 與 `recovery-issuer.test.mjs` 已有 `revoked_at` 明確成功、consumed／expired 不冒充撤銷、lost D1 response 查回與 claim/revoke 競態 SQL rollback。已更新 CYWEB 該 handoff、CYWEB 根 TODO、PR #151 留言，並取得兩 repo source CI 正向結果。**只結案原 Source blocker，不勾 DR-01～DR-13；交接文件暫留作驗收依據**，後續依正式 V3 順序完成。
- [ ] **Cloud D1 安全配號底層（新增 Draft，尚未完成跨機正式開立）**：新增 `cloud/drafts/0016_allowance_number_ledger.sql` 與 `cloud/src/allowance-number-ledger.ts`、`cloud/tests/allowance-number-ledger.test.mjs`。以 Workspace＋公司統編＋環境＋日期唯一序號，格式 `AyyyyMMddXXX`，永久保留已占號 attempt；關聯 exceptional ALLOWANCE Work Item，不存一般成功發票／原始 AMEGO payload／API 金鑰，只保存 SHA256 frozen request 雜湊。單條 D1 SQL INSERT 對 current max+1、`review` revision、active Device／Workspace 執行當下 fence；`reserved→sending→confirmed`，只有**同一次確定首次 API 拒絕 `4040161`／`4040162`**才准 `sending→occupied_external→`同工作單下一次占號；timeout 必須原號官方查回。synthetic SQLite 涵蓋跨工作單、跨裝置競爭、撤銷、同一工作單重入、999→A00、ZZZ 上限與永不回收。**這只是 Draft foundation：0015／0016 未移入正式 migrations／未部署，尚無真正 manager approval UI、Work Item 可驗證最小申請內容、權限 revalidation 路由、g0401 execution wiring、官方 reconciliation 或真實 AMEGO 實測。**進入正式 approval route 前必須由 server 綁定的公司設定決定 sellerInvoice，絕不能接受瀏覽器傳統編／憑證作為權威；不可單靠 `review` 代表已核准。
- [ ] **V3 覆核開窗唯讀即時官方資料 Core（新增 Draft，尚未接 UI）**：`AllowanceReviewInspectionService.InspectAsync(originalInvoiceNumber)` 不依賴原機 SQLite，使用當次授權公司的 Gateway 只發**一次 `invoice_query`**，回傳原發票作廢／上傳狀態、所有官方 `allowance[]`、查詢時間及資訊不全旗標。Amego `QueryResult.AllowanceSummaryPresent` 分辨實際 `allowance:[]` 與未提供／null；後者不能當無折讓，任何已有折讓都先要求跨原發票的品項分攤查核，不能用折讓單總額直接扣該張發票餘額。新隔離測試涵蓋查詢一次、不同本地 Device 不需要紀錄、缺欄位、取消、待處理、錯號及實際 wire parser。**目前尚無可核准判斷／WinForms 接線／CYID server-side company key 接線，不能只靠此唯讀 Service 直接送 g0401。**

## 2026-10-15 V3 開發續進（Source commit 進展，尚未正式發布）

- [x] **CYID-only 執行路徑收斂（Draft source）**：Cloud Worker `app.ts` 先檢查 `CYID_ENABLED` 及 Binding，舊 Built-in Provider 的 Employee／密碼／轉換 public dispatch 已移除；CYID 仍對舊 API 顯示明確 409。Work Item route／SQL 不再選擇 Built-in `cloud_employees` 或 PBKDF verifier，改以 CYID 目前管理員角色、即時 revalidate 及 D1 active Device／Workspace 寫入柵欄。Windows `LocalRepository` 不再實例化 Built-in Cloud identity provider，`IdentityProviderRuntime` 只接受 Local 與 CYID；舊 Built-in 身分狀態 fail closed。已更新 CYID-only 合成測試，歷史 source／migrations 與舊 Cloud setup 次要欄位**還沒全面刪除**，不得認定 Built-in 完全清乾淨。
- [x] **AMEGO `g0501` 作廢折讓 Gateway 原始碼（Draft）**：已加 `VoidAllowanceAsync(allowanceNumber)`，送一次官方 `/json/g0501` 的 `[{CancelAllowanceNumber}]`，不重新編號，不對 HTTP 結果不明自動重送；支持光貿網站已有的合法歷史號碼，不限 CYInvoice 的 `AyyyyMMddXXX`。隔離 payload／拒絕測試已新增，**不表示實際光貿操作已驗證，也未接 Windows 核准**。光貿官方 [API 合約](https://invoice.amego.tw/api_doc/)。
- [ ] **待完整完成的正式折讓執行**：現有 `EmployeeAllowanceWorkflowService` 與 `SyncIssuesForm` 仍以「管理員先去光貿網站人工處理，再按已人工完成」為主；尚未將 g0401／g0501、Local SQLite Ledger、Cloud D1 配號、Work Item 正式覆核與即時官方餘額驗證接成完整受保護的端到端。無此證據不得發布 V3.0。
- [ ] **CI readback gate**：以上目前是 Draft Source；每一輪以精確 PR head 的 Cloud validate／Windows x64 build＋startup smoke／Governance 成功判定，單次中間失敗與被取消的舊 run 不可當作最終結果。不得部署正式業務 D1 或創建真實光貿測試作廢。

## 2026-10-15 V3.0 正式交付工作順序（使用者明確批准）

按順序執行：**(1) 清理 Built-in → (2) 完成折讓 API 與安全配號 → (3) 完成 D1 Work Item／跨機覆核 → (4) 官方 List 對帳 → (5) 公司設定卡片與整合驗收 → (6) 災難復原跨服務部署／完整 DR-01～DR-13 實機驗收 → (7) 文件與殘餘 Built-in 清理 → (8) V3.0 正式發行**。不得將任何階段「有文件／有底層 DTO／CI 綠燈」冒充端到端完成；**Cloud Worker／D1 正式遷移與 Windows V3.0 Release 必須在依規則完成真實驗證後才可執行**。

**當次 Draft #440 新增 source 實際進度（非已發行）：**
- [ ] Built-in 清理：V3 `IdentityProviderRuntime.Current` 現只允許 Local 與已完成 CYID 的 Cloud；Direct Join 缺 CYID binding 將 fail closed、不再拉 Built-in Employee snapshot；設定頁從空白 Cloud Device 改走 CYID Direct Join，舊 CloudSetup 建立／轉換入口受阻。Windows Direct Join smoke 換成 CYID-only、拒絕非 CYID 及原 Token 恢復案例。**Cloud Worker 入口與 Work Item 憑證分支已收斂；其他 Built-in source、歷史 handler、相依測試與部署相容分支尚待審查清理**；不得直接刪正式歷史 migrations 或遠端 D1。
- [ ] 折讓 API：V3 Core 已新增 `/json/allowance_list` 的 `ListAllowancesAsync` 分頁 DTO／官方原發票品項解析，與 `allowance_status` 舊 keyed object、新 array 格式的嚴格單號對應；新增隔離 HTTP 測試。**尚未接 Windows 正式 Sync 與管理員流程。**
- [ ] Local 安全配號：已新增 `LocalAllowanceNumberStore`，在既有 SQLite 額外建立專用 allocation ledger，以公司／環境／日期原子增號、Work Item 重入返回同一 reserved 單號、先存完整固定 g0401 payload、`MarkSending` 後不允許重送、確切撞號才占號／重配、已用號不回收；新增重啟與碰撞測試。**未接實際折讓核准流程／沒有 Cloud D1 配號／真實 API 驗證。** 需要 CI readback 才能宣稱測試通過。
- [ ] Windows CI 曾在中途提交的 CYID 模擬續接測試失敗（設定模擬的 discovery 故障次數過多）；已將 `cyid-recover` 的 fail-once 修正為一次，待最新 Windows build／startup smoke 通過後才結案。先前失敗不得默認為成功。

## 2026-10-15 V3 產品範圍定案（覆蓋舊 Built-in 待辦）

- [ ] **V3 僅保留 Local + CYID Cloud 兩種正式模式**：使用者確認沒有正式 Built-in 使用者，停止維護 Built-in Cloud／Self-hosted 做為可用產品；GitHub 第三方如需自行部署 Built-in，改自行 fork。不以其相容性作 V3 合併／驗收／Release 條件；歷史成功測試和已發布 V2.6.19 不被重新定義為已刪除。
- [ ] **工程簡化清理（V3 runtime 已收斂，歷史原始碼待最後清除）**：清理 Built-in identity provider／Employee CRUD／OTP／忘記密碼／SUPER_ADMIN transfer、Cloud 初始導入及相關相容分支，評估刪除不再需要的 handlers／migrations／fixtures／CI 矩陣；保護仍被 CYID／Local 共用的 Device、Workspace、Window Core、D1 Work Items、錯誤處理與歷史 actor/audit 解讀。不可復活或重做已退役的原 D1 表，不把刪 source 當正式 DB 清理授權。
- [ ] **統一公司設定卡片（含使用者批准的 Cloud DPAPI 快取）**：Local 保留本機 DPAPI 憑證儲存與合理權限；CYID Cloud 的**公司設定權威**在 CYInvoice 業務 D1，僅 SUPER_ADMIN 通過 Worker execution-time 權限驗證才能增改移除公司統編、加密 AMEGO App Key、MO店+ 密碼（加密金鑰不可與密文同存）；ADMIN 只能透過 UI 看統編／設定狀態，不能由設定查詢 API 讀取明文。CYID Windows Device 可保存已授權取得的公司憑證 **DPAPI 加密快取**，光貿正常但 D1 暫時無法連線時支持原本允許的正常開票；連線恢復後檢查版本、刷新／撤銷快取，已知拒絕 fail closed。離線無法即時套用新版本／撤銷，回歸應覆蓋此邊界，不新增 Cloud 斷線時繞過設定權限的寫入入口。
- [ ] **CYID Cloud 斷線權限／覆核行為（使用者 2026-10-15 已核准）**：D1／Work Item 無法連線時，暫停雲端跨機折讓申請建立／覆核核准／正式開立及其他需要原子執行鎖的操作，不採原先 `allowance_list` 離線暫配號、連線後才回填 D1 的提案；避免不同電腦用不同單號重複執行**同一個申請**（4040161／4040162 不保證防此情況）。Local 單機折讓用 SQLite＋光貿正常可用。CYID Cloud 的既有一般開立／發票查詢在光貿正常且符合最後可信身分安全降級條件下**繼續可用**；已確定的身分／Device 拒絕不得降級。已送出折讓的光貿唯讀查回可繼續，結果不明禁止盲送。需新增斷線矩陣與恢復回歸。

本決策取代本文舊章節中「Built-in 是正式支援模式」、「三版共用」及自架支援的**未來產品承諾**；舊的已完成工程與歷史遷移紀錄仍可供追溯，不能當成應繼續實作的待辦。

## 0. 接續優先順序

| 順序 | 工作 | 範圍與依據 |
| --- | --- | --- |
| 已完成 | V2.6.19 Release、CYID 啟用、Cloud0.9.3 與原 D1 舊帳號退役 | 正式 CI／部署與本次私有維護 read-back；不再列為待切換 |
| V3 接續 1 | 多機 Device／邀請與 CYWEB 超管復原 | §7.4／§7.5；使用者決定災復併入 V3，由 CYWEB 簽短效一次性復原邀請，沿用原 Workspace；設計完成，功能尚未實作 |
| V3 接續 2 | Work Item／revision／跨機 OrderID 防撞 | §7.5；與同一 Device／actor 模型接續，跨機原子性與結果不明，不將 Cloud Lock 變成所有業務前置條件 |
| V3 接續 3 | V3 折讓 API／不可覆寫逐筆歷史 | §1.1／§6／§7.5；依使用者決定整體重做，不回頭修 V2.6.19 |
| V3 接續 4 | 業務 Audit／查看介面 | §7.4／§7.6；現有 Device audit 保留，新增業務事件與 actor provider |
| 待補證據 | 原裝置權限刷新、斷線／恢復、撤銷中斷與 UI／AMEGO 行為 | §1／§4／§7.2／§7.3；缺實測證據不等於未實作或已發現 defect |
| 後續／延後 | 酷澎特殊樣本、多公司、營運摘要、高 DPI／cache 簽章 | §5／§8～§11；原 Built-in 自架手冊依 2026-10-15 決定取消 |

- [x] V2.6.19 正式 tag／ZIP／SHA-256 與 Windows／Cloud Client／package／安全掃描已完成。
- [x] PR #216／#379 head 已由 #380 合併；裝置管理、邀請、分流工程不重列為未實作。
- [ ] **待補實機證據 RC Y：**96 DPI 介面；125／150 DPI 與 High Contrast 尚無證據。使用版本／最後使用為成功啟動回報，持續 heartbeat 不在本次範圍。
- [ ] **待補實機證據 RC W：**等待→完成／錯誤→恢復、數量／公司隔離、舊折讓 null 分類與管理員結案。
- [ ] **待補實機證據 RC Z：**active 清單、新增視窗、A/B 啟動回報、離線值、超管改名與 revoked 歷史。

未勾選實機項目表示尚無可引用的 live 證據；本批發行已經使用者接受本地／CI 驗收，不要求重新逐項實機操作。新增功能依實際風險驗證，不能把 CI、health 或部署成功勾成真人全矩陣通過。

## 1.1 折讓人工處理歷史缺欄位（V3.0 整體重做；本批不修）

2026-10-13 使用者決定停止 V2.6.x 的這項缺陷修正，併入 V3.0 折讓／Cloud Work Item／Audit 正式重做；**此處不是已修復或已驗收**。V2.6.19 已知缺陷保留，不作本批 Release 阻塞。不可自行補造原本機遺失的人員、日期、原因、金額，也不因延期清空既有資料。

- [x] 既有 screenshot／source 差異與不確定根因已記錄在 CLOUD_WORK_HANDOFF 歷史停點；原機 DB／備份未取得，不宣稱恢復成功。
- [x] 使用者明確將本項留待 V3.0 重做；不在 V2.6.19 變動 metadata／sync_issues／UI／schema／VERSION。
- [ ] V3.0 設計時重新確認多機 Work Item、正式折讓 API、不可覆寫逐筆歷史、申請／處理 actor／時間／原因／金額、結束結果、同步／中斷與 retention；落實詳情狀態區別與可靠的舊資料處理。
- [ ] 對 V3.0 新流程提供對應的本地／整合與必要實機驗收。原 RC AD 轉成 V3 需求參考，不能把 V2 既有缺陷當作通過測試。

本批 Release 判斷依本地／隔離驗證及既有精確 source GitHub CI；不得上傳任何 Cloudflare 測試 Worker／版本，正式／development 雲端資料與裝置不因驗收而變更。

## 1. V2.6.x 實機與光貿驗證

- [ ] V2.6.18：原訂單再次送出由光貿拒絕；第一筆已開立保持不變，第二筆 Failed 只在上傳問題，重新整理／重啟不變成成功；手動與匯入批次成功／失敗計數正確。自動化與精確 head Windows CI 以 #380 checks 為準。
- [x] V2.6.18 Build 1 source：使用者核准受限舊雙成功修復，原同步 owner 回查同公司／環境／官方號碼／OrderID／完整內容，原生成 ID 與 SentAt 一致且官方開立時間介於兩次送出才決定先後；完整 SQLite 備份、快照競態保護、原子寫入及安全重試。只將確認的多餘嘗試轉 Failed，不按排序刪資料。自動化／Windows 結果見精確 head #380 checks。
- [ ] V2.6.18 Build 1 原機驗收：保留原 Data／Cache／Logs 升級，重新整理後核對一筆原成功與一筆上傳問題失敗、Data/RepairBackups 可讀，重啟／再整理／清除失敗不影響原發票。未取得原 Data，不宣稱已修復使用者資料；證據不足的個案保留歧義再核對。

- [ ] **Local／Built-in 專用證據：**全新首次設定建立超管、密碼規則、復原碼與重啟；本公司 CYID 帳號由中央管理。
- [ ] 首次設定尚未完成、尚未建立超級管理員時，主畫面右上角光貿連線 Tag 先顯示灰色「連線中」；完成首次設定後才執行既有光貿 API 連線檢查，再切換為綠色「光貿連線正常」或紅色「光貿連線異常」。
- [ ] 帳號管理／權限驗證：一般使用者、管理員、超級管理員的建立、修改、停用、重設與禁止操作邊界。
- [ ] MO店+ 未設定 Excel 密碼時，按 MO店+ 必須先提示設定且不得先開選檔視窗。
- [ ] 設定選單持續開啟至少 30 秒，確認不再連續閃爍。
- [ ] `設定 → 系統診斷`：重新檢查、API／SQLite／WebView2／Excel／印表機／同步狀態與複製摘要均符合實機狀態，且不顯示 App Key、密碼、發票內容或完整本機路徑。
- [ ] 測試環境實際送出直接作廢 `3015-消退` 與管理員覆核作廢 `3001-3015-消退`。
- [ ] 保留完整 `invoice_query` JSON，確認 AMEGO 是否回傳可解析的 `CancelReason`／作廢原因；目前不得假設未文件化欄位存在。
- [ ] 實測紙本未收回人工確認：一般使用者不能批准，管理員可批准／取消，批准後才送作廢。
- [ ] 實測作廢確認開啟期間，背景已開立清單所有可見發票號碼持續隱藏，即使背景重新整理也不會重新顯示。
- [ ] 實際建立一筆人工折讓，以 `invoice_query.allowance[]` 驗證同日舊／新折讓辨識、含稅金額比對與多候選保守停止。
- [ ] `/json/allowance_file` 三種版型 A4、A4 (地址+A5)、A5 均能取得有效 PDF 並由既有 WebViewer 正常開啟／列印。
- [ ] 折讓官方資料發生狀態／日期／類型／稅額／金額變動後，再次開啟 PDF 必須使用新官方資料指紋，不得命中舊 Cache。
- [ ] 「折讓作廢」暫行人工流程：一般使用者可提出，管理員可於「上傳問題」完成或取消退回，現階段不得直接呼叫 `/json/g0501`。
- [ ] 「上傳問題／處理中」兩清單正確分流、數量與明細一致；只有上傳問題內 Failed 可勾選清除，正常等待不能刪除／手動標成功。
- [ ] 超過兩期的發票作廢、折讓、折讓作廢待辦均顯示「可結案」，只有 ADMIN／SUPER_ADMIN 可結案；結案後只停止本機追蹤並恢復 retention 清理資格，不代表光貿已完成。

完整操作步驟見 `RC_TEST.md`。

## 2. 待取得實機資料後再決定

- [ ] **折讓作廢人工完成後的官方確認策略。** 目前由管理員確認已完成光貿網站操作後按「已人工處理」結束本機待辦；取得真實 `invoice_query.allowance[]` 折讓作廢樣本後，再判斷能否可靠地先回查官方狀態，無法確認時維持 pending。
- [ ] **CancelReason 回查。** 取得真實 invoice_query 樣本後，再決定完成作廢歷史能否跨重新啟動解析使用者／覆核管理員／原因；沒有官方欄位就不自行保存一套永久作廢歷史。

## 3. 已完成工程證據（不重排為 TODO）

V2.6.14 功能 source db1a3b95：Governance #1171、Cloud #377、Windows #266 全部通過，含 client contracts、WinForms label 測量、完整回歸、packaged smoke／safety scan。Development staged Run #8 attempt 2 已驗證 Cloud 0.8.9／storage 12／usage／rename；0012 一次套用，撤銷歷史保留。後續僅文件更新不推進 VERSION／BUILD，精確 head 以 PR checks 為準。


功能 commit 66ec3671 已通過 Governance #1137、Cloud #369、Windows #258。Core／Void／Employee void workflow（29/29）／Allowance／SQLite／Sync／SyncCoordinator 與 Windows smoke／package 全綠；歷史行政結案契約已修正，source TODO／FIXME／NotImplementedException 盤點沒有另找到明確未實作 placeholder。

人工折讓／折讓 PDF／人工折讓作廢／系統診斷已實作；Identity Foundation、Online Authority Freshness、Device inventory／revoke、crash-safe Cloud → Local reset 已合併。Built-in Cloud Email 忘記密碼已有 API／Windows UI／client，不再列為未實作，仍需 live 驗收。版本／CI／deployment 的精確證據與功能邊界見 CLOUD_WORK_HANDOFF.md。

## 4. V2.4.x／既有同步與 Cache 實機回歸

- [ ] 以 V2.3.0 真實 `Data` 備份升級到現行 SQLite，確認 DB 建立、舊 JSON 保留、資料筆數與內容一致。
- [ ] 正式環境 recent 3-day sync，確認可抓到另一台電腦／光貿端合法更新。
- [ ] 程式持續開啟超過 5 分鐘，確認啟動同步／每 5 分鐘同步不重疊，手動重新整理維持 30 秒冷卻。
- [ ] 每日第一次同步執行目前＋上一期完整校對，不影響 recent 3-day 核心。
- [ ] 雙擊不同日期發票都先 `invoice_query`；query 無法確認時不得把舊 Cache 冒充最新資料。
- [ ] 測試環境 namespaced OrderID 不與共享測試池其他資料撞號，UI 仍顯示原始可讀 OrderID。
- [ ] 發票 PDF／Preview Cache 不保存短效 `file_url`，retention 清理時同步移除對應 Cache。

## 5. 酷澎樣本補齊

- [ ] 未出貨／不同狀態樣本。
- [ ] 公司統編訂單樣本。
- [ ] 多商品訂單樣本。
- [ ] 數量大於 1 樣本。
- [ ] 折扣／負數調整樣本。
- [ ] 取得樣本後補 parser、金額、防重與測試案例。

## 6. 正式折讓 API（V3.0，先完成 API 再做 D1 協同）

目前仍以人工折讓／人工折讓作廢待辦為主；完成紀錄以 `invoice_query.allowance[]` 為官方依據。2026-10-13 使用者決定目前人工折讓歷史缺欄位不單獨修補，V3.0 隨正式折讓及 Cloud Work Item／Audit 重做；既有 V2 缺陷不因此視為修復。

**2026-10-14 使用者確認開發順序：**優先把折讓申請 → 管理員至光貿網站人工折讓改為 CYInvoice 直接呼叫 `/json/g0401`；CYID、單機版共用同一 Core/Gateway，不要把 API 功能依賴在 D1。開立後直接以確定的折讓單號查官方狀態，通過驗收再取消手動填折讓單號與人工挑選多筆候選的操作 UI；結果不明仍保留安全回查，不自動重送。

**官方 API 契約注意：**光貿 `/json/g0401` 要求呼叫端必填 `AllowanceNumber`（最長 16 字、不可重複）；公開成功回應只明列 `code`、`msg`，不能假定光貿會自動配號或回傳號碼。CYInvoice 必須自行產生、先保存同一唯一號碼，確保跨機不撞號；逾時只能用**原號**查 `/json/allowance_query`／`/json/allowance_status`。文件：https://invoice.amego.tw/api_doc/ 。

**2026-10-15 工程進度（Draft #440，尚未上線）：**已加入 Core 的 `AllowanceIssueRequest`／`AllowanceProductItem`、依使用者 2026-10-15 核准的 `AyyyyMMddXXX`（12 字、每公司／環境每日 001–999 後接 A00–ZZZ）`AllowanceNumberGenerator.Format` 與 `TryReadSequence`，與 `IAmegoGateway`／`AmegoClient` 的 `IssueAllowanceAsync`（g0401）／`QueryAllowanceAsync`（allowance_query）／`AllowanceStatusAsync`（allowance_status）及隔離 HTTP 測試。g0401 送出官方要求的 JSON 陣列；code 0 僅代表 API 接受，**不代表財政部上傳完成**。目前**沒有已接線的持久化 Pending Operation、跨機操作撞號防護、官方回查狀態機或 Windows 操作入口**，所以絕不能把下列功能勾完成，也不能直接啟用正式折讓。原人工流程與 g0501 完全未變動。CI 證據應對照本次精確 head；沒有 AMEGO live 驗收。下一步先實作送出前原號＋payload 本機 durable 存證、曾嘗試送出不可自動重送、按原號查回；再做部分／多次折讓金額核對、Work Item 協同及 UI 切換。\n\n- [ ] `/json/g0401` 折讓開立：CYInvoice 直接送出，不再要求管理員至光貿網站人工建立。
- [ ] **V3 管理員折讓覆核開窗即時官方資料（2026-10-15 使用者新需求）**：管理員開啟既有「折讓申請覆核」視窗時，針對**這張原發票號碼**呼叫**一次 `/json/invoice_query`**，從 `data.allowance[]` 顯示最新已開立／已作廢／等待中／錯誤的折讓單號、日期、金額（明確標示未稅＋稅／含稅）及官方狀態，並列原發票目前是否作廢、本次申請金額、申請人／原因／時間。**不要誤用 `/json/allowance_query` 用發票號碼查，該 API 必須帶既有折讓單號**；只有需查單筆詳情、跨多張原發票的折讓金額分攤或官方欄位不完整時，才針對已知折讓單號額外呼叫 `allowance_query`，不能把整張多發票折讓的總額直接算成此一原發票已折讓額。載入要顯示「正在查詢光貿」→「最新官方資料／查詢時間」或「查詢失敗」，不能把本機舊快照顯示成已即時確認；查詢失敗、發票已作廢、尚有影響可折額度的 pending／不明折讓、無法核對可折餘額時**禁用核准／正式開立**，仍允許管理員檢視／退回。操作中不要封鎖 UI 主執行緒，必要時支援手動重新查詢；核准到執行 `g0401` 前仍須重新核對官方最新狀態及當下管理員權限、D1 Work Item revision／原子配號，避免開窗後資料變更／其他裝置同時覆核。CYID 管理員 B 機**不保證有原機 SQLite 發票列**，應由原 Work Item 發票號碼＋已授權公司設定建立 Gateway 直接查光貿，不能用「B 機找不到本地 InvoiceRecord」拒絕看覆核詳情；Local 可沿用 `InvoiceDetailRefreshService` 核對的 Core，但同一 UI 規則。官方 `invoice_query` 查詢原發票日期 180 天限制外／未能確認時，不宣稱「沒有折讓」，走保守人工核對流程。這是**V3 待做 UI／Core／跨機整合**，現有 `SyncIssuesForm` 仍以本地舊快照列出折讓，不代表已完成。
- [ ] `/json/g0501` 折讓作廢：正式自動化後只允許 ADMIN／SUPER_ADMIN 直接執行；送出前重新確認最新折讓狀態，結果不明不得自行標成成功。
- [ ] **2026-10-15 折讓配號規則（已確認）**：`AyyyyMMddXXX`，固定 12 碼，當日 001～999；第 1000 筆起 A00、A01、…、A0A、…、AZZ、B00、…、ZZZ，上限 **34,695**。僅在管理員確認核准執行折讓時由原 Work Item 分配，申請／等待覆核／退回完全不占號。Cloud 於本公司的業務 D1 原子配號且永久保留所有號碼與 Audit；Local 單機不依賴 D1，需 SQLite 等價的 durable 保護。已分配號碼永不回收，包含取消、作廢、失敗、結果不明與光貿外部撞號。正式／測試環境隔離；兩臺 Cloud 裝置不得同號或重覆處理同一申請。
- [ ] **光貿手動折讓撞號後的受限自動換號（官方錯誤碼已核對）**：光貿 [官方 API 錯誤碼](https://invoice.amego.tw/info_detail?mid=71) 明訂 `4040161`＝「指定折讓單號 已存在折讓開立」、`4040162`＝「指定折讓單號 已存在作廢折讓」，**兩者都表示號碼已經使用且不能回收，採相同撞號處置**；`4040163`＝「指定折讓單號 折讓類型錯誤」，不可換號重試。Core `AmegoAllowanceErrorCodes.IsAllowanceNumberOccupied` 已分類 4040161／4040162，synthetic HTTP 測試涵蓋兩碼與 4040163 排除；Gateway 只傳遞錯誤碼，**尚未實作自動配號或重送**。待 Work Item/D1 原子配號及送出前 durable operation 完成後，只有**同一操作沒有任何先前不明送出、且這次已收到官方確切 4040161／4040162 拒絕**，才將原號永久標記 `occupied_external`、保留歷史 Audit，替同一 Work Item 原子分配新號並有限次重試；timeout／斷線／模糊結果以及可能是前次自身送出造成的 4040161／4040162，均必須查原號核對官方資料，不得直接換號。尚無 AMEGO live 驗收，不能宣稱流程已上線。
- [ ] `AllowanceNumber` 由 CYInvoice **送出前先產生、持久化原號**（光貿 g0401 必填，不是光貿自動配號）；不同裝置與單機模式都不能撞號，unknown 不換號重送。
- [ ] **V3 官方異動回補（2026-10-15 最新低呼叫量方案）**：若 CYInvoice Cloud D1 中斷但光貿正常，管理員可直接在光貿網站人工**作廢發票／開立折讓／作廢折讓**。復原後 CYInvoice 以既有 `invoice_list.cancel_date` 和新增 `allowance_list` 的 `allowance_number`／`invoice_type`／`invoice_status`／`cancel_date`／`product_item[].original_invoice_number` 比對**本機 SQLite 現有發票與官方折讓快照**，補回官方事實；當完整列表欄位不足或對應衝突時才調 `invoice_query`／`allowance_query`，不要逐張掃所有發票。`f0501`／`g0501` 是實際作廢操作 API，**絕不能當查詢 API**。外部網站操作**不得新增 D1 的 CYInvoice 執行操作 Audit**，不能杜撰操作者／核准／原因；原有申請 Work Item／Audit 永久保留，恢復後可更新**原申請**官方核對狀態，不另建正常發票 D1 索引。Local 同樣共用，無 D1。
- [ ] **列表導向、低 API 量的增量對帳**：保留目前 `InvoiceAutomaticSyncService` **每日首次當期＋前一期、其餘近 3 日**列表同步節奏，不再提議背景定期對每張發票打 `invoice_query`。發票作廢可直接從現有 `invoice_list.cancel_date` 得知（現有 `InvoiceSyncService.ApplyList` 已有處理），折讓不能從 invoice_list 判斷，須新增 `IAmegoGateway.ListAllowancesAsync`／`/json/allowance_list` 分頁（官方 limit 20–500）。同日頻繁重整只查必要的近期日期範圍，日首次較大範圍補當期＋前期；按公司／環境保存游標／成功時間，分批查完整 pages，網路失敗不清空前次快照、不自動重送任何發票／折讓。Cloud 的多臺裝置各自只取少量清單頁而非全發票逐筆詳查；暫不為此新增 D1 一般發票索引或中央 sync service。既有 pending、重複號／缺原發票品項、作廢折讓晚於其開立期別等例外，才用針對性唯讀查回。官方兩類 list 都沒有文件化的 last_modified 篩選，所以**不能只以新建立日期游標掃尾**來保證偵測舊折讓的事後作廢：需較寬區間定期回掃，以及針對本機已知舊折讓的 `allowance_status` 或明細回查策略；期限外未知舊資料的完整性另列限制。
- [ ] **官方折讓快照變更檢出／PDF cache 回歸**：`allowance_list` 的 `product_item[].original_invoice_number` 可將一張或多張原發票對應到同一折讓單；以折讓單號＋官方類型／作廢時間／狀態更新原 `cyinvoice_official_allowances`，不能新增重複發票或同折讓號的重複項，不能將失敗發票反查變成功。`InvoiceAllowanceMetadata.ApplyQuery` 已可存官方折讓快照，但現行 `InvoiceSyncService.Fingerprint`／`InvoiceDetailRefreshService.OfficialFingerprint` 沒有納入折讓快照，需修正變更持久化與相關 PDF Cache 失效。回查不明、分頁未齊、缺原發票對應時，保留上次可信資料並限定補打 `invoice_query`／`allowance_query`。驗證首次折讓、多次部分折讓、外部作廢折讓、原發票作廢、跨重啟、跨裝置、近／舊期、180 天限制、漏頁錯誤與增量同步重入。
- [ ] **官方按發票號碼查折讓／作廢紀錄**：光貿 [官方文件](https://invoice.amego.tw/api_doc/) 的 `invoice_list` 可查原發票 `cancel_date`，**沒有文件化的折讓摘要／修改標記**；`allowance_list` 可查折讓單號、D0401／D0501、上傳狀態、`cancel_date`、原發票 `product_item[].original_invoice_number`，適合整批增量對帳；`invoice_query` 的 `data.allowance[]` 可在開啟詳情或資料有疑義時針對單張查。超過 `invoice_query` 以發票日期計算 180 天限制（code 51）不得誤判無折讓。Core 現有 `QueryByInvoiceNumberAsync`／`ParseAllowances`，**allowance_list Gateway 已加入 Draft（待驗證），尚無本機合併與日常同步**，需實作並用真樣本核對 list 的選填欄位是否完整、作廢異動能否在相同折讓日期範圍被查出。
- [ ] `allowance_query`／`allowance_status` 正式查詢與同步模型；目前刻意共用 `invoice_query`。
- [ ] **光貿 allowance_status 新舊回應格式兼容（核對 API 文件發現）**：官方文件現列新版 `data:[{allowance_number,type,status,tax_amount,total_amount}]` 與舊版 `data:{"單號":{type,status,...}}`；目前 Draft `AmegoClient.AllowanceStatusAsync` 只解析舊 object，須新增 array 解析、單號精確匹配／重複拒絕與兩種隔離回歸後才能視為正式查詢可靠。
- [ ] 部分折讓、多次折讓、品項／數量／金額規則。
- [ ] 發票與折讓之間的 idempotency、timeout、結果不明與同步安全規則。
- [ ] 自動折讓／折讓作廢時，確認 AMEGO 正式可保存且可依折讓單號查回的欄位，再決定如何保存 `管理員編號-使用者編號-原因`；若官方沒有合適欄位，不濫用未文件化欄位。
- [ ] **先驗收 g0401** 的發送、相同折讓單號查回、結果不明與 Local／CYID 兩種版本共用後，再移除「管理員至光貿網站人工折讓」、「手填折讓單號」、「從多筆候選人工挑單號」的正常操作 UI；歷史及不明結果追查保留。**g0501 折讓作廢屬另一個 API 驗收點，不可隨 g0401 一併宣告完工；D1 折讓協同在 g0401 後再接。**

`/json/allowance_file` 折讓 PDF 已完成工程實作，後續只需實機驗證與雲端化時確認多裝置 Cache 行為。

## 7. CYInvoice V3.0 雲端協同

完整長期定位與分期見 `CLOUD_ROADMAP.md`；Workspace／Device／Token／Employee authority 定案見 `CLOUD_IDENTITY_LIFECYCLE.md`；CY ID consumer 邊界見 `CY_ID_INTEGRATION.md`。

### 7.1 已完成 foundation

Workspace／protected Device identity、pairing／invitation、whole-device transition、中央 Employee CRUD／OTP／transfer／password recovery、USER role migration、provider abstraction、freshness、revoke 與 reset 均已實作。9/29 development staged deploy Run #7 與 migration／aggregate／FK audit 有既有證據；A 機連線與 B 機 Run255 pairing 曾有人工作業證據。後續驗收必須用最新包，不沿用歷史版本成功推定新版本全通過。

### 7.2 目前剩餘驗證／修正

Built-in／Self-hosted 專屬 Employee identity／OTP／Email／超管移交矩陣共四項已依產品範圍定案移出 V3 待辦（取消，**不是通過驗收**）。既有 Device 與 CYID／Local 回歸仍按實際依賴保留。

- [ ] **待補 A/B baseline 證據：**兩台既有 Device 保留原安裝升級現行 V2.6.19 後仍可連線，Device Management 清單、一般同步／開票既有路徑無回歸。
- [ ] V2.6.12 邀請第一次加入與首次說明實機驗收（RC X）。10/09 舊包已取得「第一次錯誤、重開成功」證據，已定位 password.Clear → TextChanged → preview=null；修正版單次完成尚待使用者驗證。邀請撤銷／重寄與其他不明結果仍待實測。
- [ ] **Authority freshness A/B 實機驗收：**A 修改中央 Employee 密碼／role／enabled 後，B 不等待背景同步或重開即可在下一次 protected operation 套用最新 authority；雲端暫時不可用且光貿正常時沿用原降級可信 cache，恢復連線後 Online authority 重新覆蓋。
- [ ] **Cloud → Local 實機驗收：**優先用可拋棄的 Device C，在 A/B 仍保留 Workspace recovery path 的前提下測 current-device reset；確認 A/B、Workspace、中央 Employees 完整保留。
- [ ] **Device revoke A/B/C 實機驗收：**新增可拋棄 Device C，從 A/B 遠端 revoke C，確認 C 舊 Token 立即失效、history 保留 revoked、重新加入產生新 Device identity。
- [ ] **LAST_ACTIVE_DEVICE 實機驗收：**只在專用可拋棄 Workspace／環境驗證，不為測試而破壞目前 A/B 唯一 recovery topology。
- [ ] **Ambiguous revoke fault-injection：**模擬 Cloud 已執行但 response 遺失／狀態暫時無法確認，確認不 wipe、本機資料／Token 保留，下一次啟動由 self-status 收斂。
- Recovery Device flow 與 Token／recovery path 同時失效的人工維運程序，統一由 §7.4 追蹤。
- [ ] 驗收 fresh-install 首次分流。
- [ ] **延後／非目前阻塞：Cloud Employee offline cache 完整性簽章。** 未來可評估 server-signed snapshot／等效完整性保護；沒有實際竄改事件、威脅模型提高或稽核需求時長期擱置。

### 7.3 CYID Consumer（本公司已啟用；未取得的 live 證據保留）

- [x] activate-cyid #42 已啟用本公司 development CYID；Application／Workspace／private bindings 與原 business Workspace 已配置，原 Windows 帳密進設定有使用者回報。
- [x] 2026-10-13 原 Worker 升級0.9.3，保持 CYID 與全部 bindings；原 D1 四張舊表退役及私有備份／保留資料／FK 核對完成，見 §7.7。
- [ ] **待補證據 RC AA：**Account Management／settings／高權限操作與 App Access、Role、enabled、密碼變更後的原裝置更新；單台成功不代表全角色矩陣。
- [ ] **待補證據 RC AB：**四種服務狀態、原離線行為、恢復與輸入／Workspace 保留；帳密／權限／Device 拒絕不降級，不自動重送。
- CYID 0-active-Device recovery 與 LAST_ACTIVE_DEVICE 後續設計統一在 §7.4，不重開已完成的本公司 activation。

### 7.3.1 V2.6.16 服務連線收斂

- [x] 依使用者四點定案保留原有降級，加入 transient 503／HTML 503；共享光貿探測與核心入口防護，四狀態及純 Local 回歸。
- [x] MainForm 一個 15 秒檢查／modal blocker，重新檢查與恢復沿用原設定／輸入； known deny 後 503 不復活。Windows 原生視窗 smoke source 已加入，CI 結果以 #380 精確 head 為準。
- [x] VERSION 2.6.16／BUILD 0、Cloud source 0.9.1；無新 schema 或遠端部署，文件／CHANGELOG 同步。
- [ ] 待補 RC AB 原安裝四種狀態／純 Local／雙斷線提示／恢復證據；RC AA 角色矩陣仍未全驗，本公司 activation 已完成。

### 7.3.2 裝置／權限同步與撤銷自動重設（V2.6.17 source）

設計 CY_ID_INTEGRATION §14.6；停點 CLOUD_WORK_HANDOFF；人工 RC_TEST AC。不得拿 V2.6.16 CI 當成本批完成。

- [x] 盤點 portable package／全部 runtime 路徑及 canonical owner，確認 final-operation 入口與 in-flight AMEGO 結果查核；原 Workspace、降級及光貿同步週期保留。
- [x] CYID canonical private service-level change contract／實作／回歸，涵蓋 enabled、刪除、Role、App Access、credentialVersion／漏同步；同步版本、changelog、Consumer Impact／支援範圍，不直接讀 CYID D1 或以 login 輪詢替代。
- [x] 一次 aggregate Device self-status／binding／Provider change 請求，分項故障；revoked Token 只回自身清除證據，CYID outage 不掩蓋 revoke。
- [x] MainForm 啟動／60 秒／重連單一同步 lifecycle，Built-in 收斂；去重、stale response／known denial 保護，15 秒連線探測不重跑完整 feed，光貿 sync 不加頻。
- [x] 原 cache owner invalidation 涵蓋角色／停用／刪除／App Access／密碼及漏同步；不把新 principal 接到舊 password proof，新成功驗證才建立新可信 entry。
- [x] 收斂 dialog／core，同一最終操作 Device＋Employee＋最新 App Access／Role；不跳過執行前檢查，不保留跨操作 Session。
- [x] explicit remote revoke 接既有 LocalResetCoordinator／Program recovery；先 durable marker 再 shutdown／wipe／restart，補 Schedule memory-only kill 缺口，啟動不因空員工 cache 卡住。
- [x] Logs 及 root／subdirectory 所有程式管理 runtime 檔案完整清除，portable 回發行初始狀態；marker 最後刪，kill／partial wipe／檔案占用續清，停止背景寫回。Unknown Token／401／Workspace disabled／scope／503 不誤 wipe。
- [x] 本地回歸覆蓋 single request、真實 Provider／Built-in／Local、startup、cache／credential／permission／deleted employee、stale response、CYID outage＋revoke、重連、Logs／模擬程序中斷後 partial wipe；真實 kill／檔案占用另由 RC AC 驗收。
- [x] V2.6.17 Build 2 source 64f23381 的 Windows #275／Cloud #386／Governance #1184／CYID #105 與 engineering Artifact 通過；最新 head 結果仍以 PR #380 checks 為準。
- [ ] 待補 RC AC 實機撤銷／關閉／kill／下次啟動／Logs／初始設定／光貿結果不明，以及 RC AA／AB 全矩陣；不重開本公司已完成的切換。
- [x] 開始 runtime 新工作時按共通規則推進版本、同步 CHANGELOG／狀態／設計／驗收；版本為 2.6.17／BUILD 2，人工／staging 不冒充完成。

### 7.3.3 已完成部署與後續證據邊界（2026-10-13）

- [x] private IDENTITY Fetcher／IDENTITY_AUTHORITY RPC 配置、CYID activation 與0.9.3 原 Worker upgrade 有實際 read-back；配置／health 不代表完整正向權限實測。
- [ ] **待補證據：**真實 consumer named RPC、Employee／App Access／Role／verified Email、Free 用量與 Windows 多裝置 acceptance；合併追蹤 §7.3／RC AA～AC，不新增遠端測試 handler。
- [ ] **維運權限核對：**既有 temporary Workers Admin 是否仍需保留；不能由本次 MCP read/write 成功或拒絕推斷全部管理權限已回收。
- 初次切換前 Built-in continuity／staging／activation 條件留在交接歷史；本公司現為 CYID_ONLY，不能以關閉 flag 當回復指令。另一個 production promotion／Windows Release 仍屬獨立範圍。

### 7.4 V3 多機裝置復原、備份與安全紀錄

**2026-10-15 現行狀態：**[CYInvoice Draft #440](https://github.com/simonliu1118-byte/CYapps/pull/440) + [CYWEB Draft #151](https://github.com/simonliu1118-byte/chihyuan-web/pull/151) 已完成相應 Source／CI；CYInvoice [Cloud #38501668530](https://github.com/simonliu1118-byte/CYapps/actions/runs/38501668530)／[Windows #38501668552](https://github.com/simonliu1118-byte/CYapps/actions/runs/38501668552)／Governance 成功，CYWEB [Runtime #38501284806](https://github.com/simonliu1118-byte/chihyuan-web/actions/runs/38501284806)／Deployment contract／Governance 成功；**兩份 PR 未合併，正式服務未部署，實機 DR-01～DR-13 沒有驗收通過**。Windows UI 入口正式定案在**「直接加入雲端」內的「災難復原」按鈕**，首次啟動只有單機版／直接加入雲端兩選項。詳細單一最新停點見 [CLOUD_WORK_HANDOFF.md](CLOUD_WORK_HANDOFF.md)。

**下一輪工程待辦**（V3 專用，不與 AutoInput 測試包混版）：
- [ ] **CYWEB #151 明確 revoked 契約雙端聯測：**CYInvoice #440 已在 Draft source 加 `revoked`（只根據 persisted `revoked_at`／成功原子撤銷）與 consumed／expired fail-closed，增加 SQL-time guard、重複撤銷、lost-response status readback、claim/revoke race synthetic regression；等 exact-head CI 通過及 CYWEB #151 消費者合約對照後才可標 source 整合完成。真 D1／Email／Device 仍未驗。
- [ ] 雙 Repo 最新 diff、正式契約／權限／race 安全審查，補強 in-flight 業務 mutation 的 fencing/epoch 及依操作邊界再驗證；防止被撤銷舊 Device 在新 Device 切換後仍提交舊工作。
- [ ] CYWEB 操作 ID／寄送結果不明時的 UI 恢復體驗、超管保護與 Chrome／行動裝置人工驗收；任何時候不自動重寄、不產生另一有效邀請。
- [ ] **唯讀**核對正式 Worker／original D1／CYID Service Binding／Email provider／已有 migration history；形成 migration 0014 + optional CYID_ONLY issuer maintenance 的受控部署／rollback／readback 計畫。未獲使用者獨立批准前不更動原 Worker/D1，禁止測試 Worker。
- [ ] 真實 DR-01～DR-13／多機切換／接管時全數撤銷／原一般 JOIN／Built-in fixture／遺失 response／DPAPI 復原／失敗 rollback；憑人工及實際 API／裝置證據逐一勾選，不用 mock 充當真人通過。


2026-10-13 使用者決定：**災難復原與 V3 多機協同一起做，從 CYWEB 由 CYID 超管簽發復原邀請碼**。裝置／邀請／協同資料上 CYInvoice 業務 D1，CYID 身分權威與其 D1 分離。詳細見 [V3 多機與復原設計](CLOUD_DISASTER_RECOVERY.md)；不再要求獨立災復功能先於 V3 完成。

- [x] **設計與 source 核對：**區分可用舊裝置、有效加入碼、所有 Token 遺失及服務／資料毀損；確認 active 筆數不是可用 Token 數、現有 issuer Device FK 與 app-scoped Session 邊界。
- [ ] **V3 CYWEB 管理接線：**核對實際 CYWEB source／core Session／CSRF owner；超管簽發／撤銷／狀態查核經專用 private capability 與當次 CYInvoice App authority，不轉用 core token、不要求舊 Device。
  - [ ] **私有 RPC 原始碼已備齊，待部署與整合：**[CYWEB Draft #151](https://github.com/simonliu1118-byte/chihyuan-web/pull/151) 的三個 POST gate（issue／status／revoke）與 [CYInvoice Draft #440](https://github.com/simonliu1118-byte/CYapps/pull/440) 的 named WorkerEntrypoint、兩個 Workspace／Application 的即時 CYID 再授權、pending→Email 服務接受→sent 原子 Audit、同一操作只寄一次、狀態查回與明確撤銷均已實作／加入合成測試。**Windows 專用 preview／claim 與 UI 原始碼已在 Draft #440 接上；CYWEB Draft #151 已加入僅超管可見的簽發／狀態／撤銷 UI，以及部署時選用的 named Service Binding 注入與 CI 驗證；仍缺正式 Binding 啟用、原 D1 0014＋optional recovery 維護、跨服務／真 Email／Windows 實機 DR 驗收**，不能勾完成。

- [ ] **V3 邀請與 Audit schema：**forward migration 擴充 issuer channel／RECOVERY purpose／nullable CYWEB Device issuer 與 constraint，保留歷史 actor；原子單次 claim／Audit、失敗及 delivery 不明安全收斂。
  - [ ] **2026-10-14 全數撤銷決議／交易核心：**復原與一般 JOIN 區分；新 Device 成功兌換時須同一筆 D1 交易讓原 Workspace 所有其他 active Device revoked、未使用 JOIN／RECOVERY 邀請及 Pairing 授權碼失效、逐裝置撤銷與成功 Audit 原子寫入；簽發時不得提前撤銷，失敗必須全數 rollback。Draft #440 已新增內部 `recovery-claim.ts`、Pairing `revoked_at`、transaction guard 與 synthetic SQLite 測試；**private issuer、受限 CYID SUPER_ADMIN preview／claim 路由、Windows DPAPI Pending Token 均已有 Draft 原始碼與自動測試；尚未正式部署、尚未套用真實 D1，且實機未驗**。
  - [ ] 工程中：[CYInvoice Draft PR #440](https://github.com/simonliu1118-byte/CYapps/pull/440) 已加入 canonical 0014／optional CYID_ONLY issuer maintenance 與 synthetic Built-in／CYID_ONLY／FK／Audit／唯一 operation ID 測試；**尚未合併或套用原 D1；private 簽發與 claim 原始碼已有，但沒有正式部署／現場 DR 驗收**。與 [CYWEB Draft PR #151](https://github.com/simonliu1118-byte/chihyuan-web/pull/151) 的超管 Auth/CSRF gate 同步收斂，不把 CI 等同 DR 實機完成。
- [ ] **V3 新裝置復原加入：**沿用 protected Pending Token owner，固定兩 Workspace／Application scope；同 Token retry／重啟／scope mismatch／revoked 不復活，**成功加入原 Workspace 的同一原子交易自動撤銷所有舊 Device**（不先撤銷，不只選定失聯者）。LAST_ACTIVE_DEVICE 僅約束一般逐台撤銷；正式復原必須保證新 Device Active、其他全部 Revoked。
- [ ] **2026-10-14 Windows 新裝置復原串接（Draft 原始碼）：**`SettingsStore` 的獨立 DPAPI Pending Recovery（Cloud endpoint、業務 Workspace、Identity Workspace、Invitation ID、新裝置名稱、加密 Token），`CloudRecoveryClient` 專用 preview／claim（不用舊 Device Bearer），`CloudRecoveryForm` 由「直接加入雲端」頁的獨立復原按鈕開啟，並要求全數撤銷二次確認；首次啟動仍只保留兩個選項，`Cloud` 專用 CYID 重新驗證路由與原子 cutover 均已寫入 #440。任何斷線／結果不明不能產生新 Token，必須使用同一 Pending Token 與原邀請查回；**仍屬未發行、未上線／未實機驗收，故保持未勾選**。
- [ ] **2026-10-14 復原入口改位與 revoked Device 寫入競態：**首次啟動維持「使用單機版／直接加入雲端」兩選項，雲端加入頁提供「災難復原／繼續災難復原」按鈕，保持既有一般 JOIN 不變。舊 Device 已撤銷後，`createPairing`／`issueInvitation`／`claimInvitation` 均在 D1 寫入當下驗證 issuer active，阻止撤銷前已啟動、撤銷後才完成的授權請求。Synthetic SQLite 測試已加進 `recovery-cutover.test.mjs`；跨服務 in-flight 業務 mutation fencing 仍留在 V3 Work Item 範圍，須另驗收。
- [ ] **私有備份異機回復工具與保管：**設計建議 age recipient encryption；仍待核准 recipient／獨立金鑰及備份位置、工具、RPO／RTO／retention 與真正異機 drill。現有 DPAPI 只證明原帳戶／機器本地回復。
- [ ] **人工維運回復：**CYWEB／CYID／D1 故障先由各 owner 恢復服務；先保留目前 snapshot／新資料，核對 storage profile 與相容 Worker，遠端 restore 另以具體範圍批准。回復後舊 revoked Token／已用邀請不得倒退復活。
- [ ] **RC AE／DR-01～DR-13：**已完成多項 synthetic SQLite／Node／Windows 合約及 PR CI；**跨服務真 Email、受控真 Worker／D1、實際 Windows 裝置的 DR-01～DR-13 尚未執行／驗收**。業務 Audit／viewer 由 §7.6 共用追蹤。


- [ ] **V3 execution-time fencing：**已先把 /v1/devices/revoke 的 D1 同交易存活 Device guard 與 Built-in SUPER_ADMIN credential guard 實作並加遲到撤銷 SQLite regression。此僅為「裝置管理」實際 write fence；作廢／折讓還未接實際業務 Work Item（create/get/transition 原型及 Windows transport 已有 CI），**D1 事件／覆核資料範圍已定案，非尚待討論**。仍需最小業務狀態／Audit、Device/actor 執行當下 guard、光貿 side-effect 結果不明處理，不能勾整體完成。

### 7.5 跨機 Work Item / Sync

災難復原不獨立先行，與 §7.4 的 CYWEB 邀請／Device registry／actor 共用 V3 設計。新機加入不代表未上雲的舊機 pending 已找回，也不觸發 AMEGO 自動重送。

- [ ] V3.0 重做折讓歷史與狀態：逐筆申請／處理／官方結果／取消／行政結案可追溯；不能僅因 issue resolved 即顯示「已人工處理」，也不能以雲端同步推斷官方成功。
- [x] **D1 範圍決議（2026-10-14）**：D1 不是發票資料庫，不建立一般發票最小索引。正常開立且未申請、未出現異常的發票**不進 D1**；有紙本未收回作廢覆核、折讓／折讓作廢申請、退回，或「上傳問題」異常才建立必要事件紀錄與不可覆寫 Audit。折讓協同在 §6 g0401 API 升級與驗收後接。
- [ ] **整個現有「上傳問題」須跨機可見**：包含 `InvoiceSyncIssueStore` 記錄的 `invoice_list_failed`、`query_failed`、`remote_not_found`、`unknown_remote_not_found`、`reconciliation_failed`、`ambiguous_match`、`local_write_failed`，人工覆核事件，以及在相同 UI 顯示的「開立失敗」發票。D1 只存每次真正異常的事件摘要、Workspace／環境、必要發票／訂單識別、來源 Device、建立時間、處理人、狀態及 Audit；不存正常發票清單、完整品項、Excel、PDF、原始錯誤堆疊或光貿整份回應。
  - 同一未結案事件防重複；管理員可在其他電腦查看、追蹤、註記處理。**本機資料寫入／SQLite 損壞須由來源電腦修復**，遠端不能直接標成修好；**官方開票／上傳狀態不明必須查光貿**，雲端 `closed`／管理員已讀不能假裝官方成功。恢復後留下處理歷史。
- [ ] 作廢（未收回紙本）／折讓／折讓作廢申請以及退回／人工結案的跨機 Work Item／Audit；折讓待 §6 API 完成後再接。管理員用自己電腦接手，不需到申請人電腦。
- [ ] CYID_ONLY 專屬公司設定卡片：統編 + 加密 AMEGO App Key + 加密 MO店+ Excel 密碼；僅 SUPER_ADMIN 可新增／修改／移除。一般 ADMIN 仍可開設定，但只能看到公司統編及「已設定」狀態，不能取得 Secrets 明文；解密金鑰不可跟密文同放 D1。Built-in／單機不強制搬既有 DPAPI。
  - Draft #440 已新增最小三個受保護 POST 操作（create/get/transition）：Device Token + 每次管理員重新驗證，Built-in 的 credential 在 SQL 寫入時再核對，CYID_ONLY 不查已退役表；**Windows Core 的 `CloudWorkItemClient` 已接同一 API 契約並有 CI 測試**。SQL 0015 仍在 `drafts/` 未部署，故正式 Worker 不可用；未接 Windows 業務按鈕／UI 或官方作廢／折讓。
- [ ] Work Item 原子 state transition／optimistic revision，避免多機同時結案。
  - 2026-10-14 已在 Draft #440 加最小草案 `cloud/drafts/0015_work_items.sql`／`work-items.ts`：只存業務參考雜湊與 Work Item 狀態、CAS revision、實際 D1 寫入時 active Device+Workspace fence；支援 Built-in／CYID_ONLY synthetic test。**已加受保護的 API／Windows transport，但尚未接 Windows UI、未部署原 D1，也不能視為作廢／折讓官方狀態或已完成跨機流程。**
- [ ] Cloud coordination 只用於真正需要跨機原子性的點，不把 Cloud Lock 變成所有業務前置條件。
- [ ] 離線期間 AMEGO 結果不明操作不得因恢復連線而自動重送。
- [x] **相同 OrderID 重複開立的既有防護（已核對 Core＋Sync＋WinForms 程式／測試；不重做）：**正式 MO店+／酷澎／鼎新 ERP 使用來源原始 `order.OrderId` 送光貿，官方負責擋相同 OrderID；測試環境沿用原本 `TestOrderIdPrefix`。**第一次成功開立的原紀錄持續存在正常發票清單**；再次以同 OrderID 送出而收到重複錯誤（含 `3040171`）時，第二次嘗試狀態改為 `Failed`、發票號碼清空，**只在「上傳問題 → 開立失敗：重複訂單編號」顯示，不進正常發票清單，也不參與重新整理時的官方查詢**。這是刻意保留失敗嘗試的行為，不代表產生第二張成功發票或缺少本機防重。已由 `TestWireDuplicateRejectionAsync`、`TestIssueOpensAndBlocksResendAsync`、`TestRecoveryOfExistingInvoiceAsync` 及 Windows `RecordsControl.Matches`、`InvoiceService.RefreshAllAsync` 現行測試／程式確認；**禁止再把第二筆失敗列當成待修復的「重複有效發票」**。
- [ ] **V3 僅調整失敗事件的跨機呈現／保存方式**（目前 D1／UI 尚未實作）：沿用上述原本分流，將「上傳問題」的失敗事件同步 D1，含原始 OrderID、錯誤類型與必要 Audit，讓管理員跨機看見；原成功發票仍只存在本機正常發票清單，不因此上傳 D1。原本「清除失敗」改為**封存事件**，保留歷程；不改光貿 OrderID 判重、不改已正常運作的開立和重新整理路徑。真正由程式自行產生的手動 OrderID 唯一性，獨立依原 §7.5 討論，不必為此加整體 Cloud Lock。
- [ ] **結果不明管理員處置（需求已確認，尚未實作）：**① 管理員聯絡光貿，將詢問原因／回覆文字及操作人、時間登錄 D1 事件 Audit；② 管理員確認光貿實際已成功時，可憑官方證據手動補入發票號碼、開立日期與時間，將**原本那筆**本機紀錄更新成已開立（不得 Append 第二筆），保留完整人工補登 Audit；③ 管理員自行在光貿網站核對確實未開立後，明確批准以**同一原始 OrderID** 再送一次，紀錄確認、核准與重送歷程。前兩項不觸發光貿重送；第三項即使送出後結果不明，仍不能自動連續重試。
- [ ] **開立失敗由清除改封存**：現有 `FailedInvoiceRecordStore.Delete` 會刪本機發票列及相關 `sync_issues`，未來事件／Audit 上 D1 後，對使用者改為封存、保留追蹤歷程與管理員身分；預設待辦清單可隱藏封存，但不可因此刪 Audit。光貿成功但本機保存失敗，也走管理員可見的事件／人工修復（從原設備完成本機紀錄回補），不誤認官方結果不明。
- [ ] **處理中不搬成額外的永久事件：**光貿作廢已送出、仍在處理的正常進度留在既有「處理中」列表，完成後從該列表消失；只有變成例外事件或管理員必須介入時才建立 D1 異常待辦。

### 7.6 Cloud Audit

- [ ] 補齊 Employee、Work Item、SUPER_ADMIN transfer 與維運事件的必要摘要；現有 Device pairing／invitation／join／revoke audit 不重做。

Audit 沿用既有公開／敏感資料邊界，不保存密碼、Recovery Code、OTP、Device Token、App Key 或不必要的完整發票內容；這是持續約束，不另列一個可勾選功能。

### 7.7 CYID 上線後本公司 D1 舊帳號退役（2026-10-13 已完成）

本公司原 D1 經獨立授權完成一次性 CYID_ONLY 維護；其他公司 Built-in／Self-hosted 保留自己的新 D1／原 migrations／原程式。§19 為 credential-only 歷史提案，現行實作與完成證據見 CLOUD_IDENTITY_LIFECYCLE §20。

- [x] **原 D1 唯讀盤點：**活躍 Worker／D1／business Workspace、CYID=true、實際筆數／verifier、transition、OTP、invitation actor 與 FK 已核對，未由 source 猜測即時資料。
- [x] **雙 storage-profile 回歸：**PR #432／Cloud Check #38340836036 驗證 CYID-only 刪表／資料保留、Built-in schema 保留及錯 scope／未完成 transition 拒絕；本次真實私有備份另外還原 SQLite、逐筆比對及模擬同一 guarded SQL。
- [x] **相容 source 已合併：**維護 SQL 不進一般 migrations；CYID rename 不再解析已退役表，history audit 依 storage profile 保持保護。
- [x] **原 Worker 0.9.3 已部署：**[Development Deploy #38396129643](https://github.com/simonliu1118-byte/CYapps/actions/runs/38396129643) 成功，保持 CYID=true／全部原 bindings／API1／marker8／storage13，storage health 正常。
- [x] **備份／授權／一次性原子維護：**私有 DPAPI 備份及回復 checkpoint 保存，解密／本地還原／完整資料核對成功後才清理原 D1；私人資料／回復資訊不進 Public repo。
- [x] **真實 D1 與後驗證：**四張員工／轉換表物理移除，CYID_ONLY marker 對應原 Workspace；invitation 舊 actor 保留、舊員工 FK 移除；Device／Token／pairing／invitation／audit／migration history 逐筆符合預期；pairing OTP 保留、舊帳號 OTP／recovery fields 清理；foreign_key_check 無違規、quick_check ok。Provider 未變、未操作 CYID D1。
- **驗收限制：**identity ready、舊 Built-in route 拒絕與缺 Device 授權拒絕已檢查；真人帳密／多裝置矩陣仍依 §7.3／RC_TEST 保留未驗，不冒稱全矩陣通過。
- [ ] **V3 actor ownership：**Work Item／Audit 區分 CYID 外部 actor 與 Built-in 歷史，不靠員編猜同人；合併追蹤 §7.5／§7.6。

**2026-10-15 後 V3 方向改為移除 Built-in 正式支援；此句原為當時歷史界線。**清理 source 仍須逐項驗證，不代表已刪或授權刪遠端 D1；更不代表 Windows Release 或部署授權。

## 8. 後續大版本：多公司 Workspace

不列入 V3.0 阻塞項目。

- [ ] 真正有不同統編需求時再新增 Company entity／`company_id`。
- [ ] V3 單公司 Workspace 升級時自動建立第一個 Company。
- [ ] 視實際需求再做 Employee ↔ Company scope、Device 預設 Company、公司切換 UI、跨公司待辦／查詢／報表。

## 9. 對外相容／自架準備（V3 取消）

2026-10-15 使用者決定不再交付 Built-in／Self-hosted。原自架手冊與 Built-in 相容承諾從 V3 待辦移除；第三方自行 fork，團隊不負責這條產品線的部署與驗收。

## 10. 後續增強

Built-in Cloud Email password recovery 屬歷史工程完成紀錄；V3 已取消該模式，不再安排專屬實機驗收。

- [ ] MO 密碼安全 Cloud sync。
- [ ] 小型營運摘要：今日／本月開票張數與金額、待處理工作數、同步異常數。

AMEGO App Key 不列入 Cloud sync，仍由各電腦自行設定並以 Windows secure storage 保護。

## 11. 延後／非本次阻塞

- [ ] 125%／150% DPI 人工驗證：依 repository visual 決策維持 Deferred；本次 96 DPI 分流驗收不能冒充其他縮放通過。

Offline cache 整體簽章（7.2）、多公司（8）與營運摘要（10）保持既定延後範圍，沒有本次完成承諾。

## 12. 單機版明確不排入

- 本機操作／稽核紀錄：不做；等 Cloud backend 後做跨機 Audit。
- 本機備份／還原：不做；CYInvoice 是中介層，官方資料以光貿為準。
- 發票 Excel／CSV 匯出：不做；需要時使用光貿網站。
- 管理員開機待辦提醒：不做；沒有持續登入，無法可靠知道開程式的人是管理員。

工程 merge 與 development Cloud deployment 在達到對應 CI／Governance／安全驗證 gate 後可依技術需要進行，不應讓已完成的 stacked work 長期停放；**正式 tag / Release 仍視為獨立 promotion 行為，需另行明確決定。**
