# CYERPAutoQuery — SMART ERP 自動查詢工具

收到一個品號，自動到鼎新 SMART ERP「品號資料建立作業 (INVI02)」查詢，讀回品號、品名、規格與「基本資料1／基本資料2／採購生管1／售價」四個頁籤的欄位。初版以視窗輸入品號、查詢完跳出明細；之後會接上 Web。

## 使用方式

1. 在 ERP 開啟「品號資料建立作業」，停在檢視狀態（不要在新增或修改中）。
2. 開啟 `CYERPAutoQuery.exe`，輸入品號，按「查詢」或 Enter。
3. 程式自動操作 ERP 約數秒，完成後跳出明細；「複製全部」可貼到 Excel。
4. 查詢中按 **Esc** 可立即中止（不會替你按 ERP 的取消）。

## 自動化流程

```
找到唯一的 INVI02 視窗（ERP 開著其他作業不影響）→ 最大化並帶到前景
-> 確認在檢視狀態、沒有其他對話框
-> F6 開啟「設定查詢條件」（已開著就直接用）
-> 切到「一般選項」（視窗會記住上次頁籤；已在一般選項就不點）
-> 品號條件設為「=」，輸入品號並讀回確認
-> 確定，等查詢視窗關閉
-> 確認表頭品號 = 查詢品號（不同即視為查無此品號）
-> 依序切換 基本資料1、基本資料2、採購生管1、售價，讀取欄位
-> 再次確認品號沒變，跳出明細
```

## 讀取方式

- ERP 欄位是 Delphi 控制項，以 `WM_GETTEXT` 直接讀取文字、勾選框以 `BM_GETCHECK` 讀取；欄位標籤不是視窗，所以明細以「頁籤＋第幾列第幾欄」標示位置。要固定取用哪些欄位，之後依實機讀到的結果決定並命名。
- 頁籤：先點已量測的固定位置（INVI02 頁籤等寬，第 i 個在頁籤控制項左緣 +56+79i px；查詢視窗「一般選項」+50、「進階設定」+116），點完確認該頁籤真的顯示；沒有顯示才用 OCR 讀頁籤列找文字再點。
- 每次點擊前都確認該點沒有被其他視窗遮住。
- 與其他 CY ERP 自動化程式共用一把機器層級鎖，避免兩支程式同時操作 ERP。

## 檔案

- `logs/CYERPAutoQuery_YYYYMMDD.log`：本機 LOG，預設只記錄長度，不記錄品號與欄位內容。
- `runtime/ocr/*.onnx`：PaddleOCR 模型（與 CYERPAutoInput 相同），頁籤 OCR 備援使用。

## 開發

- C# / .NET 8 / WinForms，Windows x64 self-contained 單檔。
- 單元測試：`dotnet test tests/CYERPAutoQuery.Tests/CYERPAutoQuery.Tests.csproj`（純邏輯，任何 OS 可跑）。
- OCR 模型：`tools/fetch-ocr-models.ps1` 依 `tools/ocr-models.json` 下載並核對 SHA-256。
