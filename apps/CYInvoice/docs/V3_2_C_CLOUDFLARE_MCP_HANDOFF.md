# 本輪待本機配套：Cloud0.9.8／V3.1.44 Build1

2026-10-28。原PR #550／同分支；**以下0.9.8尚未執行，下方0.9.7維護已完成，不重跑。** 交付時指定的完整程式SHA及同SHA必要CI／Artifact為唯一來源，不取浮動HEAD。僅修同機折讓與log，不切LocalOnly或改交易owner。

1. 下載同SHA的V3.1.44 Build1工程包，核對Artifact雜湊、路徑、VERSION／BUILD、不含Data／log；交付使用者，不自動覆寫原EXE／Data或啟動程式。
2. 循原canonical deployment-preflight，以原Worker、原D1、同Source及新的私有checkpoint完成before→deploy→after，只更新0.9.7→0.9.8程式。沒有schema-upgrade、migration、DDL、匯出或allowance-activation意圖。schema／marker22、history0001～0022、三正常旗標true、舊兩旗標關閉及原Bindings／Secrets／Routes／CYID scope全保留；不新增資源、不改CYID D1。
3. 沿原owner有界核對；預估直接D1讀1200／寫0、runtime8、控制面90；本批停止參考2000／0／12／110。依既有規則核對當批額度，不延用前批例外；限流立即停止，不為拆分共用帳號歸屬補查。實際用量、差異、回復證據私有交回，公開只放遮罩摘要。
4. 核對線上bundle與指定Source一致，health Cloud0.9.8、storage=ok、identityProvider=CYID、storageSchemaVersion22；無憑證拒絕檢查不含交易內容，不冒充正向驗收。不用原失敗申請重新核准來補證據，不清Claim或資料，不合併／Release／真交易。
5. 原不可變0.9.7版本與私有checkpoint保留。原schema22並未改變，但0.9.6舊reader仍不能當新姓名資料的直接回退；Worker回退不回退D1，不自動整庫還原。背景核准後使用原Grant authority snapshot與既有私有撤權服務，不依賴已登出的真人Session。

## 本輪驗收及log

使用Build1配套Cloud0.9.8，先驗同機申請／核准及正確發票／訂單、等待／失敗分類。核准Grant保存後回覆已核准／處理中，後續沿原coordinator與runtime/sync自動完成，不再由主管按繼續。原未知交易不重送。

程式同目錄`log`可直接壓縮私下交回，包含HTTP／後端事件及單筆問題JSONL；新紀錄不含密碼、Token、公司Key、姓名／買受人或原始交易內容。每日日誌4MiB輪替一份前段、14天，單筆問題證據保留；舊Data/Logs唯讀可匯出。寫入失敗須顯示，不造假已記錄。先前單筆唯讀診斷只證明核准尚未持久化，真實舊錯誤缺證據，不能推定就是逾時。原始識別、帳號計量與備份只留私有。

---

# CYInvoice Cloud 0.9.7原Worker更新完成摘要／維護索引

更新：2026-10-28（Asia/Taipei）。原PR #550／`cyinvoice/feat-v32-de-void-coordination`，工程V3.1.44 Build 0／Cloud0.9.7；正式Release仍V3.1.0。**使用者已由本機對話交回完成報告，遠端維護已停止，不重跑。** 本頁依該報告歸檔遮罩摘要，本對話未重新連線Cloudflare；私有設定、完整資源識別、帳號計量、checkpoint、備份與回復收據不提交Public Git／PR。

## 本機已完成（2026-10-28）

- 指定並實際部署Source：`29b65ab21a01bd7759b0955ad68ff059948ef473`。原Worker0.9.6→0.9.7，單一版本100%流量；原canonical deployment-preflight、同Source與專用私有checkpoint的before→deploy→after均通過。無schema-upgrade或allowance-activation意圖。
- schema及marker22，history精確0001～0022且前後相同。無migration、DDL、DB匯出、回填、還原、新Worker／D1或CYID D1操作；原身分／稽核指紋、FK、Bindings、Secrets繼承、Routes及服務scope保持。
- API／g0401／g0501三正常旗標維持true；舊MANUAL_REVIEW／EXTERNAL_COLLISION維持未設定、關閉。沒有重新啟用舊人工路徑。
- 線上bundle與指定Source完整建置一致；health為Cloud0.9.7、storage=ok、identityProvider=CYID、storageSchemaVersion=22。legacy schemaVersion8不是實際D1版次。無憑證create／approve／void-approve均401／DEVICE_INVALID；只證明Device拒絕有效，不代替正向角色或真光貿驗收。
- 同Source的[Windows run1076](https://github.com/simonliu1118-byte/CYapps/actions/runs/40546364833)、[Cloud](https://github.com/simonliu1118-byte/CYapps/actions/runs/40546364838)、[Governance](https://github.com/simonliu1118-byte/CYapps/actions/runs/40546365002)、[Development預檢](https://github.com/simonliu1118-byte/CYapps/actions/runs/40546365016)成功。CI實際部署job skipped；實際更新由本機MCP完成，本機TypeScript、完整Cloud隔離測試及dry-run通過。
- `CYInvoice_V3.1.44_engineering-run1076.zip`已由本機下載並核對[Artifact12752164809](https://github.com/simonliu1118-byte/CYapps/actions/runs/40546364833/artifacts/12752164809)一致、ZIP路徑／VERSION3.1.44／BUILD0及不含runtime資料。原EXE／Data未覆寫，工程包未執行或安裝；使用者須使用新版程式進行驗收。
- 本批直接操作計數及預估差異由本機私有收據保存，已記錄直接操作低於原估與各自停止界線。直接SQL不是完整計費量，共用帳號差額不能全歸因本批，不能宣稱完整計費read已獨立證實低於參考上限；不為拆分歸屬再查遠端。
- 未重盤Local舊資料，不把前批限定快照的0筆外推目前全域；未知申請仍按原ID查回。無真光貿交易、PR合併或正式Release。本報告不延伸授權後續維護、回復或財務操作。

## 變更與資料相容界線

- 0.9.7直接修正原allowance owner：稽核trigger會增加D1 `meta.changes`，原本等於1的成功判定可能在提交後錯回409；改為正整數，原SQL主鍵／CAS及權限不放寬。來源驗證依官方D1 `changes`定義及隨Wrangler的Miniflare D1 worker `total_changes()`差值，SQLite隔離測試重現；本對話沒有連線真D1探測。
- 新申請人姓名由已驗證CYID principal保存至原加密envelope，核准姓名存原Grant snapshot；不新增表或schema，不送到光貿，不放入Device最小摘要。新reader可讀舊3欄envelope與新可選requester欄；舊資料不造假補姓名。
- **新增具姓名的請求後，舊0.9.6嚴格reader可能拒讀新envelope。不能僅因schema仍22就直接降版。** 回退前先評估新申請／Grant／未知Ledger／Claim與新格式，選擇可讀新資料的修正版；不得改寫舊authority、解除Claim、財務重送、自動還原D1或反向migration。Worker回退不回退D1。
- Windows送出成功立即投影原待辦；回覆不明只做一次原ID查回，不重做create。未知保留原上傳問題與診斷；啟動／既有同步恢復舊版僅marker的申請。舊版已提交的同ID申請不能另建一筆。

## 使用者測試重點

使用V3.1.44配套原Worker0.9.7：一般員工提出折讓應立即顯示「等待主管核准」並進原待辦；主管核准才執行。核對CYID申請／核准姓名與實際身份、詳情及歷程、清單欄位、帳密同列；使用者先前公司卡驗收維持。測試票應取得今天及前兩天原日期識別碼發票（含作廢），不查無識別碼陌生票；正式保留期不變。原未知申請只查回，不能為測試清Claim或刪原資料。其他未驗財務項仍依唯一現行計畫；雙Windows真人完整驗收延期決議不變。

---

# 歷史完成紀錄：Cloud 0.9.6啟用與限定舊案盤點

更新：2026-10-27（Asia/Taipei）。原PR #550／`cyinvoice/feat-v32-de-void-coordination`；工程V3.1.43 Build 0；正式Release仍V3.1.0，未合併或發布。

**使用者已交回本機MCP完整完成報告，遠端操作已停止。** 本頁只歸檔可公開摘要；本對話未重新連線Cloudflare。實際私有收據、完整資源識別、備份、設定、checkpoint及回復證據均保留本機，不提交Public Git／PR。產品範圍及驗收唯一來源仍為[V3.2現行計畫](V3_2_IMPLEMENTATION_PLAN.md)。

## 完成的部署與旗標

- 指定並實際部署Source：`25ae151ed880cb6256a4cc1e01ce499145a802b7`。原Worker Cloud 0.9.6、單一版本100%流量；原不可變0.9.5版本保留。
- 沿原canonical deployment-preflight，以deploy＋`CYINVOICE_ALLOWANCE_ACTIVATION=enable`、相同Source與專用checkpoint完成before→deploy→after。無schema-upgrade意圖、migration或DDL；schema／marker維持22，history精確0001～0022。
- 線上app.js為329377 bytes，SHA256 `374f279974a0e3d43b7a1959c1742f3c6d03ee99f4c8a0742564957487462d00`，與指定Source建置一致。health為Cloud 0.9.6、storage=ok、identityProvider=CYID、storageSchemaVersion=22；legacy schemaVersion=8不是D1版次。
- 原Company Settings及加密綁定、CYID／D1／scope／Secrets繼承／Routes及其他vars保留；只新增兩個正常送出旗標變數。APP_ENV保持原值，交易環境由原申請保存。未新增Worker／D1、未直接操作CYID D1。

| 旗標 | 本機完成報告確認結果 |
| --- | --- |
| ALLOWANCE_API_ENABLED | true |
| ALLOWANCE_G0401_SEND_ENABLED | true |
| ALLOWANCE_G0501_SEND_ENABLED | true |
| ALLOWANCE_MANUAL_REVIEW_ENABLED | 未設定，關閉 |
| ALLOWANCE_EXTERNAL_COLLISION_ENABLED | 未設定，關閉 |

無憑證、無交易內容的create／approve／void-approve入口均401／DEVICE_INVALID，已不再是503／ALLOWANCES_NOT_READY。結合旗標及bundle回讀，證明功能已啟用並保留Device拒絕；不能當成正向角色或真光貿流程驗收。MCP未使用真Device Token、員工密碼、公司卡或財務內容，真光貿交易為0。

## 同批舊資料及入口收尾

- 指定桌面安裝的原Data已在程式未執行時完整備份，以DPAPI CurrentUser加密；記憶體解密逐檔核對並實際還原SQLite，quick_check／FK通過。盤點僅讀備份副本，未啟動App或執行bootstrap／migration；前後原Data雜湊、檔案數不變，原EXE及登入設定未覆寫。跨Windows使用者解密未驗。
- 該快照只有已找到的測試帳戶同步索引；未核准草稿、已核准Local metadata／Ledger、舊pending／Cloud提交marker、未知Void Claim、歷史處置及財務稽核等當次查詢分類均0。**此結果只適用該份快照，不能推定其他安裝、裝置、未提供正式帳戶或未盤點表也為0。**
- 原業務D1申請、核准、Ledger、作廢申請、Void Claim、Void attempts六表完成主鍵有界唯讀盤點，當次均無舊案；其他表不因此記0，原稽核及身分歷史保留。
- 本批沒有可逐筆收尾的既有財務案；未DELETE、回填authority、解鎖Claim、改寫投影或假造官方完成。舊案若再出現，仍沿原owner及證據查回，不按同號／同金額猜測，不跨權威重送。
- 舊「已人工完成」入口與直接結案方法已從同Source及V3.1.43工程包移除，沒有另一套待辦。**原桌面EXE未自動更新，使用者需使用已交付V3.1.43工程包，才會看到新版入口及環境投影修正。** 不更動舊發票清理政策。

## Source CI與工程包

指定完整SHA的[Windows](https://github.com/simonliu1118-byte/CYapps/actions/runs/40230081363)、[Cloud](https://github.com/simonliu1118-byte/CYapps/actions/runs/40230081307)、[Governance](https://github.com/simonliu1118-byte/CYapps/actions/runs/40230081258)及[Development預檢](https://github.com/simonliu1118-byte/CYapps/actions/runs/40230081257)均成功。Development實際部署job skipped；實際部署由本機MCP完成。Cloud Windows畫面smoke同SHA失敗job重跑後成功，未更改120秒上限。

本機TypeScript、完整Cloud隔離測試及Worker dry-run通過，含交易環境保存／恢復、一次核准、25次明確拒號、未知不重送、有限作廢重試、舊案只查回與精確投影；合成案例不代表真人已測。正式規則／CYID契約未改，共通2.10.0與AITeam main一致，CYID Consumer Impact: NONE。

工程包`CYInvoice_V3.1.43_engineering-run1073.zip`已由使用者本機下載保存，不依賴Artifact保留期限；[原Artifact 12612925250](https://github.com/simonliu1118-byte/CYapps/actions/runs/40230081363/artifacts/12612925250)為81177580 bytes，SHA256 `d4ddde799e93cde25b9ca951e0573a67c63e21d46b49f7893c0c992d9d1b4256`。本機已核對與原Artifact一致、ZIP路徑安全及VERSION=3.1.43／BUILD=0，未執行或安裝，不含Data，不是正式Release。

## 用量與回復界線

本機已依該批另行同意的用量及有界盤點範圍完成，實際直接D1 SQL、runtime、控制平面及Local副本查詢量與預估差異已在使用者完整報告／私有收據交回，已記錄作業均在該批上限內。本次文件歸檔沒有新增Cloudflare讀寫或請求。

直接SQL meta不是完整計費read；共用帳號差額包含其他服務／runtime內部SQL及延遲，不能全歸因本批。原報告未能獨立證明本批完整計費read精確值或是否低於參考上限；維護已停止，不為拆分計費重跑查核。

本批未重新匯出D1，歷史schema22備份不宣稱涵蓋後續新寫入。若啟用後停送／回退，先評估新申請、未知Claim及設定變更；Worker回退不回退D1，不重跑0017～0022、不自動整庫還原。本頁是完成紀錄，不是重做部署、盤點或回復的指令。

## 已完成維護索引

以下為已歸檔維護的來源索引，取代過時的待執行／暫停清單；完整備份、實際還原及逐筆操作收據仍留本機私有。這些歷史狀態不覆蓋上方Cloud 0.9.7現況，也不是重新套migration或部署的指令。

| 作業日 | 已完成範圍 | 指定Source | 結果 |
| --- | --- | --- | --- |
| 2026-10-22 | schema18→19→20 | `cf6f4e282090e8a96d9358b8a33214f6df665279` | 0019／0020各成功一次；對應備份實際還原、DB後驗及marker更新完成 |
| 2026-10-22 | schema20→21 | `83e7b691fc494186e7a0d373654c888e83874fde` | 0021成功一次；備份實際還原及原Worker marker21完成 |
| 2026-10-23 | schema21→22／Cloud 0.9.4 | `16f6a6ce03681b157ceec2805657d3a409935221` | 0022成功一次；備份實際還原及原Worker marker22完成 |
| 2026-10-26 | Cloud 0.9.4→0.9.5 | `cff89085adb72536437ba2edc88b3d21d056de5a` | 僅程式更新；schema22、原綁定及當時關閉旗標保留 |
| 2026-10-27 | Cloud 0.9.5→0.9.6 | `25ae151ed880cb6256a4cc1e01ce499145a802b7` | 僅程式與已授權正常旗標更新；schema／marker22，限定舊案盤點完成 |

| 2026-10-28 | Cloud 0.9.6→0.9.7 | `29b65ab21a01bd7759b0955ad68ff059948ef473` | 原Worker程式更新完成；schema22、原正常旗標及綁定保留，未重盤Local |

## 剩餘真人驗收

以已下載V3.1.44工程包配套Cloud0.9.7及使用者自行設定的公司卡，驗收真實金鑰可用性、角色正向操作、主管一次核准後自動配號／送出／查回、正常等待／異常分類、斷線重啟、診斷與g0501有限重試。不能以旗標、無憑證拒絕或合成測試代替真人財務結果。

公司卡選取／編輯／同步／重啟取卡的既有使用者驗收保留。雙Windows／不同Device真人驗收依既定決議於V3.2上線後補做，不新增上線阻擋條件。V3.2尚未全部業務驗收完成；PR未合併、正式Release仍需使用者當次授權。
