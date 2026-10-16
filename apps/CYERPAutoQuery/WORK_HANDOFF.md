# CYERPAutoQuery Work Handoff

## 現況（V0.1.0 Build 0，2026/10/16）

- 初版完成：視窗輸入品號 → INVI02 查詢 → 讀表頭與四個頁籤 → 跳出明細。尚未實機驗收。
- 程式結構：
  - `ItemQueryService`：唯一的查詢流程 owner（找視窗、F6、一般選項、確定、核對、切頁籤、讀欄位）。之後 Web 入口直接呼叫它，不另寫第二套流程。
  - `Invi02Layout`／`ItemCodeRules`／`TabStrip`：純規則（標題比對、品號檢查、分列、頁籤座標），有單元測試。
  - `Win32Automation`／`NativeMethods`／`VisionServices`／`OcrWordFinder`／`CyVisual*`：由 CYERPAutoInput 複製的必要部分（使用者決定先複製，日後抽共用庫）。

## 已知 ERP 行為（使用者提供）

- 查詢快捷鍵 F6，開啟「設定查詢條件」；視窗會記住上次使用的頁籤（一般選項／進階設定）。
- 已在一般選項時點一般選項不會有反應（正常）。
- 只有品號用一般選項查；其他條件要用進階設定：先「清除」舊條件，再選欄位、條件、輸入值後按「加入」。要用哪些條件之後決定。
- 一般選項條件順序：`<`、`<=`、`=`、`>=`、`>`、`%like%`、`like%`、`%like`。進階設定條件順序不同：`=`、`>=`、`<=`、`>`、`<`、`<>`、`%like%`、`like%`、`%like`。
- INVI02 頁籤順序：基本資料1、基本資料2、採購生管1、採購生管2、售價、成本、標準包裝、屬性類別頁、產品圖示、資料瀏覽。

## 實機驗收重點

1. LOG 的 `INVI02 found class=`：確認視窗類別（程式預期 `TfrmInvi02`，不同也能用，但請回報）。
2. 頁籤：LOG `tab ... by=geometry` 表示座標命中；出現 `by=ocr` 或 `geometry click did not show` 請回報 LOG，用來修正座標。
3. 「一般選項」條件設為 `=` 是否成功（LOG `operator set shown=`）。
4. 查無品號時 ERP 的實際反應（是否跳訊息框），目前做法是停止並請人工處理。
5. 明細中各欄位的「第幾列第幾欄」，用來決定之後要固定取用的欄位。

## 後續工作

- 依實機明細決定要取用的欄位，在 `Invi02Layout` 加上具名欄位位置（比照 CYERPAutoInput `ErpLayoutResolver`）。
- 進階設定查詢（清除 → 選條件 → 加入）。
- Web 入口（呼叫 `ItemQueryService`）。
- CYERPAutoInput 也取用 `Local\Chihyuan.SmartErp.UiAutomation` 鎖，兩支程式同機使用才真正互斥（目前只有 CYERPAutoQuery 會取用）。
- Icon：AITeam Icon Family 尚無本程式圖示，目前使用 Windows 預設圖示。
- 兩支程式穩定後抽出共用庫，移除複製的程式碼。
