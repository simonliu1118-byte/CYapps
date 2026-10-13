# CYInvoice 待辦與驗證

本檔追蹤目前未完成、需要後續驗證或已明確延後的工作；7.3 保留本輪完成核對項供切換 gate 追溯。已完成內容與歷史決策由 README、PR、測試與設計文件保存。

更新日期：2026-10-13（Asia/Taipei）。目前 **V2.6.19 Build 0**；#216／#379 已由 #380 進入 main，#420／#422 完成 CYID development 啟用與證據。原機 CYID 帳密進設定已由使用者確認。2026-10-13 使用者決定折讓人工歷史缺欄位不在 V2.6.19 修正，併入 V3.0 折讓流程重做；本批其餘項目准以本地／隔離測試及已通過的精確 source CI 作版本驗收，無須額外要求使用者逐項實機操作。此決定只調整本批驗收 gate，不代表尚未執行的實機或實際 Cloud 權限驗收已通過。**本批正式 Release 已完成**：2026-10-13 [CYInvoice V2.6.19](https://github.com/simonliu1118-byte/CYapps/releases/tag/cyinvoice-v2.6.19)，Windows 正式 [GitHub Actions #38336787968](https://github.com/simonliu1118-byte/CYapps/actions/runs/38336787968) 27 steps success，由 main `95fa5deab097c262d55e67391f32693d68573954` 建置；Release 附正式 ZIP 與 SHA-256。原 #38336017123 因 Cloud.Tests 命令不當而失敗，已由治理 PR #429 修正並在成功 run 中通過測試；未產生重複 tag。折讓歷史缺陷留待 V3.0，沒有改 Cloudflare Worker／D1。

唯一現行交接：[CLOUD_WORK_HANDOFF.md](CLOUD_WORK_HANDOFF.md)；唯一步驟清單：[RC_TEST.md](RC_TEST.md)。9/29 NEXT_CHAT_HANDOFF 與 V2.5 設計均為歷史資料，不能重新開啟已完成工作。接手前仍依 AGENTS.md 先讀三層永久規則，本檔不是額外規則層。

Cloud source／原 development 0.9.2／API1／marker8／storage13，actual #42 已核對 CYID_ENABLED=true 與原 Workspace／Device Token hashes／history／FK 保留。單台原機設定驗證成功；其餘角色／裝置、60 秒刷新／斷線／撤銷的真實多裝置操作未做，交由既有自動化／本地替身作本批版本驗收，未取得的 live 證據仍如實保留供後續確認。**禁止為測試建立／上傳／部署任何 Cloudflare 測試 Worker／測試版本／handler；既有無法刪除的測試 Worker 不重試刪除、不增建替代物；不更動現有 Cloudflare Worker、D1 或真實裝置／帳號。** 舊版本段落為歷史證據，不能當現行旗標。

## 0. 接續優先順序

- [x] **V2.6.19 正式發行已完成**：tag `cyinvoice-v2.6.19`、Windows/Cloud Client tests、PE/package/portable smoke、安全掃描、SHA-256 與 GitHub Release 均完成；[正式 Run](https://github.com/simonliu1118-byte/CYapps/actions/runs/38336787968)。此項不再列為阻塞；未取得 live AA／AB／AC 證據維持其實際未驗狀態。

| 優先 | 未完成工作 | 驗收位置／依賴 |
| --- | --- | --- |
| V3.0 延後 | 折讓人工處理歷史缺申請／處理欄位；隨 V3 雲端 Work Item／正式折讓流程整體重做 | §1.1／§6／§7.5；V2.6.19 已知缺陷，不再阻擋本批 Release |
| 本批本地核准 | V2.6.19 查無買方名稱可人工輸入／留空以統編送出 | RC D.12；source 6c499108 的 Windows #281／Cloud #393／Governance #1201 與工程包通過；本批不以新的真人實機操作為必要條件 |
| 目前 | V2.6.14 裝置管理、使用版本／最後使用與改名；保留 V2.6.13 介面 | RC Z／Y；source CI 已通過、人工尚待驗收 |
| 目前 | V2.6.12 邀請首次加入與首次說明 | RC X；舊包重開成功有使用者證據，修正版首次成功待實機 |
| 目前 | V2.6.11 處理中／上傳問題分流、舊資料結案、Danger button／號碼隱藏 | RC W；Windows #258 通過，人工待測 |
| 目前 | A/B 最新工程版基線與即時中央權限／Offline reconnect | RC Q／R；四包已實作，不再重做 |
| 目前 | 可拋棄 C 的 revoke／reset 與 ambiguous 恢復 | RC S／T／U；A/B recovery path 保留 |
| 接續 | Invitation、Employee identity matrix／CRUD／Email／password recovery／transfer | 7.2；使用最新測試包，舊 Run343 Artifact 已到期 |
| 本輪 | CYID Consumer source CI／Windows 實機／隔離 staging 與切換計畫 | 7.3／RC AA；provider／gateway／offline 已接線，正式切換及 0-Device recovery 未完成 |
| V3 協同 | Cloud Work Item／原子結案／revision、多機 OrderID 防撞、Audit | 7.5／7.6；identity gate 後分項開發 |
| 已完成 source、後續遠端 gate | **本公司 CYID-only D1 實體移除 Built-in 員工表**；PR #432 原始碼／SQLite／CYID／Windows CI 全綠，**線上原 D1 未執行刪表** | 7.7／CLOUD_IDENTITY_LIFECYCLE §20；唯一原 business D1、原 Workspace／Device／Audit、私有備份核對後再核准維護 |
| 後續 | 正式折讓 API／全域單號、自架手冊、酷澎樣本 | 5／6／9；不可用人工流程冒充完成 |

- [ ] 完成 RC Y 的介面實機驗收（96 DPI）；125／150 DPI 與 High Contrast 尚未取得證據。使用版本／最後使用為成功啟動回報，不代表即時在線；持續 heartbeat 不在本次範圍。
- [ ] 完成 RC W 的實機驗收並記錄包／環境／結果：正常等待→完成、等待→錯誤→恢復、數量／公司隔離、舊折讓 null 分類、無 active upload issue 的作廢仍能管理員結案。
- [x] PR #216／#379 head 已包含於 #380 ancestry 並合併 main；V2.6.14 裝置管理／邀請／分流實機驗收仍在 RC Z／X／W，不以工程合併代替。

本檔既有未勾選實機項目代表「尚未取得可引用證據」，不等於已發現 defect。**使用者本次批准改用本地驗收作 V2.6.19 本批發行判斷；未執行的真實 AMEGO、Windows DPI、CYID 角色與多裝置撤銷等仍不能標成實機通過。** 已完成工程證據集中在交接／CHANGELOG，不把 CI 自動勾成人工成功。

- [ ] 完成 RC Z：active 清單、獨立新增視窗標籤／欄位、A/B 升級重啟回報、離線值保留、超管改名與 revoked 歷史仍可由雲端管理查詢。

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

- [ ] 全新首次設定：建立超級管理員、密碼規則、一次性復原碼與重新啟動。
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

## 6. 正式折讓 API（V3.0 雲端化階段）

目前仍以人工折讓／人工折讓作廢待辦為主；完成紀錄以 `invoice_query.allowance[]` 為官方依據。2026-10-13 使用者決定目前人工折讓歷史缺欄位不單獨修補，V3.0 隨正式折讓及 Cloud Work Item／Audit 重做；既有 V2 缺陷不因此視為修復。

- [ ] `/json/g0401` 折讓開立：CYInvoice 直接送出，不再要求管理員至光貿網站人工建立。
- [ ] `/json/g0501` 折讓作廢：正式自動化後只允許 ADMIN／SUPER_ADMIN 直接執行；送出前重新確認最新折讓狀態，結果不明不得自行標成成功。
- [ ] `AllowanceNumber` 由 CYInvoice 自動產生，建立全域唯一編號與跨裝置防重機制。
- [ ] `allowance_query`／`allowance_status` 正式查詢與同步模型；目前刻意共用 `invoice_query`。
- [ ] 部分折讓、多次折讓、品項／數量／金額規則。
- [ ] 發票與折讓之間的 idempotency、timeout、結果不明與同步安全規則。
- [ ] 自動折讓／折讓作廢時，確認 AMEGO 正式可保存且可依折讓單號查回的欄位，再決定如何保存 `管理員編號-使用者編號-原因`；若官方沒有合適欄位，不濫用未文件化欄位。
- [ ] 雲端化完成後移除暫行人工折讓與人工折讓作廢路徑，收斂為正式 API 流程。

`/json/allowance_file` 折讓 PDF 已完成工程實作，後續只需實機驗證與雲端化時確認多裝置 Cache 行為。

## 7. CYInvoice V3.0 雲端協同

完整長期定位與分期見 `CLOUD_ROADMAP.md`；Workspace／Device／Token／Employee authority 定案見 `CLOUD_IDENTITY_LIFECYCLE.md`；CY ID consumer 邊界見 `CY_ID_INTEGRATION.md`。

### 7.1 已完成 foundation

Workspace／protected Device identity、pairing／invitation、whole-device transition、中央 Employee CRUD／OTP／transfer／password recovery、USER role migration、provider abstraction、freshness、revoke 與 reset 均已實作。9/29 development staged deploy Run #7 與 migration／aggregate／FK audit 有既有證據；A 機連線與 B 機 Run255 pairing 曾有人工作業證據。後續驗收必須用最新包，不沿用歷史版本成功推定新版本全通過。

### 7.2 目前剩餘驗證／修正

- [ ] **目前候選版 A/B baseline acceptance：**兩台既有 Device 升級 V2.6.14 通過 CI 的工程包後仍可連線，Device Management 清單、一般同步／開票既有路徑無回歸。
- [ ] V2.6.12 邀請第一次加入與首次說明實機驗收（RC X）。10/09 舊包已取得「第一次錯誤、重開成功」證據，已定位 password.Clear → TextChanged → preview=null；修正版單次完成尚待使用者驗證。邀請撤銷／重寄與其他不明結果仍待實測。
- [ ] 精確命中、全新 Employee、Employee No only、Email only、兩欄各撞不同人的實機／integration 測試。
- [ ] **Authority freshness A/B 實機驗收：**A 修改中央 Employee 密碼／role／enabled 後，B 不等待背景同步或重開即可在下一次 protected operation 套用最新 authority；雲端暫時不可用且光貿正常時沿用原降級可信 cache，恢復連線後 Online authority 重新覆蓋。
- [ ] Built-in Employee CRUD、Email OTP、password、enabled、role 在 A/B 間即時與背景 snapshot 同步實機測試。
- [ ] Built-in Cloud「忘記密碼」員編／已驗證 Email challenge、重寄倒數、OTP／新密碼 confirm 及 A/B 舊密碼失效實機驗收；工程已實作。
- [ ] SUPER_ADMIN transfer 的 execution-time re-auth／OTP／原子 X→ADMIN、Y→SUPER_ADMIN／Recovery Email 與 A/B authority 驗收。
- [ ] **Cloud → Local 實機驗收：**優先用可拋棄的 Device C，在 A/B 仍保留 Workspace recovery path 的前提下測 current-device reset；確認 A/B、Workspace、中央 Employees 完整保留。
- [ ] **Device revoke A/B/C 實機驗收：**新增可拋棄 Device C，從 A/B 遠端 revoke C，確認 C 舊 Token 立即失效、history 保留 revoked、重新加入產生新 Device identity。
- [ ] **LAST_ACTIVE_DEVICE 實機驗收：**只在專用可拋棄 Workspace／環境驗證，不為測試而破壞目前 A/B 唯一 recovery topology。
- [ ] **Ambiguous revoke fault-injection：**模擬 Cloud 已執行但 response 遺失／狀態暫時無法確認，確認不 wipe、本機資料／Token 保留，下一次啟動由 self-status 收斂。
- [ ] **尚未實作：**所有 Device Token 遺失但 Recovery path 可用時的 Recovery Device flow。
- [ ] **尚未完成文件：**所有 Device Token + recovery path 同時失效時的 reference-backend 人工維運程序。
- [ ] 驗收 fresh-install 首次分流。
- [ ] **延後／非目前阻塞：Cloud Employee offline cache 完整性簽章。** 未來可評估 server-signed snapshot／等效完整性保護；沒有實際竄改事件、威脅模型提高或稽核需求時長期擱置。

### 7.3 CYID Consumer（V2.6.16 source 已實作，正式切換未完成）

設計與授權／限制見 CY_ID_INTEGRATION.md §14，PR #379 的 §13 保留原交接快照。Canonical 1.0.3／minimum 1.0.0；同 repo 直接引用，不建 contract mirror。

- [x] 核對 main 94f559cd、PR #216 V2.6.14 head 1e6d4137、PR #379 文件；整合新獨立工作分支，VERSION 2.6.15／BUILD 0。
- [x] CyIdIdentityProvider、集中 provider 選擇、authenticated protected Workspace／Device binding；Local／Built-in 保留，確認 CYID 後不自動回退。
- [x] Worker private IDENTITY gateway、Login／Resolve／finally Logout、穩定錯誤與無 Session／verifier 外洩；Device 與 Employee／App Access 權限分離。
- [x] Device rename／revoke、pairing／invitation 沿用現有 mutation owner；0013 forward migration 區分 external invitation actor，保留 Built-in 歷史／FK。
- [x] last-trusted protected offline cache，V2.6.16 沿用原降級：雲端 transport／timeout／暫時 5xx 且光貿正常；online reject 清除 cache、reconnect 新權限重生效；不 export CYID verifier。
- [x] Consumer Version 1.0.3（初次接入 1.0.2）、canonical support-window gate、real-provider Worker 與 C# contracts／拒絕／scope／outage／logout loss／onboarding 本機回歸。
- [x] 原 Workspace 接續 source regression：原兩台 Device Token 切換前後可用；本機切換後重開保留 Workspace／Device／Token、發票 pending、買方／PDF、公司設定及 protected credentials；錯綁定不覆寫。

本輪精確 head Governance／Cloud／Windows CI 與 engineering Artifact 以 [#380 checks](https://github.com/simonliu1118-byte/CYapps/pull/380/checks) 為單一即時結果；source／治理證據見 CLOUD_WORK_HANDOFF，不用手動 checklist 鏡像 run 狀態。

- [x] 專案 canonical adoption 依 governance/* 流程完成 #381／Governance 2.3.34，功能分支同步 main；不把規則修改混入一般功能 PR。
- [ ] RC_TEST AA Windows 實機 CYID：Account Management／settings／高權限操作，斷網／reconnect 與角色、App Access、停用、密碼變更後更新。
- [ ] 核對真實 CYID Application 註冊／Workspace enablement／App Access、private Service Binding、兩 Workspace 配對；2026-10-11 使用者委託推進至上線，先隔離 staging，正式切換前完成可檢視的驗收／回復方案。
- [ ] 受控切換：備份／0013／FK、EmployeeNo 與歷史 actor／pending 業務稽核、rollback／舊 client gate；不以 destructive reset 代替 migration。
- [ ] 切換前所有 active 裝置在原安裝升級至 CYID-capable Windows（目前工程包 V2.6.19），員工啟用／App Access／Role／verified 超管 Email 備妥；staging 證明原 Workspace、Device、Token、資料及業務接續。不達 gate 維持 Built-in，不要求重建／重新加入。
- [ ] RC_TEST AB 實機驗收四種狀態、Built-in／CYID 503 原降級、純 Local 光貿斷線阻擋、任一恢復及輸入／Workspace 保留；帳密／權限／Device 拒絕不降級，不自動重送。
- [ ] CYID 0-active-Device recovery 實作／驗證後才調整 LAST_ACTIVE_DEVICE；目前保留原保護。
- [x] 人工结案／折讓及中央帳號操作 dialog／core 收斂見 §7.3.2；多階段 OTP／必要讀取仍各自最終驗證，不保存跨操作 Session。

### 7.3.1 V2.6.16 服務連線收斂

- [x] 依使用者四點定案保留原有降級，加入 transient 503／HTML 503；共享光貿探測與核心入口防護，四狀態及純 Local 回歸。
- [x] MainForm 一個 15 秒檢查／modal blocker，重新檢查與恢復沿用原設定／輸入； known deny 後 503 不復活。Windows 原生視窗 smoke source 已加入，CI 結果以 #380 精確 head 為準。
- [x] VERSION 2.6.16／BUILD 0、Cloud source 0.9.1；無新 schema 或遠端部署，文件／CHANGELOG 同步。
- [ ] RC AB 在原安裝測四種狀態／純 Local／雙斷線提示／恢復；RC AA 原 CYID staging gate 保留未完成。

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
- [ ] RC AC 實機撤銷／關閉／kill／下次啟動／Logs／初始設定／光貿結果不明；原 RC AA／AB 與正式切換 gate 保留。
- [x] 開始 runtime 新工作時按共通規則推進版本、同步 CHANGELOG／狀態／設計／驗收；版本為 2.6.17／BUILD 2，人工／staging 不冒充完成。

### 7.3.3 部署前檢查與分段上線（2026-10-11）

- [x] 修正 development 部署驗收的舊 0.9.0 hard-code；由 Cloud package.json 核對版本及 runtime-sync capability，保留原部署 owner。
- [x] 唯讀 preflight、原 DB／Built-in／runtime secret names／rate limit／canonical migration prefix／FK／歷史 fingerprint／private recovery checkpoint source 與回歸；0013 真實 SQLite 前後延續及 Token hash 更換拒絕通過。
- [x] 遠端 development preflight #11：原 Cloud 0.8.9／storage 12、1 Workspace／4 Device records、僅 pending 0013、FK／private Time Travel／Worker checkpoint 可用；不把既有 development target 自稱隔離 sandbox。
- [x] additive development Cloud #12：0.9.2／storage 13，CYID 關閉；原資料／Token hashes／history／API compatibility 與 runtime-sync capability 驗收通過。沒有 secret replacement／authority switch；Windows 當次認證／業務與 UI 驗收仍獨立。
- [x] #380 工程基準／canonical 1.0.3 已合併至 main `59d63b67`；功能 head `46723a89` 七項 CI 通過。
- [x] CY Web 七個 manifest mirror 已同步 main canonical 1.0.3（governance #145，main a0cb4675，Governance #243／deploy PR validation #136 通過）；其 consumer 維持支援窗內 1.0.2，不改 Web runtime。
- [x] Provider development 0.3.6 code-only upgrade：#384 source 348185fb 四項 CI 通過，preflight #123／實際 upgrade #124 通過；原七個 migration、vars／bindings／Secret names／hashed authority／history counts／FK 保留，invalid Session 拒絕正常；UTC recovery reference 2026-10-11T04:20:13.295Z。沒有 Secret replacement／remote migration／bootstrap／authority cutover。
- [ ] Private named RPC 與既有 consumer 實際 binding／帳號授權驗收，依下列 staging scope／Windows gate；Provider health 不等於該能力已 exercised。
- [x] 依使用者定案移除遠端probe／cleanup CLI、測試模組與workflow入口；保留過期未部署no-D1版本，停止DELETE重試／擴權／測試上傳。歷史證據見Provider DEVELOPMENT_DEPLOYMENT；刪除10339不再是接入先決條件。
- [x] Exact active-version inheritance真實business部署：#402六項CI通過並合併；actual #25（38134710138）same-tested-tree exact-source binding readback／100% deployment成功。原Cloud0.9.2／storage13／Built-in／Workspace／Device Token hashes／employee／history／FK／routes保留，expired未部署no-D1 probe仍1個；無test upload／DELETE／Secret replacement／authority switch。Recovery UTC2026-10-11T11:13:08.522Z；詳細source/tree/run見Provider DEVELOPMENT_DEPLOYMENT。Old probe/cleanup refs已停放到安全tree，不重跑舊run。
- [x] 在原 development deployment owner 加入 `prepare-cyid` source：Built-in 保留、missing scope 於任何 remote request 前拒絕、原 active Workspace／完整 schema／既有綁定衝突檢查、只新增兩個 approved services、exact-active inheritance／readback；本地回歸含 new／partial／existing bindings 與拒絕路徑。不新增 remote test code／migration／Workspace／authority。
- [x] 共通 Provider input／scope resolver source 收斂：各部署流程共用 CYID_WORKER_NAME Secret；Invoice 讀 Provider active Worker 的專屬 scope、原 business D1 唯一 active Workspace，以及只作排除的 Provider D1 binding ID，不再要求 GitHub 複製整組 IDs。不借 CYACC App／scope，Provider drift／歧義停止；本地回歸通過，CI 依變更 PR。
- [x] 共通 CYID_WORKER_NAME Secret 已由使用者設定；#413 七項 exact-head CI 通過並合併，唯讀 #29（38191352230）保留原 Cloud／Workspace／Device hashes／history／FK／routes，CYACC main #608（38191534386）validate／deploy 通過；無 Invoice code upload／Identity D1 access／authority switch。證據見現行交接。
- [x] 使用者透過既有 CY Web development 中央管理完成 CYInvoice 登記／目前身分 Workspace 啟用，並配置 Provider 兩項 Invoice runtime scope；唯讀 #38（38203645406）確認 configured=true。原業務 Workspace 保留，不重播 bootstrap、不讀 CYID D1、不借 CYACC scope。這不代表一般員工已授予 App Access。
- [x] 實際 prepare-cyid #39（38203790273），source 2e21c870／tree c1d303e4，IDENTITY／IDENTITY_AUTHORITY 和三個 approved vars readback／100% business 部署通過。原 Cloud0.9.2／schema13／Workspace1／Device4／Token hashes／employee／history／FK／routes 保留；migration skipped、private cleanup 通過、Built-in／CYID false。精確證據及工程包見 CLOUD_WORK_HANDOFF 最新停點。
- [x] 2026-10-12 使用者確認原 Windows 升級後發票／設定保留且原帳密可進設定，授權接續；此為單次原安裝 Built-in continuity 回報，不勾選整份 AA／AB／AC 或全部角色／裝置。
- [x] 原 development 受控 activate-cyid：#420 head70514413／treec8fb38c1 全部 CI 通過並合併；actual #42（38206372325）source8488ce3d／同 tree 確認 CYID_ENABLED=true、100% business 部署、原 Workspace1／Device4／Token hashes／history／FK／routes 保留，migration skipped／private cleanup 通過。Recovery UTC2026-10-12T05:05:10.603Z；無 reset／rejoin／D1 restore／production／Release。
- [x] 使用者於 2026-10-12 16:52／16:59 回報原 V2.6.19 安裝重開、CYID 員工帳密進設定與原資料續用正常；此為單台使用者回報。一般員工 App Access／Role、其餘裝置／private RPC／AA／AB／AC 仍另驗，不自動授權或擴大勾選。
- [x] 官方active與retired部署入口已同步Secret並read-back：Provider development #153／preflight #154通過；Invoice #31於Provider切換時在upload前阻擋，等待Provider完成的新snapshot #32通過，原Workspace／Device hashes／history／Builtin保留、無test upload／authority switch。Exact source／refs／run evidence見現行交接及Provider DEVELOPMENT_DEPLOYMENT。
- [x] 使用者已刪除 development 同名 CYID_WORKER_NAME Variable 並保留 Secret；唯讀 #34（38195315134）通過，原 Cloud0.9.2／storage13／Workspace／Device／Builtin／FK 保留，Provider scope 仍 false，無 upload／Identity D1 access。
- [ ] 已配置的 private IDENTITY Fetcher／IDENTITY_AUTHORITY RPC 實際 transport、真實 Employee／App Access／Role／verified Email／原 Windows 驗收，使用真實 consumer 功能，不新增遠端測試 handler；temporary Workers Admin 取消待確認。
- [x] 使用者全部裝置停用窗口的受控上傳／原服務安全核對：#395 六項 CI；#396 active-version audit／guard source 六項 CI／43 focused／100 Provider tests 通過並合併。唯讀 #19 通過 active version health／original D1／FK／Workspace／Device hashes／employee/history 在 #19 window 的保留；恢復 UTC 2026-10-11T07:41:20.449Z。這不代替 cleanup、#17 pre-upload snapshot 比對、真實 Windows Device 認證／本機 backup／回復或正式切換。
- [ ] 原 Windows 安裝先完整備份並升級 V2.6.19，Built-in 下核對原 Workspace／Device／Token／設定／資料。scope 配置已完成；仍須真實 Employee／App Access／Role／Email readiness、Free 用量，以及受控正向／變更／撤銷／outage 驗收。依使用者決定不再上傳隔離測試程式，不在原業務資料造 synthetic 帳號；撤銷清除僅在已備份並指定可撤銷裝置的窗口執行。
- [ ] staging、RC AA／AB／AC 與原 A/B/C 完成後提出正式切換／回復方案；正式 tag／Release 版本身分另依 PROJECT_RULES 明確處理。

### 7.4 新裝置加入方式與安全紀錄

現行只保留配對碼／邀請碼；API URL 由使用者設定，Windows 不內嵌 endpoint／不以 Workspace ID 作一般手動入口。Security audit 已涵蓋 pairing／invitation／join／revoke 的必要事件，相關 live 驗收集中 7.2，不重複勾選。

- [ ] 安全操作紀錄／稽核紀錄查看介面。
- [ ] 所有原裝置皆遺失且無有效邀請的受控災難復原流程；與 7.2 的 Recovery Device flow／人工維運手冊一起收斂。

### 7.5 跨機 Work Item / Sync

- [ ] V3.0 重做折讓歷史與狀態：逐筆申請／處理／官方結果／取消／行政結案可追溯；不能僅因 issue resolved 即顯示「已人工處理」，也不能以雲端同步推斷官方成功。
- [ ] 作廢／折讓／折讓作廢／管理員結案 Work Item 上 Cloud。
- [ ] Work Item 原子 state transition／optimistic revision，避免多機同時結案。
- [ ] Cloud coordination 只用於真正需要跨機原子性的點，不把 Cloud Lock 變成所有業務前置條件。
- [ ] 離線期間 AMEGO 結果不明操作不得因恢復連線而自動重送。
- [ ] 解決多機離線自動 OrderID 撞號；正式多機上線前需 Device namespace／短碼或等效方案。

### 7.6 Cloud Audit

- [ ] 補齊 Employee、Work Item、SUPER_ADMIN transfer 與維運事件的必要摘要；現有 Device pairing／invitation／join／revoke audit 不重做。

Audit 沿用既有公開／敏感資料邊界，不保存密碼、Recovery Code、OTP、Device Token、App Key 或不必要的完整發票內容；這是持續約束，不另列一個可勾選功能。

### 7.7 CYID 上線後 CYInvoice D1 舊帳號資料退役（2026-10-13 規劃，尚未執行）

使用者確認 TODO 已打勾的工作視為完成；本公司目前已全面轉由 CYID 管理帳號，要求原 CYInvoice 業務 D1 **實體移除 Built-in Employee／Credential／Transition 相關表**。未來其他公司 Built-in／Self-hosted 使用自己的新 D1，程式功能與原 migration 保留。§19 為初版 credential-only 歷史規劃，**現行落地方案見 `CLOUD_IDENTITY_LIFECYCLE.md` §20**。本批是 source 與本地 CI 實作，尚無遠端 D1 刪表授權／實際數據，不新建測試 Worker。

- [x] 原始碼唯讀盤點：`CYID_ENABLED=true` 的 Worker 拒絕舊員工與 Web Auth route，`runtime-sync` 改走 CYID 權限服務；既有 Built-in/self-hosted 模式仍被支援，不能全域 DROP 帳號表。
- [x] SQL schema 唯讀盤點：`cloud_employees` 仍被 `device_employee_links`、`employee_link_candidates`、`employee_transition_items`、舊 `device_invitations.issued_by_employee_id` 以 FK 引用；`email_otp_challenges` 亦被現行 CYID 配對 OTP 使用。先保留歷史 actor、Device、Workspace、Audit。
- [ ] **唯讀原 D1 盤點 gate：**以受控只讀方式確認目前活躍 Worker／原 D1 binding／目標 Workspace 及 `CYID_ENABLED`，收集匿名化 table counts、舊 verifier 非 NULL 筆數、FK/歷史、未完成 transition、有效 pairing OTP、舊／CYID invitation actor；目前尚無直接即時 D1 查詢結果，不能填入或推測資料筆數。禁止將姓名、Email、雜湊值、Secrets、實際 ID 寫到 Public PR 或 log。
- [ ] **本地雙模式回歸：**用本地 synthetic SQLite／Worker fixture 驗證 CYID mode 在舊 verifier 為 NULL 時，裝置／邀請／Email OTP／權限／runtime sync 不回歸；同時驗證 Built-in/self-hosted 的帳號 CRUD、recover、transfer 與裝置流程繼續有效。
- [x] **CYID-only 物理 schema 清理 source 已實作：**獨立 `cloud/maintenance/retire-built-in-for-cyid-only.sql`（不進常規 migrations）移除 `cloud_employees`、`device_employee_links`、`employee_link_candidates`、`employee_transition_items`；重建 invitation FK 保留 old actor、保留 Device／Workspace／Audit／共用配對 OTP，並以 profile marker 標記合法清理。Cloud source **0.9.3 已於 PR #432 合併 main**；原 business Worker 實際活躍版本尚未升級，**尚未操作真實 D1**。
- [x] **本地／CI source 驗證完成：**[PR #432](https://github.com/simonliu1118-byte/CYapps/pull/432) 合併 main `0cc97c9a`；[Cloud Check #38340836036](https://github.com/simonliu1118-byte/CYapps/actions/runs/38340836036) Linux／SQLite／CYID real-provider／Windows Client／工程包與安全掃描全綠，[Development Cloud Validate #38340836071](https://github.com/simonliu1118-byte/CYapps/actions/runs/38340836071) 僅 validate 成功，遠端 deploy job **skipped**；[Governance #38340836060](https://github.com/simonliu1118-byte/CYapps/actions/runs/38340836060) 成功。這是本地／CI 證據，不代表原 D1 已清理或原 Worker 已部署。
- [ ] **原 Worker 相容部署 gate：**未來只在核准原 business Worker 升級 0.9.3 且留住 `CYID_ENABLED=true`、原 D1／service binding／Device／history 后才能進行清表；先 code-only 後 D1 maintenance，不能用測試 Worker/handler 驗收。
- [ ] **真實 D1 清理 gate：**獲得原 D1 最新唯讀證據、業務影響盤點、私有備份／回復窗口及確切使用者批准後，才執行 CYID-only 完整舊帳號／轉換表移除。原 D1、CYID D1 與任何測試 Worker 目前均未修改。
- [ ] **V3.0 設計銜接：**Work Item／Cloud Audit 新增 actor 必須明確區分 CYID 外部身分與舊 Built-in 歷史，不能靠員編猜測同人；跨機原子操作、OrderID 防撞與正式折讓流程等核心功能仍獨立規劃。

**資源保護：**不建立／上傳／部署任何 Cloudflare 測試 Worker／handler，不重試刪除舊測試 Worker，不刪 CYID D1，不變更原 Workspace／Device/Token，不把 Local EmployeeStore 或 Built-in/self-hosted 的正式支持一起移除。

## 8. 後續大版本：多公司 Workspace

不列入 V3.0 阻塞項目。

- [ ] 真正有不同統編需求時再新增 Company entity／`company_id`。
- [ ] V3 單公司 Workspace 升級時自動建立第一個 Company。
- [ ] 視實際需求再做 Employee ↔ Company scope、Device 預設 Company、公司切換 UI、跨公司待辦／查詢／報表。

## 9. 對外相容／自架準備

- [ ] Cloud API 穩定後撰寫 CYInvoice Self-hosted / Cloud Integration User Manual。
- [ ] Guide 定義 Built-in Cloud 部署需求、endpoint、schema、Device auth、Employee authorization、錯誤碼、版本、idempotency／reconciliation 語意；不得要求 CY ID 才能完成基本部署。

## 10. 後續增強

Built-in Cloud Email password recovery 工程已完成，實機驗收見 7.2。

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
