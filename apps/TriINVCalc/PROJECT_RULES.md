# TriINVCalc Project Rules

本文件只記錄 `apps/TriINVCalc/**` 的專案補充與例外。共通規則依根 `REPOSITORY_RULES.md`，Public repo 規則依根 `REPO_POLICY.md`。

## 1. 正式基準與名稱

- 專案識別名稱固定為 `TriINVCalc`。
- 程式顯示名稱固定為「三聯式發票開立計算機」。
- 正式發行以 Windows x64 portable package 為原則，不要求安裝器。
- 專案版本以本目錄 `VERSION` 為唯一基礎版本來源，`BUILD` 依共通規則管理返修次數。

## 2. 功能範圍與資料安全

- 本工具僅供三聯式發票金額試算，不串接電子發票平台、政府 API 或客戶資料庫。
- 正式程式不得持久化買受人、地址、統編、發票號碼等客戶或發票識別資料。
- 執行期 Trace／ERROR log 僅供本機除錯，不得提交 Public Git，也不得放入正式 Release package。
- 原始碼、測試與文件不得包含真實客戶資料、實際發票資料或其他營運資料。

## 3. 計算核心

- 所有交易固定為應稅，稅率固定 5%。
- 商品輸入固定五列；數量為半形整數 `1–999`，含稅單價為 `1–9999` 且最多兩位小數；空白數值依既有程式規則視為 0。
- 每列含稅金額依數量 × 含稅單價後採一般商業四捨五入為整數元；含稅折扣僅接受整數元。
- 整張發票以折扣後含稅總計反推整數營業稅，再取得未稅銷售額；明細未稅金額需透過尾差調整與總額一致。
- 未稅單價優先顯示兩位小數，必要時可使用三位小數；每列必須滿足「四捨五入（顯示未稅單價 × 數量）＝顯示未稅金額」。
- 計算異常必須寫入獨立 ERROR log；一般操作 Trace log 採循環上限，不得無限成長。

## 4. UI 與發行驗證

- 右側預覽維持三聯式發票視覺結構；發票金額欄、銷售額、營業稅與總計以整數元顯示。
- Enter／Tab 需能依既定輸入順序移動欄位；快速切換不得造成訊息重入、無回應或程式崩潰。
- 正式 Release 前至少驗證 Windows x64 啟動、Windows GUI 子系統、icon/manifest、Enter/Tab 焦點流程、五列輸入、折扣、尾差分配、四層金額核對與 LOG 輸出。
- EXE icon 必須由標準 Windows PE resource 正確內嵌，不得以執行階段備援取代檔案本身的 Icon resource。
- 程式畫面可保留使用者指定的 `© 2026 C.C.LIU All Rights Reserved.` 小型署名；正式 Release、README 與 package 文件仍依共通規則使用標準 copyright notice。
