# CYInvoice V3.2：schema22維護完成與後續驗收

更新：2026-10-26（Asia/Taipei）。目前工程V3.1.42 Build 4／Cloud Source 0.9.5；最近完成的本機維護仍為V3.1.42／BUILD=0、Cloud 0.9.4；原[PR #550](https://github.com/simonliu1118-byte/CYapps/pull/550)仍為Draft，正式Release仍V3.1.0。

**schema21→22及配套原Worker維護已由使用者本機MCP完成報告確認；遠端操作已停止。** schema維護依使用者報告登記；本輪開發配套Source，未獨立重新查詢或修改Cloudflare。完整備份、回復、資源識別、私有路徑及逐筆計量證據留在使用者私有報告，不貼Public Git／PR。產品驗收以[唯一現行計畫](V3_2_IMPLEMENTATION_PLAN.md)為準。

Build 4的測試票跨機抓回已按使用者指示沿原日期識別碼處理，無新增來源帳本／API／schema23；此修正不依賴Cloud新功能。折讓功能依2026-10-26使用者決定維持關閉，不因畫面修正順帶啟用。

## 下一輪：Build 4／Cloud 0.9.5原Worker程式更新（尚未執行）

- 本輪工程Source為V3.1.42 Build 4／Cloud 0.9.5，沿原PR #550；部署前核對PR完整HEAD、同SHA必要CI及bundle，不能以歷史已部署Source代替。
- 目的只有原`/v1/work-items/list`的Device授權最小待辦摘要，避免開啟清單就詢問管理員密碼。詳情、已封存歷史與全部寫入仍須當次管理員驗證；當前Workspace／Device／Application access在讀取前後均檢查。
- **只更新原Worker程式，D1及marker均維持22。** 不新增Worker／D1、不修改或重跑0020～0022，不更改CYID D1、Bindings、Secrets、Routes或現有旗標。沿原canonical deployment owner做同schema的before／deploy／after，不設定新的schema-upgrade意圖。
- 本機先依原規則估算本批控制平面／健康／必要查核用量並取得該批部署授權，保留原不可變Worker版本及設定回復證據。原schema22維護與私有備份證據沿用歷史，不為程式更新重做DDL或整庫掃描。
- 部署後核對線上bundle與同一Source建置一致、Cloud 0.9.5、storageSchemaVersion=22、history不變及原旗標／服務綁定。正式Release仍V3.1.0，不合併或發布，不呼叫真光貿交易。
- 使用者以新工程包重測：開啟／重新整理清單無密碼窗，待辦詳情才驗管理員；失敗原因可見且驗證窗保留。若WORK_ITEMS_ENABLED原本關閉，保留原值並明示功能未啟用，不自行打開任何旗標。
- 本輪未做遠端操作；已驗收的公司卡選取／編輯／同步／重啟取卡保留，本輪只收斂重複驗證。完整資源識別、部署回復及計量證據仍只留本機私有資料，不貼Public Git／PR。

## 最新完成：schema21→22／V3.1.42配套

- 本機回報採用Source `16f6a6ce03681b157ceec2805657d3a409935221`；原服務升為schema22／Cloud 0.9.4。唯一0022成功套用一次，既有資料與稽核保留；0020／0021不修改或重跑，不重建服務。
- 單公司卡名稱／同步版次及安全退回已具配套結構；公司／金鑰原revision保留，card_revision自原revision初始化。保留歷史核准／Ledger／Claim／Audit，不補造權威或解鎖未知交易。
- 本機回報備份已實際還原、故障回滾／成功升級及前後一致性檢查完成；這是維護證據，不代表真人公司卡同步、憑證可用性或光貿交易已驗收。
- 五個折讓旗標維持關閉。未授權入口拒絕只證明拒絕行為，不代替真人Device同步或正向業務驗收。未執行真光貿交易、合併PR或正式Release。
- 直接SQL計量與共用帳號差額的窗口及歸屬不同，不能當成完整作業計費量；本批完整讀取是否落在核准上限內仍不能獨立證明。不得為補計量拆分而重跑遠端查核。
- 工程包及本機下載雜湊與同一Source的GitHub Artifact一致；使用者可開始公司卡／畫面驗收。本機結構及配套更新已完成，不再列為取卡前的未部署阻塞。

## 本輪工程驗證與測試包

同一Source的[Windows](https://github.com/simonliu1118-byte/CYapps/actions/runs/39920169559)、[Cloud](https://github.com/simonliu1118-byte/CYapps/actions/runs/39920169530)、[Governance](https://github.com/simonliu1118-byte/CYapps/actions/runs/39920169550)、[Development預檢](https://github.com/simonliu1118-byte/CYapps/actions/runs/39920169615)均成功，已由本對話再次核對；Development實際部署job skipped，本次實際維護依使用者本機MCP報告登記。

[V3.1.42工程包](https://github.com/simonliu1118-byte/CYapps/actions/runs/39920169559/artifacts/12487625176)對應上述Source。本次僅改文件，不改程式、VERSION／BUILD或套件，不需重新部署。Source／合成測試及未授權拒絕不代替真人畫面、Device取卡、實際環境或財務驗收。

## 前批維護歷史

## 已完成：原D1／Worker20→21

- 實際部署Source為 `83e7b691fc494186e7a0d373654c888e83874fde`，原分支 `cyinvoice/feat-v32-de-void-coordination`。本機核對正式規則、CYID consumer相容性及指定Source工程CI。
- 原D1 schema21、原Worker marker21；正規history精確0001～0021，0021僅一次。舊資料／稽核、schema與history、FK／quick_check及canonical before／deploy／after均由本機報告驗證通過。
- 原服務與綁定保留，線上bundle與指定Source一致；未新增Worker／D1、未直接讀寫CYID D1，未回填舊authority snapshot或解鎖舊Claim。
- schema20完整加密備份及隔離實際還原、0021失敗回滾／成功升級演練、非空歷史Ledger／Claim合成驗證通過；schema18～20前批私有證據已交回。備份／回復位置與具體識別只保留私有紀錄，未驗的回復情境不推定完成。
- 不重跑0017～0021，不修改已套用migration。Worker回退不會回退D1；不得反向DROP或自動整庫還原，回復仍須先評估新寫入與交易狀態、取得對應決定。

## 財務入口仍關閉

| 旗標 | 本機完成時 |
| --- | --- |
| ALLOWANCE_API_ENABLED | false |
| ALLOWANCE_G0401_SEND_ENABLED | 未設定（關閉） |
| ALLOWANCE_G0501_SEND_ENABLED | 未設定（關閉） |
| ALLOWANCE_MANUAL_REVIEW_ENABLED | 未設定（關閉） |
| ALLOWANCE_EXTERNAL_COLLISION_ENABLED | 未設定（關閉） |

無交易內容的開立／作廢核准入口驗證均為503／ALLOWANCES_NOT_READY。未做真光貿交易，這只證明入口關閉，不代表啟用後業務驗收。

## 前批schema21工程驗證（歷史）

已部署Source的[Windows](https://github.com/simonliu1118-byte/CYapps/actions/runs/39797161719)、[Cloud](https://github.com/simonliu1118-byte/CYapps/actions/runs/39797161481)、[Governance](https://github.com/simonliu1118-byte/CYapps/actions/runs/39797161548)、[Development預檢](https://github.com/simonliu1118-byte/CYapps/actions/runs/39797161732)均成功。Development實際部署job skipped；實際維護由本機MCP執行。

本機另完成TypeScript、部署預檢、migration rehearsal、coordinator及Worker dry-run。本機交回時未改Source／PR；本次文件歸檔不改已部署程式與版本，不需重新部署。

## 前批schema21計量結論與限制（歷史）

本批另獲維護授權，未延用前批額度例外；完整估算、逐筆SQL meta、帳號前後快照、實際讀寫／request量及差異已在原對話完整報告與私有收據中交回，不複製到公開文件。

**直接SQL meta不是完整計費總量，共用帳號差額也不能全部歸因本作業；不能宣告本批完整計費讀取已確認在原5000參考上限內。** 原報告記錄共用差額略高於參考上限，無法精確判定本作業本身是否超額；已停止遠端操作，未提高上限。不能把未歸屬差額全歸因備份，也不能以空業務表推定DDL零計費。

本次文件歸檔無Cloudflare讀寫、部署或交易；與已完成本機維護的實際用量分開。下一階段需另估用量及取得授權，不為補文件重跑遠端查核。

## 下一階段尚待完成

1. 核對測試公司卡、真光貿API環境及可用性；盤點實際Local舊待辦與未知Claim。未驗數量不記0，不把舊全域停送門檻重新作為已完成結構維護的前置條件。
2. API／g0401／g0501適用功能啟用及測試交易需另授權；舊兩個人工旗標繼續關閉，非新流程正常caller。
3. 按唯一計畫驗收一次核准、自動配號／送出／查回、等待／異常分類、斷線重啟、診斷與g0501有限重試；Local／CYID各遵守原交易owner，舊未知Claim只查回，不因新版跨權威重送。
4. 新流程真驗及舊待辦盤點完成後，退役最後舊網站人工入口／caller，歷史唯讀。雙Windows／不同Device真人驗收依原決議於V3.2上線後補做，未冒稱完成。

本機已停下；本報告僅更新狀態，未合併PR、未發布Release，維護授權不延伸到第二階段。
