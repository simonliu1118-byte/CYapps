# CYInvoice Windows 候選版驗證清單

本清單供現行 C#／WinForms 工程測試包與正式 Release 前驗收使用。所有會實際改動光貿資料的測試先使用光貿測試環境；只有使用者明確指定時才切換正式公司環境。Cloud lifecycle 測試只使用 development Cloud 與可安全回復的測試 Device／Workspace。

- 目前工程測試基準：**V2.6.16**；CYID source 未部署，精確 head Windows 工程包另記交接。
- Development Cloud：**0.8.9 / API 1 / compatibility marker 8 / storage Schema 12**；10/10 staged Run #8 attempt 2 已驗證新 capabilities，實機操作仍待驗收。
- 前置 V2.6.14 Windows 測試包：PR #216 最新精確 head 通過 CI 的 `V2.6.14` engineering Artifact；Windows #258／#260 為先前版本證據；Run343 僅保留為歷史記錄，舊 Artifact 已到期。
- 最新公開正式 Release：**V2.4.2**。
- V2.6.11 功能 commit 的 Governance #1137、Cloud #369、Windows #258 全部通過；9/29 development staged deployment 為既有歷史證據，本次未重查 live。PR #216 尚未合併，本清單專注於 CI 無法取代的實機、光貿及跨 Device 互動驗證。

## A. 全新啟動、超級管理員與設定

1. 將候選版解壓縮到全新資料夾，確認沒有既有 `Data`。
2. 啟動 CYInvoice.exe，先驗收首次模式分流；選單機版後應顯示「首次設定」，直接加入雲端另依 Q／invitation 驗收。
3. 視窗應顯示「首次開啟程式需設定超級管理員，超級管理員無法變更。」
4. 首次設定欄位依序為：員工編號、姓名、Email、員工密碼、再次輸入密碼；不應要求 App Key 或 MO店+ Excel 密碼。
5. 員工編號不是 4 碼數字、姓名／Email 空白、密碼少於 8 碼、含非 ASCII 英數字、兩次密碼不同時都應阻擋。
6. Enter 應依欄位順序前進；最後一欄 Enter 才執行建立。
7. 建立成功後應顯示一次性超級管理員復原碼；關閉後不得再次從本機明文讀回舊復原碼。
8. 關閉並重啟後不得再次進入首次設定。
9. `settings.json` 與 SQLite 不得保存員工密碼、復原碼、App Key 或 MO店+ 密碼明文。
10. 設定選單與帳號管理進入時都使用同一套「權限驗證」視窗。

## B. 帳號管理與權限

1. 超級管理員可建立一般使用者與管理員；新增視窗顯示「新增使用者」。
2. 一般角色 UI 顯示為「一般使用者」，正式內部 role 必須為 `USER`；active runtime 不得再把 `EMPLOYEE` 當作 role 或相容 alias。
3. 新密碼至少 8 碼且只能使用 ASCII 英文字母／數字。
4. 管理員不得修改、停用、刪除或重設超級管理員。
5. 管理員不得取消自己的管理員權限、停用或刪除自己。
6. 超級管理員不得被降級、停用或刪除。
7. 超級管理員復原碼使用成功後舊碼應立即失效並產生新碼。
8. 主畫面與子視窗文字應統一為「帳號管理」，不得殘留「帳戶管理」。

## C. MO店+ 密碼與匯入入口

1. 全新設定完成但尚未設定 MO店+ Excel 密碼時，按「MO店+」應先提示至設定頁完成密碼設定。
2. 上述情況不得先開啟檔案選擇器。
3. 設定 MO 密碼後，可正常選擇受密碼保護 `.xls` 或一般 `.xlsx`。
4. 受密碼保護 `.xls` 仍由 Excel COM 唯讀開啟，不建立中介 Excel，也不得將密碼寫入 LOG。

## D. 手動開立與買方名稱

1. 自動訂單編號格式為 `MYYYYMMDDXXX`。
2. 一般消費者固定含稅；公司統編可切換含稅／未稅。
3. 完整 8 碼統編輸入後自動查詢；API 技術失敗不得顯示為單純「查無名稱」。
4. 人工名稱與 API 名稱不同時顯示提示；`↻` 強制重查不得撐大或裁切欄位。
5. 快速連續輸入不同統編時，舊查詢結果不得覆蓋新輸入。
6. 固定精度：含稅 `200` → 未稅 `190.4761905` → 含稅 `200`。
7. 商品 Enter 流程：品名 → 數量 → 單價；單價 Enter 後有下一列就移到下一列品名，否則新增一列。
8. 同來源＋原始訂單已有不可重開狀態時必須阻擋；結果不明不得盲目重送。

## E. 測試環境 OrderID namespace 與去識別化

1. 測試環境 API OrderID 使用 CYInvoice technical namespace。
2. 一般 UI 仍顯示原始可讀 OrderID。
3. 開立後 discovery／`invoice_query` 使用相同 namespace 可正確找回。
4. 不得誤認共享測試池其他公司／執行個體資料。
5. 正式環境不得套用測試 namespace。
6. 測試環境可識別資料仍依現行規則去識別化。

## F. Excel 匯入

### F1. MO店+

1. 依欄名辨識，不依固定欄位位置。
2. 以既有樣本核對官方開票金額，不能用平台淨入帳取代。
3. 公司訂單總額拆稅後總額不得改變。
4. 取消、未勾選、失敗或結果不明時不得送出或保存不該保存的人工名稱。

### F2. 酷澎

1. 已驗證 DeliveryList 可依欄名解析並進入共用確認視窗。
2. 只使用「應開立予買家之發票金額」。
3. 尚未取得可靠樣本的格式必須安全停止，不猜測。

### F3. 鼎新 ERP

1. 必須找到「單頭資料」及「單身資料」，一個活頁簿只允許一張銷貨單。
2. 單頭必要欄位與單身必要欄位缺少時停止。
3. 單價由金額 ÷ 數量推導；單身金額合計必須等於單頭本幣合計。
4. 公司統編／名稱修改後必須先按「套用資料」。
5. 鼎新一律紙本。

## G. SQLite、同步與 retention

1. 以舊版 `Data` 備份升級，確認 SQLite 遷移成功且舊 JSON 保留。
2. 損壞舊 JSON、SQLite 或 schema 時應停止，不得建立空 DB 覆蓋。
3. 啟動、每 5 分鐘及手動重新整理共用最近 3 天同步核心。
4. 手動重新整理維持 30 秒冷卻。
5. 每個本機日第一次正式環境同步涵蓋目前兩月期別＋上一期。
6. 同步不得呼叫開票 API 重送發票。
7. 多筆本機候選時建立／保留問題，不猜測覆寫。
8. 正式環境發票 Cache 保留目前及上一期；測試環境只保留當日。
9. 未解決作廢／折讓 pending 在有效追蹤期間不得被一般 retention 提前清除。
10. 超過兩期的可結案作業，由管理員手動結案後才恢復一般 retention 清理資格。

## H. 發票作廢

### H1. 直接作廢

1. 使用已開立且可作廢的測試發票。
2. 輸入作廢原因，再於確認視窗輸入發票號碼、員工編號與密碼。
3. 證明聯狀態區塊應位於員工驗證欄位下方，三個選項一列一顆，不得裁切。
4. 背景已開立清單在確認視窗開啟期間，所有可見發票號碼都應保持空白，即使背景重新整理或重建列也不能重新出現。
5. 不需管理員覆核時，實際送出的 `CancelReason` 應為 `使用者編號-原因`，例如 `3015-消退`。
6. 光貿確認成功後詳細資訊應重新 query 並更新為最新狀態。
7. 若光貿仍是等待確認，CYInvoice 不得自動重送。

### H2. 紙本未收回人工覆核

1. 紙本證明聯選「尚未收回」時，不得直接呼叫作廢 API；應建立「紙本作廢確認」待辦。
2. 一般使用者不能在待辦中批准或取消。
3. 管理員雙擊待辦後可「確認送出作廢」或「取消退回」。
4. 管理員批准後 `CancelReason` 應為 `管理員編號-使用者編號-原因`，例如 `3001-3015-消退`。
5. 取消退回不得呼叫光貿作廢 API，發票維持原官方狀態。
6. 實際測試保留完整 `invoice_query` 回覆，確認光貿是否提供可回查的 CancelReason／作廢原因；目前不得假設未文件化欄位一定存在。

## I. 折讓人工流程

1. 在可折讓發票詳細資訊按「折讓」。
2. 輸入原因、含稅折讓總額、員工編號及密碼；錯誤帳密不得建立待辦。
3. 成功後顯示「折讓申請已建立。請通知管理員查看並完成操作。」之現行使用者提示。
4. 「上傳問題」應出現折讓人工處理待辦。
5. 管理員在光貿網站完成人工折讓後，於待辦詳細頁按「已人工處理」；CYInvoice 應透過 `invoice_query.allowance[]` 比對申請前基線與新折讓。
6. 唯一新折讓且含稅金額吻合時可確認完成。
7. 多筆新候選、金額不符或仍在處理中時不得猜測結案，待辦必須保留。
8. 已進入等待官方確認階段後不得「取消退回」。
9. 完成後發票詳細資訊的「作廢 / 折讓紀錄」應顯示折讓摘要；pending 不應出現在歷史區。

## J. 折讓 PDF

1. 準備至少一張光貿已完成折讓單。
2. 雙擊折讓歷史開啟詳細資訊，按「檢視 PDF」。
3. 應顯示三個版型：A4、A4 (地址+A5)、A5；不得顯示發票專用其他兩種版型。
4. 不應預先選定版型；使用者選擇後才呼叫 `/json/allowance_file`。
5. 三種版型各實測一次，確認光貿回傳可正常在 WebView2 PDF Viewer 開啟。
6. 同一折讓單與版型重開時可使用有效 `Cache/AllowancePDF`。
7. 非 PDF、異常網址、錯誤 API 回覆不得被當成成功檔案保存。
8. PDF Viewer 關閉後主程式仍可正常操作。

## K. 折讓作廢人工流程

1. 在狀態已完成的折讓詳細資訊按「折讓作廢」。
2. 輸入原因、員工編號與密碼；錯誤帳密不得建立待辦。
3. 建立前 CYInvoice 應重新 query 發票，確認該折讓單仍唯一存在且官方狀態已完成。
4. 成功後「上傳問題」出現「折讓作廢人工處理」。
5. 本版不得直接呼叫 `/json/g0501`。
6. 管理員在光貿網站人工完成後可在詳細待辦按「已人工處理」。
7. 尚未人工處理前可「取消退回」；兩者都只更新本機待辦，不得假裝呼叫光貿成功。
8. 同一張發票已有另一張折讓單的作廢申請時，應阻擋建立第二筆互相覆蓋的待辦。

## L. 上傳問題單一清單

1. 主視窗「上傳問題」應只有一張清單，不再分上下兩張。
2. 技術問題、開立失敗、紙本作廢確認、折讓人工處理、折讓作廢人工處理都在同一表中。
3. 只有開立失敗列可勾選；其他列不得被 checkbox 當作刪除目標。
4. `清除開立失敗紀錄` 只刪除本機 Failed 紀錄，不影響其他 issue／pending，也不影響光貿資料。
5. 人工待辦雙擊開詳細視窗；操作按鈕位於詳細頁，不堆在主清單工具列。
6. 一般技術 issue 可依既有流程標記已解決；作廢／折讓人工流程不得被一般標記繞過。
7. 清單重新整理、縮放與水平捲動時不得持續閃爍。

## M. 超過兩期管理員結案

1. 準備超過目前＋上一個兩月期別的作廢或折讓 pending 測試資料。
2. 一般使用者不能結案。
3. 管理員只有在詳細待辦中看到「管理員結案」。
4. 結案後清除相應本機 pending／人工 metadata，並解決 issue。
5. 管理員結案不得呼叫光貿 API。
6. 作廢 pending 不得因本機結案被偽造成官方 `Voided`；應保持不確定／歷史語意。

## N. 發票 PDF、列印與會員載具

1. 紙本詳細資訊可取得官方 A4 第一頁預覽。
2. 一般消費者檢視 PDF 直接使用 A4；公司統編先顯示五個版型卡片。
3. 發票 PDF Viewer 使用 Edge 原生工具列，不加入第二套自製縮放工具列。
4. 直接列印不得修改 Windows 全域預設印表機。
5. 會員載具不得呼叫紙本 `invoice_file`，並清楚標示模擬預覽非正式憑證。

## O. UI 與穩定性

1. 主視窗最大化／最小化／還原後不得重疊或消失。
2. 設定選單開啟後持續觀察至少 30 秒，不得出現連續閃爍。
3. 首次設定、權限驗證、作廢原因、作廢確認、人工待辦詳細視窗都不得出現不必要 title-bar icon。
4. 危險動作按鈕採一致圓角外觀與較淡紅色，不回到方角高飽和舊樣式。
5. 使用者可見名稱統一：「帳號管理」、「新增使用者」、「一般使用者」、「權限驗證」。

## P. 發行包

1. 根目錄包含 `CYInvoice.exe`、當版唯一 `V版本號.txt` 及 `使用說明.txt`。
2. 不得包含 `Version` 資料夾、`todo.txt`、執行期 Data／Cache／Logs。
3. engineering Artifact 明確標示 `Channel: engineering`。
4. WebView2 managed DLL 集中於 `Runtime/WebView2`。
5. engineering/public artifact 上傳前必須通過 repository `scan-public-package.py`，不得包含 secrets、production bindings 或被治理規則禁止的檔案。
6. 正式 Release 必須由 `main` 重新執行全部必要驗證；engineering CI 成功不能直接視為正式發布完成。

## Q. Built-in Cloud 現行候選版 baseline

> 先做本節，再進行 R～U。既有 A/B 兩台都是目前 Workspace 的 recovery path，不應一開始就做破壞性動作。

1. A、B 均使用目前 V2.6.14 通過 CI 的工程包啟動；來源／digest 見 CLOUD_WORK_HANDOFF.md，不使用已到期 Run343 Artifact。
2. 兩台都應能連上 development Cloud，既有 Workspace 不應被重新 bootstrap。
3. `裝置管理` 應看到既有 active Device inventory；不應因升級產生重複 Device。
4. 既有中央 Employees 應仍可登入，角色為 `SUPER_ADMIN / ADMIN / USER` vocabulary，不得出現 active `EMPLOYEE` role。
5. 一般發票清單、recent sync、手動重新整理及既有 Cloud status 顯示應無回歸。
6. 若任一台 baseline 不正常，停止後續 destructive lifecycle 測試；先保存 Logs 並修復，不得用重建 Workspace 規避問題。

## R. Cloud authority freshness A/B

> 本節驗證 Package 2。只做可回復的中央 Employee 變更；不要用目前唯一 SUPER_ADMIN 做停用實驗。

1. 在 A 對測試用中央 Employee 做一項可回復變更，例如密碼、`USER ↔ ADMIN` 或 enabled 狀態。
2. 不等待 5 分鐘、不重新啟動 B，立即在 B 觸發需要 Employee authentication/authorization 的 protected operation。
3. B 必須使用最新 Cloud authority；舊密碼、舊角色或舊 enabled 狀態不得繼續被 online cache 認為有效。
4. B 成功取得新 authority 後，本機 protected cache 應被最新 snapshot 取代。
5. 將 B 暫時置於真正無法連線到 Cloud 的狀態；最後可信 protected cache 可供允許的 Offline authentication 使用，不得 fallback 到舊 Local EmployeeStore。
6. 恢復 B 網路後，下一個 protected operation 必須重新以 Online authority 為準，不必重啟程式。
7. HTTP authorization failure、revoked Device、malformed authority 或 Workspace mismatch 不得被當成「離線」而使用 cache 繞過。
8. 測試完成後把中央 Employee 恢復原狀，A/B 都應立即看到恢復後 authority。

## S. Device revoke / retire A/B/C

> 本節驗證 Package 3。優先建立可拋棄的 Device C，不直接拿 A 或 B 當 revoke 目標。

1. 以既有已驗證的 pairing 流程建立新的測試 Device C。
2. A/B 的 `裝置管理` 應看到 C 為 active，且 active count 與清單一致。
3. 從 A 針對 C 執行遠端撤銷；必須要求 execution-time `SUPER_ADMIN` credential 驗證與 destructive confirmation。
4. 撤銷成功後 C row 必須保留為 revoked history，不得 hard-delete。
5. C 的舊 Device Token 從此不得通過一般 Device-authenticated Cloud API。
6. 重複 revoke 同一個已 revoked C 應保持 idempotent，不產生第二個 active identity。
7. 同一台實體測試電腦若重新加入，必須建立新的 Device ID / Device Token；不得復活舊 revoked token。
8. A、B 的身份、Workspace、中央 Employee authority 不得受 C revoke 影響。

## T. Cloud → Local destructive reset

> 本節驗證 Package 4。使用可拋棄的 C installation；A/B 必須保持 active，確保 Workspace 仍有 recovery path。

1. C 處於 Built-in Cloud 且是 active Device，A/B 仍在線。
2. 在 C 選擇回到 Local／destructive reset，應看到兩次明確確認；不能只切一個 mode flag。
3. 確認後 App 應先關閉主 UI／背景同步，再執行 current Device revoke 與 reset transaction。
4. Cloud 明確確認 C 已 revoked 後，才允許刪除 C 的 CYInvoice `Data` / `Cache` / Local Employee / Cloud Employee cache / Cloud identity/token/pending state。
5. 與 CYInvoice local state 無關的診斷／外部檔案不得被過度刪除。
6. 重啟 C 應回到 first-run，不應殘留舊 Workspace、Device Token 或舊 Local SUPER_ADMIN authority。
7. 重新選 Local 時需建立新的 Local `SUPER_ADMIN`。
8. A/B、中央 Workspace、其他 Devices、中央 Employees 與 audit history 必須保持完整。
9. C 之後若再次加入原 Workspace，應視為 fresh Device identity。

## U. Final Device、Workspace inactive 與 ambiguous-result 安全邊界

1. **不要在目前 A/B recovery topology 上故意測最後一台 Device。** Final-Device destructive acceptance 應另建可拋棄 Workspace／環境。
2. Built-in Cloud active Workspace 只剩最後一台 active Device 時，revoke 必須回 `LAST_ACTIVE_DEVICE`，本機不得 wipe。
3. 若要保留 Workspace，先加入另一台 active Device 才可退出目前 Device。
4. 若 Workspace 已由外部中央管理面手動 disable/archive，Windows Client 只能讀取並確認 inactive；不得提供 disable/delete/purge Workspace 的按鈕或 API 權限。
5. Workspace 已確認 inactive 時，最後一台本機可以完成 local destructive reset；不得因此刪除中央 Workspace history／Employees／Audit。
6. 模擬「revoke request 可能已到 Cloud，但 success response 遺失」時，本機必須保留 Data、Token 與 reset marker，不得猜測成功後 wipe。
7. 下一次啟動應透過 narrow `/v1/devices/self-status` 確認 Device terminal state / Workspace state；該 endpoint 不得讓 revoked token 恢復一般 API authorization。
8. 若 self-status 證明 Device revoked 或 Workspace inactive，可繼續已授權 wipe；若仍 active，取消未完成 reset 並完整保留資料；若狀態仍不明，fail closed 並停止進入正常 App。
9. CY ID 尚未成為正式 recovery authority 前，不得因「未來可能支援 0 Device」而放寬 Built-in `LAST_ACTIVE_DEVICE`。

## 通過條件

- 本次變更涉及的 A～U、W、X 與 Y 項目依實際 scope 通過，且 Logs 沒有未處理 `ERROR` 或未處理例外。
- 所有需要光貿實際回覆的結果均以真實測試回覆為準，不以 UI 顯示自行推定 API 行為。
- Cloud lifecycle 測試必須以 development Cloud 與可回復／可拋棄 Device 為主，不為驗證 destructive path 而破壞唯一 recovery path。
- 發票成功判定、防重、環境隔離、Cloud authority、Device trust、結果不明禁止重送／誤刪等安全不變量不得因 UI 或人工流程調整而改變。
- **目前 V2.6.14 仍為工程版；CI／實機驗收、merge 到 main 或 development Cloud deploy 均不等於正式 Release。**


## W. V2.6.11 Build 2 上傳問題／處理中分流

優先驗收本節；通過結果填入 TODO.md。此處為操作清單，不代表已有人工作業證據。

1. 確認「處理中」位於「上傳問題」旁，數量與清單一致；開啟處理中不改上傳問題已讀狀態。
2. 官方上傳狀態 1／2／3／31／32 顯示處理中；99 完成不再列出，91／未知狀態不得冒充正常等待。
3. 正常等待作廢及已人工操作、正常等待光貿確認的折讓移至處理中；雙擊沿用既有明細。需要管理員確認折讓候選、金額不符、回查失敗仍在上傳問題。
4. 背景同步或主清單重新整理完成後，重新整理兩個清單，確認完成項目移除，錯誤項目歸回上傳問題，沒有重複送出 API。
5. 處理中不得勾選刪除或「標記已解決」；等待中的作廢／折讓超過保留期仍可經管理員驗證結案，只停止本機追蹤、不宣稱官方成功。
6. 正式／測試環境與不同公司資料不得混入。舊折讓待確認資料沒有新分類欄位時，先保留上傳問題，正常同步回查後才重新分類。
7. 使用舊資料回歸：沒有 active upload issue 的正常作廢等待，超過兩期仍能由管理員結案；一般 QueryFailed 不得藉結案清除其他業務 pending。
8. 作廢確認期間發票文字／預覽維持隱藏，黑色號碼遮罩不再出現；已開立(等待作廢) 為半形括號。折讓「已人工處理」與作廢採同一 Danger button 外觀。
9. 先以 96 DPI 檢查按鈕並排／明細不裁切；125%／150% 保持既定延後項目，不宣稱已通過。

## X. V2.6.12 首次說明與邀請加入回歸

10/09 使用者已回報舊包第一次空物件錯誤、重試 Invitation is unavailable、關閉再開成功。這只確認舊版復原可用，不能勾選修正版首次完成。

1. 以可拋棄的全新測試安裝檢查首次說明：單機／直接加入雲端為左對齊的兩點，於完整語句折行，96 DPI 不裁切。
2. 使用新核發的測試邀請及超管帳密，下一步核對 Workspace、確認加入一次，應同步中央帳號並顯示完成，不需要關閉再開。
3. A 機回查應只有一筆新增 Device；B 重開維持相同 Device identity，不能重建第二筆。
4. 回歸使用配對碼加入，仍可首次完成且不建立本機帳號。
5. 返回修改加入資料後，必須重新按下一步核對，舊 preview 不可授權修改後輸入。
6. 若 claim 已成功但後續 authority／snapshot 中斷，保留原 Pending Device Token；重開先找回相同 Device，不重送已用邀請。
7. Workspace／Device／authority／snapshot 不一致仍拒絕完成，不能因避免空物件錯誤而略過驗證。

## Y. V2.6.13 按鈕、清單高度與版本

使用精確 head CI 通過的目前 V2.6.14 工程包驗收保留的介面，記錄包／環境／結果。

1. 96 DPI 巡查主畫面工具列、開票／匯入／商品刪除、發票／折讓／PDF 版型、設定／首次設定、帳號與所有雲端視窗；全部 Button 為共通小圓角。確認 hover／pressed／disabled、Tab 焦點、Enter／Escape、圖像及品牌／Danger 色彩正常，無文字裁切。125／150 DPI、High Contrast 另待實機結果，不由 CI 宣稱完成。
2. 「處理中」及「上傳問題」以 0、1、4、20 筆驗收，零筆顯示「無資料」、一筆只有一列，超過十二列或工作區限制出現捲軸；重新整理由多變少可縮回。雙擊明細與既有權限不變，處理中禁止勾選刪除。
3. 紙本／公司／載具發票詳細資訊，作廢／折讓紀錄空、1、5、8 筆均固定五列容量；8 筆可捲動讀完，上方資訊區得到剩餘空間，長內容可完整查看，PDF 預覽與操作未重疊。
4. V2.6.13 的加入時快照已由 V2.6.14「使用版本」取代；依 Z 驗收，不再要求升級後保留加入時版本。
5. 回歸 RC W／X 的分流、行政結案及首次邀請／配對完成；本次不以舊包證據取代新版驗收。


## Z. V2.6.14 裝置管理與啟動回報

使用 PR 精確 head 全部 CI 通過的 V2.6.14 工程包；先核對 development Cloud 0.8.9／storage 12／device-usage-v1／device-rename-v1。

1. 主窗顯示「目前有 N 台使用中的裝置」、原撤銷說明、四欄清單；無 tabs／狀態／撤銷時間，撤銷為 Danger，最後一台仍禁用。數量與 active 列數一致，revoked 不在 Windows 清單。
2. 新增裝置按鈕開獨立視窗；六個 label 完整顯示，欄位左緣一致、垂直置中，複製網址／OTP／配對／邀請仍能操作。關閉重開可恢復 pending／joined ticket；成功後返回主窗重新整理。
3. A、B 分別升級與啟動，成功連線後主窗顯示各自 VERSION／BUILD 與各自最後使用時間；B 啟動不改 A 版本。server UTC 在 Windows 以本機時區顯示；不要將此欄解讀為持續 heartbeat。
4. B 離線啟動不阻擋主窗／開票既有離線流程，不虛構最後使用；之後連線重啟才更新。舊版未回報仍保留較舊版本，沒有最後使用值留白。
5. 超管可更改 A 或 B 名稱，空白／過長／換行、錯誤超管、非超管或已撤銷裝置拒絕；改名不變 Device／Token／Employee identity，另機重新整理可見。名稱與 audit 同交易保存。
6. 使用可拋棄 C 撤銷，清單消失但 Cloud DB 的 Device row／revoked_at 與 audit 仍在；不得為驗收刪除歷史。保留 A/B 及最後一台 recovery path。

自動化不能代替實機 96 DPI 驗收。125／150 DPI、High Contrast 尚待證據，不自動升為本輪阻塞。

## AA. V2.6.15 CYID Consumer 實機驗收（尚未執行）

先安排隔離 staging，確認 registered Application／Workspace enablement／App Access、private IDENTITY binding、兩 Workspace 配對、0013 backup/FK；記錄 PR 精確 head、工程包 hash 和環境，不把上一版包或 local provider fixture 當 live 證據。原 Local／Built-in A/B/C 與業務驗收保留。

- [ ] Local／Built-in 原流程及舊 discovery-404 Worker 不受影響；已確認 CYID 的 client 不自動降級。
- [ ] 原 A/B/C 安裝先升級到 CYID-capable Windows，備妥各員工啟用／App Access／Role／verified 超管 Email，再受控啟用。前後核對同一 CYInvoice Workspace、原 Device／Token、公司／印表機／protected secrets、發票／pending／買方／PDF；重開可繼續原工作，不重新加入／建立 Workspace／reset，錯綁定拒絕且保留原設定。
- [ ] CYID invitation／pairing 首次加入成功，pending token 中斷／重開仍只 claim 一次；不讀 Built-in Employee snapshot，不建立 Local 員工。
- [ ] Account Management 隱藏且上方按鈕排列正常；settings 顯示 CYID，忘記密碼提示 CY Web；首次啟用／重設在 CY Web 完成。
- [ ] USER／ADMIN／SUPER_ADMIN 逐次帳密驗證原本開票／作廢／折讓／設定／裝置 rename/revoke；Identity Admin 不升格為 app SUPER_ADMIN。
- [ ] App Access 撤銷／恢復、role change、Employee disable、password/credential change 後，下一次在線驗證立即採新結果；拒絕清除相應 cache。
- [ ] 原裝置先成功在線驗證，再只阻斷雲端（光貿仍正常）／transport timeout／503：正確密碼用最後 protected Role，錯誤密碼拒絕；未在線成功的員工沒有 offline grant。
- [ ] 整機斷網阻擋全部業務；只阻斷 Windows→CYInvoice 或 Worker→private CYID（503）且光貿仍可達時驗收原降級；結果不明禁止重送。
- [ ] HTTP 401／403、畸形成功或錯 scope 回應、使用者取消不走降級；已知拒絕後 503 不復活；Device／provider／Workspace 錯誤拒絕並清全部 cache。
- [ ] Offline→reconnect 再操作採新權限；中央撤銷在斷網期間不可觀察，last-trusted 無新增 TTL 的產品邊界確實可接受。
- [ ] A/B/C 裝置 token 不能代替 Employee；撤銷後舊 Token 不可用；複製 cache／修改 endpoint／Workspace／Device／token 不可重用。
- [ ] Logout 失敗／response loss 不 replay business action，不把未撤銷 Session 宣稱清除；不在 Logs／package 保存密碼或 CYID token。
- [ ] Cloud→Local reset 清 CYID cache/binding，保留原 crash-safe 流程；未實作 0-Device recovery 前 LAST_ACTIVE_DEVICE 仍拒絕最後撤銷。
- [ ] Migration 0013 保留舊邀請與 audit；external actor 不建 fake employee／verifier，rollback 不自動降級已確認 CYID client。

125／150 DPI 仍 Deferred。本節全部為人工待驗，不以自動化測試勾選；正式切換／Release 另行授權。


## AB. V2.6.16 服務連線／原有降級／恢復實機驗收（尚未執行）

記錄 PR 精確 head、工程包 SHA-256、環境、操作前後原 Workspace／Device／Token／資料及輸入。自動化測試不勾選人工驗收。

- [ ] Built-in、CYID 各測光貿／雲端都可用，當次權限驗證採最新 role／enabled／password／App Access。
- [ ] 光貿正常，雲端 transport／timeout／503（含 private CYID outage）：原降級正確密碼與最後 Role 可用，錯密碼與未有可信 cache 者拒絕；中央帳號／Device 異動不可用。
- [ ] 光貿不可達、Cloud 正常：開票／查詢（含買方與 PDF cache）／作廢／折讓確認／背景同步停止，不新增未知或發票變更；雲端功能依原權限正常。
- [ ] 雙斷線 modal 阻擋所有業務，不能 Alt+F4／Esc 略過；保留重新檢查／關閉程式，15 秒自動檢查；先恢复雲端或先恢復光貿都立即回對應狀態，不重啟。
- [ ] 純 Local 光貿可用正常、不可用同一阻擋、恢復後正常；不產生任何 Cloud 探測或身份切換。
- [ ] 帳密錯誤／權限拒絕／Device 撤銷絕不因 503 降級；已知 Device／綁定拒絕後仍阻擋，直到同一裝置／綁定驗證成功。
- [ ] 中斷／恢復不改 Workspace、Device、Token、mode、設定、發票 pending 或輸入；不自動開票、不自動重送，結果不明先回查。
- [ ] modal 文字完整、共通圓角按鈕、tab／Enter／關閉行為符合原生操作；96 DPI 實機確認，125／150 DPI 維持 Deferred。

正式 CYID 切換仍先完成 RC AA 與原 A/B/C，未以本項取代 staging、migration／rollback 或帳號準備。
