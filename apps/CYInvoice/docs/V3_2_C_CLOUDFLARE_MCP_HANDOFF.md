# CYInvoice V3.2：Cloud 0.9.6啟用與限定舊案盤點完成

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

## 前批0.9.5證據補齊

使用者已交回2026-10-26完整完成報告：Source `cff89085adb72536437ba2edc88b3d21d056de5a`／V3.1.42 Build 3，原Worker 0.9.4→0.9.5完成；schema22、原服務／綁定及旗標保留，當時折讓仍關閉，無真交易。這是本批0.9.6前的歷史基線，無需重做。

## 剩餘真人驗收

以已下載V3.1.43工程包及使用者自行設定的公司卡，驗收真實金鑰可用性、角色正向操作、主管一次核准後自動配號／送出／查回、正常等待／異常分類、斷線重啟、診斷與g0501有限重試。不能以旗標、無憑證拒絕或合成測試代替真人財務結果。

公司卡選取／編輯／同步／重啟取卡的既有使用者驗收保留。雙Windows／不同Device真人驗收依既定決議於V3.2上線後補做，不新增上線阻擋條件。V3.2尚未全部業務驗收完成；PR未合併、正式Release仍需使用者當次授權。
