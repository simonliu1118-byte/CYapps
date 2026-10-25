# CYInvoice V3.2：正常折讓啟用與舊資料收尾交接

更新：2026-10-26（Asia/Taipei）。原PR #550／`cyinvoice/feat-v32-de-void-coordination`；工程V3.1.43 Build 0／Cloud Source 0.9.6／schema22；正式Release仍V3.1.0。

使用者已明確指示「折讓應該要啟用了，全面啟用」及「舊資料和入口收尾一起做」。**正常開立／作廢啟用已授權，不需重問啟用同意。** 本對話只完成Source、隔離測試及交接，沒有連線Cloudflare或執行真光貿交易；實際切換由本機Work＋MCP沿原部署owner完成。

## 已完成的基線

- schema20→21與21→22／配套原Worker維護，已由使用者本機完成報告確認。原0022只成功一次、資料／稽核保留，私有備份實際還原及回復證據留在本機。不得修改或重跑0017～0022。
- 使用者另確認Cloud 0.9.5原Worker程式更新已完成。本輪不以尚未取得的完整報告捏造Source、部署ID或即時readback；本機沿既有私有報告核對當前原服務即可，不重做前批作業。
- 使用者已確認公司卡選取、編輯、同步、重啟取卡介面可用。不能延伸宣稱所有正式金鑰或財務交易已驗收。
- 本輪0.9.6修正：申請保存測試／正式環境，核准與恢復均取原申請環境；不再用APP_ENV決定金融帳號。測試用官方公開測試憑證，正式用原公司卡，原卡身分／revision／Claim及授權綁定保留。

## 本機執行範圍

1. 沿原分支核對PR最新完整HEAD、同SHA必要CI及工程版本；使用完全相同Source建置Worker bundle。不得使用舊包、移動的分支名稱或歷史成功CI代替精確SHA。
2. 先估算本批必要metadata／有界D1讀取／健康檢查及部署用量，依原用量規則取得必要的額度決定；前批額度例外不延用。本批啟用本身不需重問同意。實際resource IDs、token及Secret值只在原私有設定內使用，不貼聊天／Git／PR。
3. 只更新原Worker，不新增Worker／D1、不操作CYID D1、不做migration；schema與marker均維持22。保留原不可變Worker版本、設定及本批私有checkpoint供回復；Worker回退不回退D1，不自動整庫還原。
4. 原`cloud/scripts/deployment-preflight.mjs`設定：
   - `CYINVOICE_DEPLOYMENT_MODE=deploy`
   - `CYINVOICE_ALLOWANCE_ACTIVATION=enable`
   - **不設定**`CYINVOICE_SCHEMA_UPGRADE`
   - 沿原本機認證、原Worker／CYID provider選擇及原部署設定。APP_ENV保留，不為測試公司卡改部署環境。
5. 同一Source、同一專用私有checkpoint及相同意圖，依序執行原owner的`before`→`deploy`→`after`。`before`產生原`wrangler.deploy.generated.jsonc`，只在本機使用；用原已審方式產生單一`app.js`，再交原owner部署，禁止另跑wrangler deploy繞過稽核：

   ```text
   node scripts/deployment-preflight.mjs before <private-checkpoint.json>
   node scripts/deployment-preflight.mjs deploy <private-checkpoint.json> <reviewed-bundle/app.js>
   node scripts/deployment-preflight.mjs after <private-checkpoint.json>
   ```

6. 查核原Worker單一版本100%流量、線上bundle雜湊與同Source本機產物一致、health的`cloudVersion=0.9.6`／`storageSchemaVersion=22`／storage及CYID正常。legacy schemaVersion不是實際D1版次。核對原Bindings、Secrets名稱與繼承、Routes、裝置／Workspace身分及固定CYID scope不變；不讀出秘密值。

### 旗標目標

| 旗標 | 本批目標 |
| --- | --- |
| ALLOWANCE_API_ENABLED | true |
| ALLOWANCE_G0401_SEND_ENABLED | true |
| ALLOWANCE_G0501_SEND_ENABLED | true |
| ALLOWANCE_MANUAL_REVIEW_ENABLED | 原存在則false；原未設定則繼續未設定 |
| ALLOWANCE_EXTERNAL_COLLISION_ENABLED | 原存在則false；原未設定則繼續未設定 |

原COMPANY_SETTINGS_ENABLED=true及加密Secret綁定必須存在；原部署owner檢查後才允許啟用。除了上述正常旗標及停用舊人工旗標，其他vars不變。未授權／無效Device及角色拒絕檢查只用無交易內容請求；不拿503退回當作啟用成功，也不透過MCP自動送出真財務交易。

## 同批舊資料與入口收尾

- 舊網站「已人工完成」按鈕與直接結案方法已從Source移除；不另建立待辦入口。舊紀錄仍留原「待辦/處理中」或「上傳問題」，詳情提供官方查證，歷史唯讀保留。
- 舊折讓有原送出marker及官方候選證據，沿原reconcile核對；沒有送出證據則保留原問題，不以相同金額／同號推定成功，也不轉新申請或POST。
- 舊折讓作廢以原號、買方及原發票明細查證；官方明確作廢才結案，保留原申請。已有Claim交原恢復owner只查回，不解鎖或用歷史按鈕繞過。
- 新runtime projection帶原申請環境。本機依精確application ID＋issue/void操作歸入正確測試／正式帳戶；舊錯置投影只結束其重複顯示，原歷史行保留。不得按同張發票合併不同部分折讓。
- **實際Local舊待辦／未知Claim仍未在本對話盤點，不能記0。** 本機關閉程式並依原方式備份原Data後，對原帳戶／既有索引做有界唯讀盤點，區分未核准新草稿、已核准Ledger、舊待確認marker、未知Claim及歷史已結案；只回報遮罩後數量／分類，不貼真實單號或客戶資料。超過預估停止擴大讀取並回報，不能為收尾批次DELETE、回填authority或解鎖Claim。
- 不要求全部Local Claim歸零才能更新Worker或啟用新流程；每筆沿原交易owner及原證據處理。不更動舊發票清理政策。

## 完成後交回

回報精確Source／CI／工程包、原Worker版本與bundle一致性、schema22、三正常旗標與舊人工旗標狀態、原服務／身分保留、本批實際用量與計費歸屬限制、舊資料盤點分類及未解決項。私有備份、checkpoint、完整resource IDs及逐筆收據不提交Public Git／PR。

之後由使用者以工程包及自行設定的公司卡驗收一次核准、自動配號／送出／查回、正常等待／異常分類、斷線重啟、診斷及g0501有限重試。合成碰號與故障測試不能當成真人已測。雙Windows／不同Device真人驗收依既有決議於V3.2上線後補做，不新增上線阻擋條件。未授權合併PR／正式Release；切換完成不等於V3.2全部業務驗收完成。
