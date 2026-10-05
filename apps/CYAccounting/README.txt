志遠記帳系統 V1.3.0

正式版本：V1.3.0
平台：Windows 10/11 x64
形式：可攜版，解壓縮後直接執行 CYAccounting.exe，不需另行安裝 Python。

V1.3.0 更新重點
- 完成 CYAccounting Phase 1 視覺整理；主介面採 Blue Theme，ACC 綠色保留為程式 Icon identity。
- 輸入記帳頁維持大輸入欄位與較大字體，方便快速輸入；記帳資料表則維持高密度閱讀，資料列 23px、表頭 24px。
- 基本資訊、收入、支出、輸入確認、設定與匯入區塊的框線、字級與間距統一整理。
- 一般下拉選單使用一致的 chevron 視覺；資料表內嵌編輯仍保留窄版配置。
- 收入／支出科目管理恢復 V1.1.0 已驗證的原生 QTabWidget 作法，不再使用額外自訂 Tab 幾何。
- 子視窗正常沿用 ACC 程式圖示，不再嘗試移除 Windows 標題列 Icon。
- 主畫面 footer、版本資訊與版權資訊完成整理。
- 既有記帳流程、Enter/Tab/Esc、SQLite 資料格式、歷史交易文字快照、備份／還原、Google Drive、Excel 匯入、鎖帳、7 位數金額限制與雙重 DELETE 清除流程均維持原有行為。
- 本版正式驗收基準為 100% / 96 DPI；125% / 150% DPI 尚未列入本版完成範圍。

資料與更新注意事項
- 預設帳本位於程式資料夾下的 Data。只刪除 CYAccounting.exe 不會刪除帳本。
- 更新前建議先備份原本的 Data；不要直接用新版整個資料夾覆蓋正在使用的舊版資料夾。
- 若曾自訂資料庫位置，資料仍保存在該自訂位置；Google Drive 上的既有備份也不會因移除本機程式而自動刪除。
- 正式可攜包不包含使用者帳本、設定、OAuth client JSON、token、log 或既有備份。

清除全部本機帳本
- 設定頁提供「清除所有記帳資料與期初餘額」。
- 必須連續兩次各自輸入完全相同的大寫 DELETE 才會執行；取消或任何一次輸入錯誤都不會清除。
- 執行前程式會先建立可驗證的本機復原備份；既有本機與雲端備份保留。
- DELETE 只是防誤觸確認文字，不是管理密碼。

Google Drive 首次設定
1. 在 Google Cloud 建立「桌面應用程式」OAuth 用戶端並啟用 Google Drive API。
2. 下載 OAuth client JSON。
3. 志遠記帳系統 → 設定 → Google Drive →「匯入 OAuth 憑證」→「連結 Google Drive」。
4. 連結後可啟用自動雲端同步，也可在「匯入」直接選取 Google Drive 的 .xlsx 或 Google 試算表。
5. 舊式 .xls 請先另存為 .xlsx 後再匯入。

版本與資料庫相容性
- 既有資料庫可直接沿用，不會因本次視覺整理而重建或改寫歷史交易資料。
- 羅馬拼音與內部識別一律使用 Wade–Giles 或 CY；志遠使用 Chihyuan / Chih-yuan / CY。

Copyright © 2026 C.C. Liu, Chihyuan Co. All Rights Reserved.
