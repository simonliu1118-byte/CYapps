# CYInvoice 開發規範（僅適用本專案）

> 範圍：`apps/CYInvoice/**`
>
> 本文件只約束 **CYInvoice（光貿 API 電子發票程式）**。不得把本文件的 Win32／原生控制項偏好自動套用到 CYapps 其他程式。其他程式應依各自需求、UI 目標與風險決定技術方案。

## 1. 核心原則：發票程式以穩定、可預測為最高優先

CYInvoice 會執行正式電子發票開立、查詢、作廢、PDF 等高重要性流程。因此 UI 與視窗架構應優先採用成熟、直接、可預測的 Windows 原生機制，避免為了外觀增加不必要的自繪、狀態同步或多層修補。

這是 **CYInvoice 專案特有的可靠性取向**，不是所有 CY 程式的通用規範。

## 2. Win32 UI 單一路徑規則

### 2.1 建立時就使用最終 control style

若 Win32 原生 control 能直接完成需求，必須在 `CreateWindowExW` / helper 建立時就使用最終 style。

禁止：

- 先用 `BS_OWNERDRAW` 建立，之後再用 `BM_SETSTYLE` 轉成一般按鈕或 Radio Button。
- 先用臨時尺寸建立，之後再由 `refine*` / `normalize*` 函式把同一 control 改成另一套最終版面。
- 同一個 control 同時保留「舊實作 + fallback + 新實作」三條路徑。

應採用：

- 一般按鈕：直接 `BUTTON + BS_PUSHBUTTON` / `BS_DEFPUSHBUTTON`。
- Radio Button：直接 `BUTTON + BS_AUTORADIOBUTTON`，需要群組時在建立時加 `WS_GROUP`。
- Tab：直接使用最終尺寸與 style 的 `SysTabControl32`。
- ListView：直接使用 `SysListView32`；欄寬由唯一 layout 函式計算。
- Edit / Static / ComboBox / ProgressBar / GroupBox：直接使用對應原生控制項。

### 2.2 每個畫面只允許一套 layout

同一 control 的尺寸／欄寬不得同時由多個階段計算。

商品 ListView 正確方式：

`取得目前 ListView client width -> 扣除固定垂直 scrollbar 槽 -> 固定必要欄寬 -> 剩餘寬度全部給品名 -> SetColumnWidth 一次`

已開立發票 ListView 正確方式：

`取得 client width -> 扣除固定垂直 scrollbar 槽 -> 固定開立時間／發票號碼／來源／15 碼訂單編號／統編（含 `0000000000`）寬度 -> 其他必要欄位 -> 剩餘寬度全部給買受人`

兩個 ListView 都不得在最後一個實際欄位右側留下假欄位。資料未滿一頁時，以 disabled 且套用 Windows theme 的原生 `SCROLLBAR` 子控制項占住 `SM_CXVSCROLL` 系統寬度；資料溢出時隱藏占位控制項，並讓 ListView 自己的原生垂直 scrollbar 在同一位置顯示。欄寬永遠只計算 scrollbar 左側的穩定內容寬度；不得再使用 rc.8 的 `SIF_DISABLENOSCROLL`／動態 Header 寬度推算方式，也不得因 scrollbar 出現而擠出水平 scrollbar。

ListView 與 scrollbar 使用 Windows theme，只有其內建 `SysHeader32` 使用原生 classic renderer 以保持清楚且連續的格線。表頭不得按壓、排序、拖動或調整欄寬。商品 ListView 在任何支援的主視窗尺寸均不得出現水平 scrollbar。已開立清單沒有資料或資料未滿時，斑馬紋空白列只能是 UI 顯示列，不得寫入本機紀錄、參與查詢或觸發雙擊詳細資訊。

主視窗 resize 可以再次呼叫同一個 layout 函式，但不得存在第二套不同演算法。

### 2.3 Paint / custom draw 不得改 business/UI state

`WM_PAINT`、`WM_CTLCOLOR*`、`NM_CUSTOMDRAW`、`WM_DRAWITEM` 等繪圖路徑只能決定外觀，不得順便：

- Enable/Disable controls
- 改 Radio checked state
- 改 buyer mode / tax mode
- 改資料模型
- 發 API 請求

狀態必須在明確的 state transition 函式中一次完成。繪圖只讀取狀態，不修改狀態。

## 3. 自繪只保留 Win32 沒有乾淨原生方案或本專案明確指定的視覺需求

允許的 custom draw / subclass 例外目前包括：

1. `SysListView32` 斑馬紋。
2. 發票狀態字色、已作廢灰字／刪除線與上傳紅／黃／綠燈號。
3. 商品 ListView subitem 的「刪除」按鈕外觀與 hit-test（避免每列建立大量 HWND Button）。
4. 商品 ListView subitem 就地編輯：在 cell 上暫時建立真正 Win32 `EDIT`，只 subclass Enter / Tab / Esc / focus 行為。
5. **CYInvoice 主頁籤的藍色底線標頭外觀**：控制本體仍是 `SysTabControl32`，只以 `TCS_OWNERDRAWFIXED + WM_DRAWITEM` 畫矩形中性色標頭、選中粗體文字與藍色底線。選取狀態、鍵盤導覽、`TCN_SELCHANGE` 與 page frame 必須繼續由原生 Tab control 管理。

這些例外不得演變成第二套 control/state 系統。

## 4. 禁止 patch layer / refine layer 疊加

不得用額外檔案或 `init()` 偷換 callback、包住原本 WndProc，再於 `WM_CREATE` 後二次修正控制項。

若需要重構，應直接修改正式建立路徑，並刪除被取代的舊路徑。

## 5. 視窗、鍵盤與非同步規則

- HWND 的建立、銷毀、文字／狀態更新必須在 UI thread 執行。
- 一般輸入控制項使用 Win32 dialog navigation 處理 Tab / Shift+Tab；不要為每一欄各寫一套 Tab 模擬。
- 密碼驗證等明確流程可對 Enter 做最小 subclass，使 Enter 等同該畫面的確認按鈕；不得讓主畫面 Edit 的 Enter 意外觸發開立發票。
- API / 檔案解析等長工作在 worker goroutine 執行，再以 `PostMessage` 回 UI thread。
- worker 回報必須用 generation / state 檢查，避免更新已關閉或已過期的視窗。
- Modal 視窗不得重複實作多套互不一致的 nested message loop。

## 6. 發票資料與 API 相容性不得混淆

- 「光貿已開立成功」與「本機保存失敗」必須分開處理。
- 查到相同 OrderID 不等於本次開立成功；需核對發票狀態與必要欄位。
- 結果不明不得自動重送。
- 作廢後重開、防重複、環境隔離等既有安全規則不得因 UI 重構而弱化。
- AMEGO 合法 wire-format 差異應在 API decoder 邊界正規化。例如查詢回覆中的日期／時間可能是 JSON string 或 JSON number；兩者可轉為同一內部文字格式，但**不得因此放寬成功核對條件**。
- 上傳狀態只在 AMEGO 明確回報 `99` 時顯示綠燈；`91` 顯示紅燈；處理中或尚未確認的已開立發票顯示黃燈。未知不等於成功。
- 統編名稱查詢收到 code 99 或正常空結果時，代表 API 已回覆但查無資料；右上 API 健康狀態保持正常並允許人工輸入買方名稱。只有連線、逾時、授權、簽章、帳號或服務錯誤才標示 API 異常。

## 7. 修改前後檢查

每次 UI 重構或新增視窗至少確認：

- 是否新增第二套 layout / refine / normalize 路徑。
- 是否把原生 control 不必要地改成 owner-draw。
- Paint handler 是否偷偷修改狀態。
- 商品 ListView 是否永久保留垂直 scrollbar 槽，且未出現 horizontal scrollbar 或最後假欄位。
- 已開立清單是否永久保留垂直 scrollbar 槽；開立時間、發票號碼、來源（`MO店+`／`好賣+`／`iOPEN`）、15 碼訂單編號及統編（含 `0000000000`）是否能完整顯示，買受人是否取得所有剩餘寬度。
- 已開立清單空白顯示列是否只用於斑馬紋，不可選取、不可雙擊、不得進入任何發票資料或安全判斷。
- 一般消費者是否只能含稅輸入；未稅選項需 disabled。
- 設定密碼／App Key 不得因空白欄位誤存而破壞既有有效設定。
- CI tests / vet / Windows x64 build / packaging 全部通過後才提供實機測試 ZIP。

## 8. Scope 再確認

本規範的「Win32 原生優先」是為了 CYInvoice 的電子發票可靠性與維護可預測性。

**不得因本文件存在，就要求 CYAccounting、CYEnvelope、下載器或其他 CYapps 專案一律採用相同 UI 技術。**
