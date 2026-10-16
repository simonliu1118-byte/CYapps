# CYERPAutoQuery — SMART ERP 自動查詢工具

收到一個品號，自動到鼎新 SMART ERP「品號資料建立作業 (INVI02)」查詢，用 ERP 自己的「轉出至 Excel」取得該品號的完整資料（單頭約 150 個欄位＋庫別）。介面與 CYERPAutoInput 一致；初版在視窗輸入品號、結果列在主視窗，之後會接上 Web。

## 使用方式

1. 在 ERP 開啟「品號資料建立作業」，停在檢視狀態（不要在新增或修改中）。
2. 開啟 `CYERPAutoQuery.exe`，右上角狀態標籤應為「ERP：檢視」。輸入品號，按「開始查詢 ERP」或 Enter。
3. 查詢期間實體滑鼠鎖定、畫面中央顯示「自動查詢中」；完成後表頭與明細列在主視窗，「複製明細」可貼到 Excel。
4. 查無品號時顯示「查無此品號」。
5. 查詢中按 **Esc** 可立即中止並解除滑鼠（不會替你按 ERP 的取消）。

## 自動化流程

```
找到 INVI02 視窗（ERP 開著其他作業不影響；多個時用處於檢視狀態的那個；沒有時從主視窗「作業速查」輸入 INVI02 開啟）→ 最大化並帶到前景
-> 確認在檢視狀態、沒有其他對話框
-> F6 開啟「設定查詢條件」（已開著就直接用）
-> 切到「一般選項」（視窗會記住上次頁籤；已在一般選項就不點）
-> 品號條件設為「=」，輸入品號並讀回確認
-> 確定，等查詢視窗關閉
-> 確認表頭品號 = 查詢品號；ERP 轉到資料瀏覽且品號空白（0.8 秒不變）即為查無此品號
-> 切到「基本資料1」（從資料瀏覽轉出不含庫別）
-> Ribbon「Excel」→「轉出至 Excel」
-> 匯出設定：確認格式為 xlsx；「單頭」「單身」各按一次「全選」（儲存設定會記住別人改過的勾選）；儲存檔案填入程式旁 TEMP 資料夾的新檔名 → 確定
-> 「檢視Excel資料?」按 No（按 Yes 會真的開檔）
-> 讀取單頭資料與單身資料，立即刪除暫存檔，再次確認品號沒變，列出結果
```

## CY Web 查詢（Agent）

- 主視窗「Web 連線」：填 CY Web 網址、CY Web 超級管理員在「ERP 品號查詢」頁產生的 Token（只顯示一次），勾選「啟用 Web 查詢」。Token 以 Windows DPAPI 加密存在 `config/agent.json`（換電腦或換 Windows 帳號需重新設定）。
- 啟用後程式每 3 秒向 CY Web 領一次查詢工作，用與本機相同的流程查詢並回報；狀態列顯示「Web：已連線／查詢中／連線失敗」。
- 只回傳約定的欄位（`ItemFieldCatalog`）；失敗只回固定錯誤代碼，不送出本機訊息或路徑。
- 本機正在查詢時不領工作；Token 被拒時停止連線，需重新設定。
- Contract：chihyuan-web `docs/architecture/ERP_ITEM_QUERY_BRIDGE_CONTRACT.md`。

## 讀取方式

- 資料來自 ERP 匯出的 xlsx，由程式自己解析（不需要 Excel 或 OpenOffice）。欄位名稱就是 ERP 的欄位標題（例如「品名」「標準售價」「售價定價一」「商品描述」）；日期欄轉成 yyyy/MM/dd。
- 每次匯出前在「單頭」「單身」各按「全選」，不受別人改過的勾選影響。
- 只保留 `ItemFieldCatalog.cs` 列的欄位（使用者 2026-10-16 指定），其他欄位（成本、庫存金額等）解析後立即丟棄；這份清單也是之後 Web 的欄位白名單。
  - 單頭：品號、品名、規格、單位、庫存數量、商品分類（ERP「商品」）、商品分類名稱（料件分類三名稱）、商品描述、條碼編號、批號管理、有效天數、主供應商、主供應商名稱、標準進價、標準售價、售價定價一～六。
  - 庫別：庫別、庫別名稱、儲位、安全存量、經濟批量、上次盤點日、最近入庫日、最近出庫日。
  - 匯出缺少清單中的欄位時停止並列出缺少哪些，不回傳不完整的結果。
- 頁籤：先點已量測的固定位置，點完確認該頁籤真的顯示；沒有顯示才用 OCR 讀頁籤列找文字再點。
- Ribbon「Excel」與選單「轉出至 Excel」不是 Win32 控制項，以 OCR 找文字點擊（同 CYERPAutoInput 的「列印 → 直接列印」）；點錯 Ribbon 可能觸發其他功能，所以不用固定座標。
- 每次點擊前都確認該點沒有被其他視窗遮住。
- 與其他 CY ERP 自動化程式共用一把機器層級鎖，避免兩支程式同時操作 ERP。

## 檔案

- `logs/CYERPAutoQuery_YYYYMMDD.log`：本機 LOG，預設只記錄長度，不記錄品號與欄位內容。
- `config/agent.json`：CY Web 連線設定（Token 已加密，不要複製到其他電腦）。
- `TEMP/`：ERP 匯出暫存，讀完立即刪除（含成本、庫存金額，不要手動保留）。
- `runtime/ocr/*.onnx`：PaddleOCR 模型（與 CYERPAutoInput 相同），Ribbon／選單與頁籤備援使用。

## 開發

- C# / .NET 8 / WinForms，Windows x64 self-contained 單檔。
- 單元測試：`dotnet test tests/CYERPAutoQuery.Tests/CYERPAutoQuery.Tests.csproj`（純邏輯，任何 OS 可跑）。
- OCR 模型：`tools/fetch-ocr-models.ps1` 依 `tools/ocr-models.json` 下載並核對 SHA-256。
- 圖示：`tools/fetch-canonical-icon.ps1` 從 AITeam CY Icon Family 固定 revision 下載 `AutoQ.ico` 並核對 SHA-256（不進 Git）。
