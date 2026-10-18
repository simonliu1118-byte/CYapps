# CYInvoice V3.0／V3.1 合併發行後待驗收清單

更新：2026-10-19（Asia/Taipei）｜適用：CYInvoice **V3.1.0 Build 0** 正式發行之後

> **狀態：使用者明確批准延後，不代表已通過。** 本清單合併 V3.0 原未實測項目與 V3.1 新的跨機／角色實機項目，**不是新功能需求，也不是已發現的缺陷**。V3.1 Source 的 Windows／Cloud／Governance 隔離 CI 和使用者本機 MCP 原 D1／Worker 受控切換各有獨立證據；**不可用 CI、health、無帳密拒絕或單機備份取代真人驗收**。使用者允許在這些項目未完成的情況下正式發布 V3.1.0，後續逐項補證據。

## 驗收記錄格式

勾選只表示實際完成真人／真環境操作且有可核對結果；每項請在私有驗收紀錄填入 **日期、Windows 版本、軟體 VERSION／BUILD、100/125/150% 縮放、測試情境、結果、測試負責人、回退方式**。使用現有 Workspace、合法測試帳號與最小權限；不在公開 repo、GitHub Issue／PR、Actions Log 或此檔案紀錄正式 D1/Worker 資源 ID、員工帳密、Device Token、客戶發票／訂單、備份密鑰、資料指紋或 Time Travel bookmark。

## A. V3.0 遺留的真人／實機待驗收

- [ ] **V30-01 真實 Email／收信鏈路**：既有 CYID／CYWEB 邀請、復原邀請或必要通知的真實 Email 送達、延遲／逾時／拒收及使用者實際操作；不得用測試信箱合成回覆宣稱正式寄送成功。
- [ ] **V30-02 獨立 Windows 使用者／異機不同金鑰災難還原**：現有加密備份已核對在**同一 Windows 使用者**下解密及本機 SQLite 還原；尚未證明與原 DPAPI／金鑰不同的 Windows／裝置可以按設計完成復原。**不得把單機成功說成異機成功**。
- [ ] **V30-03 DR-01～DR-13 災難復原真人流程**：CYWEB 超管簽發／送達、Windows Preview／Claim、一張一次、10 分鐘失效與撤銷、雙裝置競爭、結果不明原 Pending Token 回讀、Workspace／Device 一致性、成功接管後既有 Device 撤銷、失敗回滾及稽核紀錄；詳見 [原 DR 驗收矩陣](CLOUD_DISASTER_RECOVERY.md)，不得為測試撤銷唯一可信現役裝置。
- [ ] **V30-04 Local／CYID 舊裝置升級與業務連續性**：兩台以上真 Windows 的既有 Workspace、Device／權限／公司卡／本機 DPAPI、發票／PDF／快取不重置；錯誤綁定拒絕，正式 AMEGO／本機帳務不重送、不誤清；不重新啟用已退役的 Built-in D1。
- [ ] **V30-05 CYID 權限變更／裝置撤銷及連線降級**：不同角色、員工啟停、App Access／改密碼後重新驗權；Device 撤銷不得降級放行、不可用複製快取冒用、可控撤銷後的本機資料安全清除及中斷後恢復。不同情境僅用可拋棄裝置及授權測試資料。
- [ ] **V30-06 網路中斷／503／恢復與未知官方結果**：Windows→原 Worker、原 Worker→CYID、AMEGO 可用／不可用矩陣、舊 last-trusted 降級邊界；帳密錯誤／權限拒絕不得放行，作廢／開立回覆不明不得自動重送。
- [ ] **V30-07 公司卡人工套用與多裝置版本連續性**：CYID 超管手動核准，既有 App Key／MO 密碼僅在原 Windows DPAPI 保存並確認版本與統編相符；過期卡、不同 Workspace／Device、撤銷及斷線情況不得假稱可用。不須公開或測試正式秘密值。
- [ ] **V30-08 正式 Windows 視覺與舊業務回歸**：真實 100%／125%／150% DPI 及 High Contrast 下首次啟動、公司卡、邀請、復原、主畫面、裝置管理、上傳問題／處理中、PDF／匯入與作廢等控制項未裁切且可操作；Local 與 CYID 的實際驗證和 AMEGO 測試環境結果分開記錄。

## B. V3.1 非折讓跨機 Work Item 待驗收

- [ ] **V31-01 兩台真 Windows 正向回報／跨機查詢**：用 CYID Cloud 模式、同 Workspace 兩個不同 Device；A 端對未收回紙本作廢／非折讓上傳例外明確確認、建立匿名 Work Item，B 端 ADMIN／SUPER_ADMIN 能看到正確同筆待辦及篩選分頁。Local 不顯示 Cloud 待辦。
- [ ] **V31-02 真人 CYID 角色矩陣**：USER 僅提交本人允許的非折讓例外，不具主管查詢／核准權；ADMIN／SUPER_ADMIN 須每次重驗、不同 Workspace／已失權員工或 Device 拒絕。不能用無帳密 401／403 拒絕測試代替有效帳密正向及失權驗收。
- [ ] **V31-03 手動兩階段覆核及 CAS**：B 端 open→review→closed，逐次用 revision CAS，兩台競爭只一筆成功；HTTP 結果不明使用原 ID **GET 唯讀查回**，不盲重送、不顯示舊的可操作清單。
- [ ] **V31-04 稽核不可修改／刪除與業務隔離**：真權限下核對每個 work item 狀態轉態與 actor／Device／revision／人工封存原因的稽核；封存只表示**人工流程完成**，並非光貿 AMEGO 作廢／上傳成功，不能觸發發票重送或進入 3.2 折讓。
- [ ] **V31-05 斷線／撤銷／重開及資料保護**：在真 Device／CYID 權限變更、離線→恢復或回覆遺失時，UI 不能保留舊管理權限；回查不洩漏員工、發票號碼、OrderID、買方、原始錯誤文字與私有 metadata；不因測試刪改原業務資料。
- [ ] **V31-06 WinForms 真 DPI／版面操作**：跨機待辦窗最小／標準尺寸、七欄自適應、工作項目 ID 欄及查回輸入、角色操作按鈕，在 Windows 100%／125%／150%、必要時 High Contrast 目視且可操作；CI smoke 只算前置證據。

## C. 安全條件與後續處置

- **發行前已做、不要重跑**：使用者本機 MCP 回報原 CYInvoice D1 schema15→16 的一次性 canonical 0016 受控套用、原 Worker schema16／`WORK_ITEMS_ENABLED=true`、備份與本機還原／rollback 演練、前後資料一致性及未授權拒絕；證據保留於**本機私人記錄**（不公開路徑與檔案內容），公開僅登記「使用者本機回報完成」。**不要重做 0014／0015、再次套用 0016、操作 CYID D1、建立測試 Worker 或新 D1**。
- **非 3.1 驗收項、不得誤列已修復**：V2 時期人工折讓歷史缺欄位／3.2 正式折讓 API 另行處理；3.3 需求不包含在 V3.1 發行內。既有歷史缺欄位不得自行生成、清空或聲稱修復。
- **發現真實問題時**：保留可回復備份、不要毀損原資料；建立獨立 Issue／Patch，依 CYInvoice 的 Patch／Build 規則修正與驗證，再由使用者決定是否要新正式 Release。
- **本清單仍保留未勾選**；沒有真人測試證據就不得勾選，也不得把「使用者批准先發行」改寫成「已驗收」。使用者 2026-10-19 明確接受以既有 Windows／Cloud／Governance CI、部署前檢查與 V3.1-D 本機回報為**可先發行**的依據，真人驗收另補。
