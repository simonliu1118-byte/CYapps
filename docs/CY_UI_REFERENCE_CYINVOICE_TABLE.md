# CY UI Reference — CYInvoice Table / List Implementation

> [!CAUTION]
> **本文件是 `CY Desktop Visual Guide` 的參考附件，不是現行開發規範，也不是永久規則。**
>
> 本附件保留的是 CYInvoice 已開立發票清單目前已驗證良好的**結構、幾何與繪製做法**，供未來 CYApps 的 Table / ListView / DataGrid 設計參考。
>
> **目前 CYInvoice 的顏色、字體大小、列高、欄寬、padding、badge 色票與其他具體視覺數值，均不得視為未來標準。** 未來若採用新版 CY Desktop Visual Guide，這些值應依新版 Theme、Typography、Density、Surface 與 Table tokens 重新決定。

**參考來源：**

- `apps/CYInvoice/src/CYInvoice.WinForms/RecordsControl.cs`
- `apps/CYInvoice/src/CYInvoice.WinForms/NativeListViewHost.cs`

---

## 1. 為什麼保留這份參考

CYInvoice 目前的清單已具備良好的桌面商務資料表操作感，尤其是：

- 表頭與表身欄位幾何連續。
- 欄位分隔線可順暢由 Header 延伸至 Body。
- 視窗／viewport 改變時會重新計算欄寬。
- 垂直 scrollbar 與 DPI 對實際可用寬度的影響有納入考量。
- 固定欄與可伸縮欄並存，不是每一欄平均硬塞。
- Header 與 Body 都採相同 column geometry，而不是各自另外猜測欄位位置。
- Owner-draw 讓 Header、Body、狀態與 badge 能保持完整控制，同時保留原生 ListView 的 scrolling / selection 基礎。

本附件的目的，是避免未來重做 Table 時再次踩到「Header 和 Body 差 1 px」、「scrollbar 出現後最後一欄錯位」、「DPI 造成欄線漂移」等已處理過的問題。

---

## 2. 核心原則：Single Column Geometry Source

### 必須保留的概念

表頭與表身必須共享同一組欄位幾何資訊。

不要採：

- Header 自己算一次 X / Width。
- Body 再自己算一次 X / Width。
- Header 用比例、Body 用固定值。
- Header 畫右邊界、Body 又以不同座標重算右邊界。

只要兩邊計算路徑不同，DPI、縮放、scrollbar、border ownership 或 integer rounding 都可能造成 1 px 以上錯位。

### 驗收標準

若 Table 使用垂直欄線：

> **Header 與 Body 的每一條欄線必須形成肉眼連續的單一直線。任何可見的 1 px 欄位錯位都視為 UI 缺陷。**

若某 framework 無法低成本可靠做到垂直欄線，寧可採無垂直線設計，也不要留下錯位格線。

---

## 3. Viewport Width 必須以實際可畫區域計算

CYInvoice 的做法不是單純拿外層 Control.Width 當欄位總寬，而是取得 ListView 的實際 client / viewport 寬度，再考慮 DPI 與邊界誤差。

這個概念應保留：

1. 以實際資料 viewport 寬度為欄位總寬來源。
2. 垂直 scrollbar 出現時，不得讓 Header 與 Body 採不同的有效寬度。
3. DPI scaling 後應使用同一座標系統計算欄寬。
4. Border / client edge 的像素也要有一致 ownership。

具體補償值不應跨程式複製；不同 framework 應依自己的 client-area API 實測。

---

## 4. 欄寬策略：固定欄 + 主要伸縮欄

CYInvoice 目前使用「多個固定用途欄 + 一個主要可伸縮欄」的策略。這個策略值得保留，但目前任何欄寬數字都不是新版標準。

未來建議：

- 日期、發票號碼、金額、狀態等格式相對固定的欄，可設定合理基準與最小寬度。
- 姓名、名稱、備註、描述等內容長度變化大的欄，可指定為主要伸縮欄。
- 視窗變窄時，依預先定義的 shrink priority 逐步縮減，不要所有欄平均壓縮。
- 每個可縮欄要有合理 minimum width。
- 若已低於可讀下限，應接受水平捲動或調整資訊呈現，不要繼續把文字壓到無法辨識。

### 重要

`RecordsControl.cs` 目前的固定欄寬與 minimum width 只是 CYInvoice 現版需求下的數值。

未來新版 UI 應依：

- 新 Typography。
- 新 Table Density。
- 新字體大小。
- 欄位實際內容。
- 視窗 minimum size。
- DPI 測試結果。

重新量測，不得照抄現值。

---

## 5. Header / Body Grid Drawing

CYInvoice 現行 owner-draw 的重要經驗是：Header 與 Body 都在各自 cell bounds 的一致邊界位置畫線，而不是另外建立一層獨立 grid overlay。

未來若仍採 owner-draw：

- Header separator 與 Body separator 應使用相同的 column right-edge 定義。
- 橫線與直線的 border ownership 應一致，避免重複畫線造成 2 px，或左右 cell 各畫一條導致變粗。
- 畫線應依 framework 的實際 cell bounds，不另外估算。
- 最後一欄、scrollbar 前緣與 outer border 應單獨驗證。
- Resize / scroll 後應 invalidate / repaint 必要區域，避免舊格線殘留。

### 視覺值不是參考核心

目前 Header background、grid pen 顏色、文字 padding 等只是現版 CYInvoice 樣式。

未來應全部改由新版 Table tokens 決定，例如：

- `Table.HeaderBackground`
- `Table.Background`
- `Table.Grid`
- `Table.Selection`
- `Table.Text`
- `Table.HeaderText`
- `Table.CellPadding`
- `Table.RowHeight`
- `Table.HeaderHeight`

---

## 6. Row Height 與文字垂直觀感

現行 CYInvoice 使用原生 ListView 搭配小型 ImageList 控制 row height，這是 WinForms / ListView 的實作技巧，不是全 CYApps 必須採用的方式。

應保留的是結果要求：

- 行高與字級比例自然。
- 文字看起來接近垂直置中。
- 不為追求更高的 row 而留下大量上下空白。
- Header height 與 body row height 比例協調。

未來 row height 必須依新版 Typography / Density 重新驗證。

---

## 7. Selection、Zebra、Status 與 Badge

CYInvoice 現版包含 zebra row、狀態色、作廢表示、來源 badge、上傳狀態等視覺邏輯。

可參考的是：

- 不同狀態應能快速辨識。
- 狀態不能只靠顏色，仍應有文字／符號。
- 特殊狀態的繪製不得破壞 grid continuity。
- Badge 應存在於 cell bounds 內，不影響欄位幾何。

不應照抄的是：

- 現在的藍、黃、紅、綠 RGB 值。
- 現在的 badge radius。
- 現在的 badge font size。
- 現在的 zebra 色。

這些應由新版 Visual Guide 的 Theme / State / Badge tokens 決定。

---

## 8. Alignment

CYInvoice 的欄位對齊策略可保留作語意參考：

- 一般文字：左對齊。
- 真正數值／金額：右對齊。
- 短操作／狀態符號：視情況置中。
- 數字型 identifier 不因為只含數字就自動右對齊。

實作時 Header 與 Body 的同一欄應維持一致或語意合理的對齊關係。

---

## 9. Resize / DPI / Scroll 驗收清單

未來任何 CY Table / List 實作若採類似 grid，至少應實機確認：

- 初次開啟。
- 視窗最大化。
- 視窗還原。
- 寬度縮到 minimum size。
- 寬度放大。
- 無垂直 scrollbar。
- 有垂直 scrollbar。
- 滑鼠滾輪捲動後。
- 100% DPI。
- 125% DPI。
- 150% DPI。

每一種狀態都檢查：

1. Header / Body 欄線是否連續。
2. 最後一欄是否被 scrollbar 吃掉或留下不合理空隙。
3. 可伸縮欄是否吸收剩餘空間。
4. 文字是否被不合理裁切。
5. Grid 是否有 1 px / 2 px 粗細跳動。
6. Selection / status / badge 是否仍在正確 cell bounds 中。

---

## 10. 未來套新版 Visual Guide 時的優先順序

調整 CYInvoice 自身或把此模式移植到其他程式時，建議依下列順序：

1. **先保留 column geometry 與 resize / scrollbar correctness。**
2. 套新版 Typography / Density，重新決定 font / row / header height。
3. 套新版 Theme / Surface，重新決定 header、body、grid、selection 顏色。
4. 重新量測欄寬與 minimum width。
5. 調整 cell padding / badge / status visual。
6. 最後再做 100% / 125% / 150% DPI 與 resize 驗收。

不得先硬套舊欄寬，再用新字級去補救；欄寬必須隨新版 typography 重新量測。

---

## 11. 不應從 CYInvoice 現版複製成共同標準的內容

以下內容均視為「現況 implementation detail」，不是 Design Guide 標準：

- 目前 `Microsoft JhengHei UI` 的具體 pt 數值。
- 目前 ListView row height 數值。
- 目前 Header / grid / zebra / status 的 RGB 數值。
- 目前每一欄的固定 width。
- 目前各欄 minimum width。
- 目前 badge 的字級、padding、radius。
- 目前 owner-draw 是否一定是最佳 framework 解法。
- 目前 placeholder row 的具體做法。

其他 framework（Qt、Win32 / Go 等）應採用最穩定且原生友善的方法達到相同視覺結果，不要求重做 WinForms 的底層技巧。

---

## 12. 一句話原則

> **參考 CYInvoice 的「表格怎麼保持完整、精準、穩定」，不要照抄 CYInvoice 現在「每個 pixel 是多少、每個顏色是什麼」。**
