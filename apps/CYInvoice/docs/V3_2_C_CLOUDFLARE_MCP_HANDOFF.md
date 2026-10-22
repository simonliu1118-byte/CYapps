# CYInvoice V3.2 本機 Work 交接：schema21 與實際驗收

更新：2026-10-22；工程 V3.1.41 Build 1；原分支 `cyinvoice/feat-v32-de-void-coordination`／[PR #550](https://github.com/simonliu1118-byte/CYapps/pull/550)。這是操作交接，不是新規則，也不是本次即已授權部署。產品唯一計畫：[V3_2_IMPLEMENTATION_PLAN.md](V3_2_IMPLEMENTATION_PLAN.md)。

## 已知狀態與不可重做部分

- 使用者本機 Work 截圖回報原 D1 schema20／Worker marker20、100%流量、CYID／Bindings與前後檢查成功；詳細私有計量、備份／實際還原證據仍待補齊。這是本機證據，非本對話獨立即時readback。
- **不要重跑0017～0020，不要修改0020，不新建Worker／D1，不碰CYID D1。** 本批只新增0021 Source；未改正式Worker、財務旗標或Release。正式Release仍V3.1.0。
- 先核對原PR最新完整SHA及該SHA的Windows Build、Cloud Check、Governance全部成功；未通過不部署。不要拿舊`3f9259f5`當新功能已完成的Source。

## 第一階段：補證據與20→21結構／Worker維護

1. 讀正式三層規則、CYID canonical consumer standard及唯一現行計畫；共通規則版本比對AITeam main。唯讀確認原Account／Worker／D1身分、原Worker版本／Bindings／Secrets、精確0001～0020 history及marker20。已是21則核對來源後停止，不重套。
2. 補歸檔schema20前後私有證據與完整還原測試。正式營運資料／ID／備份不貼Public Git、PR、CI或對話；只回報遮罩摘要與可定位的私有存放處。
3. **執行前重估及取得本階段具體授權**：原批5,000 D1 read／500 write／30 Worker requests預算只作上限參考，不自動增加或延續。先確認共用帳號當日餘額，按主鍵有界盤點受影響表（每表最多101 opaque IDs、超100即停重估），估算新欄位／索引對現有N列的成本；包含備份、index建立及前後驗證。帳號餘額未知／限流／告警立即停，不重試繞過。
4. DDL前保存原immutable Worker version／設定、schema20 Time Travel bookmark及完整加密備份；在隔離本機庫實際還原，核對既有申請、核准、sending Ledger、Claim、Audit與FK／integrity。不能用只有bookmark或匯出成功代替還原驗證。
5. 唯一遷移為 `cloud/migrations/0021_allowance_automatic_execution.sql`；只在精確schema20按正規history套一次。保留歷史authority_snapshot=NULL，不補造舊授權／不解鎖舊未知Claim；舊資料不可刪除或回寫。若失去回覆先唯讀查history／schema，不盲目再套。
6. DB後驗精確0001～0021、原資料／稽核指紋、結構／索引／觸發器、FK／integrity。通過才沿原部署owner使用 `CYINVOICE_SCHEMA_UPGRADE=20:21`，before／deploy／after同一source與私有checkpoint，更新原Worker marker21。禁止`deploy:with-migrations`、跳版、重用checkpoint或順手更改Bindings／Secrets／流量配置。
7. 維護期間全部折讓旗標缺省或false：`ALLOWANCE_API_ENABLED`、`ALLOWANCE_G0401_SEND_ENABLED`、`ALLOWANCE_G0501_SEND_ENABLED`及舊`ALLOWANCE_MANUAL_REVIEW_ENABLED`／`ALLOWANCE_EXTERNAL_COLLISION_ENABLED`。舊兩旗標無正常caller，保留關閉供相容盤點；不得趁結構維護啟用任何財務送出。
8. 回報精確SHA、schema／marker／Worker version、五旗標關閉、原CYID／Bindings保留、備份／實際還原證據、實際SQL meta及帳號讀寫／request差額、剩餘量與預估差異。不補造數字。

失敗時停止。Worker可按已保存immutable version回退；D1不隨Worker回退。不得反向DROP或自動整庫還原，先評估期間新寫入，再由使用者決定回復。

## 第二階段：另外授權的功能啟用／真光貿驗收

此階段不能因schema21成功自動開始。先確認測試公司卡、API環境與交易owner。Local Identity／LocalOnly才執行本機送出；CYID申請由Cloud執行，不以歷史設定全面封死Local，也不允許同一操作跨權威重送。

逐項驗收主管完整詳情→只按一次核准→系統配號／送出／查回，包含正常成功、明確佔號自動+1、未知結果原號查回、斷線／重啟恢復、官方等待進處理中、真異常進上傳問題及匯出可查證診斷。g0501須確認已作廢結案、正常等待不重送、同號最多3次／至少60秒及舊Claim只查回。Cloud背景由既有runtime同步驅動，所有客戶端離線期間不會自行啟動新POST。

真流程驗收及舊待辦盤點完成後，移除最後歷史網站人工入口／直接caller，保留歷史唯讀。雙Windows／不同Device真人驗收依原決議於V3.2上線後補做；未實測標未驗證。正式Release、合併、任何真實客戶財務交易均須對應授權，不由工程CI推定。

## 過往維護紀錄（只保留事實，不再列舊執行清單）

- 2026-10-21本機回報schema16→18／marker18、CYID與Bindings保留、API flag=false，read +1711／write +55／Worker request +12；私有備份還原曾核對。這是使用者回報，不是本對話獨立驗證。
- 2026-10-22本機截圖回報18→19→20已完成；完整計量與備份明細待歸檔。舊步驟及原授權記錄由Git歷史保留，不可照舊清單再執行。
- 本批GitHub／工程回合未呼叫Cloudflare：D1讀取0、寫入0、Worker部署0、真光貿交易0；本機後續實際計量另報。
