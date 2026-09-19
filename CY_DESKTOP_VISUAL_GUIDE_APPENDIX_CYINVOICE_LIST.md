# CY Desktop Visual Guide — CYInvoice List Reference Appendix

> [!CAUTION]
> **本附件仍在討論與調整中，尚未定案，也不是目前有效的開發規範。**
>
> 本附件只記錄目前 CYInvoice 清單中已驗證、值得保留的**結構與實作方法**，供未來 CY Desktop Visual Guide 與其他程式的 Table / ListView 設計參考。
>
> **本附件不把 CYInvoice 現行顏色、字體、字級、列高、欄寬或其他視覺數值視為未來標準。** 未來若 CYInvoice 套用新版 Visual Guide，所有視覺數值都應依新版 Typography、Theme、Density、Surface 與 Table token 重新決定。

**狀態：Draft / Reference Only**  
**來源基準：CYInvoice `main` commit `d812d03f56526b409082d9086e0b9cf554f36652`（2026-09-19）**

---

## 1. 為什麼保留這個參考

目前 CYInvoice「已開立發票清單」的完成度高，尤其以下部分值得保留：

- 表頭與表身形成連續的欄位網格。
- 欄位分界線不應在 Header / Body 之間產生肉眼可見的 1 px 錯位。
- 欄寬由單一幾何來源管理，而不是 Header 與 Body 各自計算。
- 能處理視窗尺寸、viewport、scrollbar 與 DPI 對可用寬度的影響。
- 允許固定欄位 + 一個彈性欄位的商務清單配置。
- Owner-draw 時 Header 與 Body 使用一致的欄位 Bounds / 邊界歸屬，讓格線能自然延續。
- 保留原生 Windows ListView / scrollbar 等成熟行為，不為純視覺一致性重寫整套控制項。

這些是**方法與品質標準**，不是現行外觀數值的永久保存。

---

## 2. 目前相關實作位置

目前主要參考檔案：

- `apps/CYInvoice/src/CYInvoice.WinForms/RecordsControl.cs`
  - 建立清單欄位。
  - 定義欄位語意與對齊。
  - 集中執行 `LayoutColumns()`。
  - Owner-draw 表身。
  - viewport 改變後重新計算欄寬。

- `apps/CYInvoice/src/CYInvoice.WinForms/NativeListViewHost.cs`
  - 包裝原生 `ListView`。
  - 計算實際可用 viewport width。
  - 處理 DPI logical width。
  - 集中套用欄寬。
  - Owner-draw Header。
  - 監看 resize / scroll / viewport 變化。
  - 使用原生 scrollbar。

- `apps/CYInvoice/src/CYInvoice.WinForms/UiControls.cs`
  - 另有共用 `DataGridView` 與 scrollbar 預留的經驗可參考。
  - 同樣不把目前顏色、列高等數值視為未來 Visual Guide 標準。

---

## 3. 必須保留的核心概念

### 3.1 Grid Continuity

Header 與 Body 應視為同一張表格的同一組 Column Geometry。

驗收標準：

- Header 的每一條欄位分界應順暢延續到 Body。
- 不接受肉眼可見約 1 px 的左右錯位。
- Resize、DPI 切換、scrollbar 出現／消失後仍需保持一致。

若某 framework 無法低成本保證完整垂直格線連續，**寧可減少垂直格線，也不要保留錯位的格線。**

### 3.2 Single Source of Column Geometry

Header / Body 不應各自維護一套欄寬。

建議概念：

1. 先取得實際可用 viewport width。
2. 建立一份 column width collection。
3. 固定欄位先決定合理寬度。
4. 指定一個主要內容欄位吸收剩餘寬度。
5. 最後一次性套用到真正的 columns。
6. Header / Body 都由同一組 column bounds 繪製或呈現。

### 3.3 Flexible Column

CYInvoice 目前採用固定欄位加一個可伸縮主要欄位的方向，這個概念適合商務桌面清單。

未來可以保留：

- 日期、金額、狀態、短代碼等欄位可採固定或限制範圍。
- 名稱、說明、備註等主要文字欄位可吸收剩餘寬度。
- 不為了塞滿視窗而讓所有欄位一起無規則縮放。

**哪一欄伸縮、各欄最小寬度與實際寬度屬 App / Layout 決策，不由本附件寫死。**

### 3.4 Viewport / Scrollbar Awareness

可用欄寬應以實際 viewport 為準，而不是只看外層 control 的宣告寬度。

需考慮：

- control border。
- 垂直 scrollbar。
- DPI scaling。
- Windows 原生 ListView client rectangle。
- resize 後的重新 layout。

DataGridView 或其他 framework 若原生 API 不同，可以採不同實作，但應維持相同結果：**欄位總寬與真正資料 viewport 對得上。**

### 3.5 Consistent Border Ownership

Owner-draw Header / Body 時，欄線要明確決定由哪一個 cell 負責繪製。

CYInvoice 目前的有效概念是：

- 使用 cell 自己的 bounds。
- 垂直分界畫在一致的 right edge。
- 水平分界畫在一致的 bottom edge。
- Header 與 Body 採相同 edge 邏輯。

這可以避免一邊畫左線、一邊畫右線造成重疊、缺口或 1 px 位移。

實際 line color、alpha、粗細則必須由新版 Visual Guide 決定。

---

## 4. 現行 CYInvoice 數值：只作 Source Snapshot，不得直接升格為規範

以下數值只用來幫未來維護者辨識目前實作，**不是推薦值，也不是新 Visual Guide token**：

- 清單目前曾使用 `10F` 字體設定。
- `RecordsControl` 建立 host 時目前使用 `rowHeight = 22`。
- `NativeListViewHost` 預設建構值另有 `rowHeight = 27`。
- Header 現況背景約為 `RGB(246,246,246)`。
- Owner-draw 格線現況曾使用 `RGB(190,190,190)`。
- `UiControls.Grid()` 另有現行 `DataGridView` 的 `ColumnHeadersHeight = 28`、`RowTemplate.Height = 27`、`GridColor = RGB(226,226,226)`。
- 已開立發票清單目前的欄位初始寬度包含 `140 / 108 / 122 / 150 / 82 / flexible / 86 / 76 / 88 / 48` 等值。

未來導入新版視覺時，上述值全部應重新檢視，不得以「CYInvoice 現在就是這樣」作為保留理由。

---

## 5. 未來必須改由 Visual Guide / App Profile 決定的項目

### 5.1 Typography

未來由：

- `Table.Header.Font`
- `Table.Body.Font`
- `Table.Header.Weight`

或相等角色的 token / app profile 決定。

不得在參考附件中固定成目前 `10F`、`12F` 等現況。

### 5.2 Density

未來由 Compact / Standard / Comfortable 等密度層級決定：

- Header height。
- Row height。
- cell vertical padding。
- cell horizontal padding。

大按鈕／大字級的程式不應仍套用小 row height；高密度商務清單也不應被迫使用過大的 Web-style row。

### 5.3 Theme / Surface

未來由 Theme / Surface token 決定：

- Header background。
- Body background。
- alternating row。
- selection background。
- text color。
- grid / divider color。
- outer border。

CYInvoice 現行灰色數值只能當 snapshot。

### 5.4 Column Width

欄寬不屬品牌 Visual token。

各程式依：

- 欄位語意。
- 常見資料長度。
- 主要操作需求。
- 視窗最小寬度。
- 是否允許水平 scrollbar。

自行設計。

Guide 應規範「如何保持 Header / Body 共用 Geometry」與「不要因錯誤縮放造成截斷／錯位」，而不是規定所有 CYApps 使用同一組欄寬。

---

## 6. 未來跨 Framework 套用方式

本附件不是 WinForms-only 規範。

### WinForms ListView

可直接參考目前 `NativeListViewHost` 的概念：

- viewport 實測。
- DPI logical conversion。
- centralized column widths。
- owner-draw shared edge logic。

### WinForms DataGridView

可參考 `ReserveVerticalScrollBar()` 的方向：

- 固定欄先計算。
- 預留 scrollbar。
- 彈性欄吸收剩餘寬度。

但未來仍應以實際 DataGridView border / client rect 驗證，不應直接複製 magic number。

### Qt / PySide6

應使用同一份 model / column size policy 控制 Header 與 viewport；避免 header section size 與 body geometry 分離。

### Win32 / Go

若 Header 與 rows 為自繪，應共用同一份累積 X positions / column rectangles，禁止 Header 和 row 各自做 rounding。

---

## 7. 建議的抽象資料結構

未來若建立共通 helper，建議概念上分離：

```text
TableVisualProfile
  HeaderFontRole
  BodyFontRole
  HeaderHeight
  RowHeight
  HeaderSurface
  BodySurface
  DividerColor
  SelectionSurface
  CellPadding

TableColumnLayout
  ColumnDefinitions
  Fixed / Flexible policy
  MinWidth
  Alignment
  CalculatedBounds
```

重點是：

- `TableVisualProfile` 可以隨新版 Theme / Density 改變。
- `TableColumnLayout` 可以隨程式功能改變。
- **Grid continuity 不隨 Theme 改變。**

---

## 8. 實機驗收清單

未來 UI Shell 或正式程式的 Table / ListView 至少應實測：

- 100% DPI。
- 125% DPI。
- 150% DPI。
- 視窗正常大小。
- 視窗最大化。
- 視窗縮放後。
- 垂直 scrollbar 尚未出現。
- 垂直 scrollbar 出現後。
- 空清單。
- 少量資料。
- 超過一頁的大量資料。
- Selected row。
- Header sorting state（若有）。

其中最重要的視覺驗收：

> **Header / Body 的欄位邊界應形成一條連續線；肉眼可見的 1 px 錯位視為 UI defect。**

---

## 9. 與新版 CY Visual Guide 的關係

未來主 Guide 的 Table / ListView 章節應把本附件視為：

**「已驗證實作參考」而不是「現行樣式參考」。**

也就是：

- 參考 CYInvoice **怎麼把表格做整齊**。
- 不照抄 CYInvoice **現在用了什麼顏色、字級、列高與欄寬**。

CYInvoice 自己之後若套用新版視覺，也應使用相同原則重新 Theme，而不是因為自己是 reference implementation 就被禁止更新外觀。
