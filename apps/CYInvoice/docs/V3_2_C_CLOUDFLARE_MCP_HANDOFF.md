# CYInvoice V3.2：schema21維護完成與後續交接

更新：2026-10-23（Asia/Taipei）；本機維護作業日2026-10-22。工程V3.1.41 Build 1，原[PR #550](https://github.com/simonliu1118-byte/CYapps/pull/550)，正式Release仍V3.1.0。

**第一階段已由使用者完整本機MCP報告確認完成，遠端操作已停止；第二階段尚未開始。** 本檔只歸檔可公開的狀態摘要；完整部署、備份、計量及回復細節留在使用者私有證據與原對話，不放入Public Git。本對話未重新連線Cloudflare驗證或執行維護。產品流程以[唯一現行計畫](V3_2_IMPLEMENTATION_PLAN.md)為準。

## 本輪開發交回：Build 2／schema22尚未部署

目前工程Source為V3.1.41 Build 2；下方schema21完成證據只對已部署Source `83e7b691` 有效，不代表本輪修正已上線。逐項收斂與公司卡綁定保留見[唯一現行計畫第5節](V3_2_IMPLEMENTATION_PLAN.md)。

新增唯一forward Source `0022_allowance_unsent_withdrawal.sql`，僅讓原申請在當次主管驗權、確定未送出及CAS保護下退回。沒有新增表、不解鎖舊Claim、不修改已套0020／0021；schema21下新退回入口安全拒絕。原部署owner已支援21:22預檢／意圖，**未執行遠端migration或Worker部署**。原五旗標關閉與前次停點不變；實際21→22維護仍需當次授權及備份／回復／用量證據，不能直接重跑前批。

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

## 工程驗證

已部署Source的[Windows](https://github.com/simonliu1118-byte/CYapps/actions/runs/39797161719)、[Cloud](https://github.com/simonliu1118-byte/CYapps/actions/runs/39797161481)、[Governance](https://github.com/simonliu1118-byte/CYapps/actions/runs/39797161548)、[Development預檢](https://github.com/simonliu1118-byte/CYapps/actions/runs/39797161732)均成功。Development實際部署job skipped；實際維護由本機MCP執行。

本機另完成TypeScript、部署預檢、migration rehearsal、coordinator及Worker dry-run。本機交回時未改Source／PR；本次文件歸檔不改已部署程式與版本，不需重新部署。

## 計量結論與限制

本批另獲維護授權，未延用前批額度例外；完整估算、逐筆SQL meta、帳號前後快照、實際讀寫／request量及差異已在原對話完整報告與私有收據中交回，不複製到公開文件。

**直接SQL meta不是完整計費總量，共用帳號差額也不能全部歸因本作業；不能宣告本批完整計費讀取已確認在原5000參考上限內。** 原報告記錄共用差額略高於參考上限，無法精確判定本作業本身是否超額；已停止遠端操作，未提高上限。不能把未歸屬差額全歸因備份，也不能以空業務表推定DDL零計費。

本次文件歸檔無Cloudflare讀寫、部署或交易；與已完成本機維護的實際用量分開。下一階段需另估用量及取得授權，不為補文件重跑遠端查核。

## 下一階段尚待完成

1. 核對測試公司卡、真光貿API環境及可用性；盤點實際Local舊待辦與未知Claim。未驗數量不記0，不把舊全域停送門檻重新作為已完成結構維護的前置條件。
2. API／g0401／g0501適用功能啟用及測試交易需另授權；舊兩個人工旗標繼續關閉，非新流程正常caller。
3. 按唯一計畫驗收一次核准、自動配號／送出／查回、等待／異常分類、斷線重啟、診斷與g0501有限重試；Local／CYID各遵守原交易owner，舊未知Claim只查回，不因新版跨權威重送。
4. 新流程真驗及舊待辦盤點完成後，退役最後舊網站人工入口／caller，歷史唯讀。雙Windows／不同Device真人驗收依原決議於V3.2上線後補做，未冒稱完成。

本機已停下；本報告僅更新狀態，未合併PR、未發布Release，維護授權不延伸到第二階段。
