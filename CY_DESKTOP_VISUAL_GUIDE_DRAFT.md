# CY Desktop Visual Guide — Phase 1 Final Draft

> [!CAUTION]
> **本文件仍是設計／實作參考 Draft，不是現行治理規則。**
>
> 在使用者明確核准正式採用以前：
>
> - 現有 CYApps 專案的功能開發、修正、PR、CI、Build、Release 不得因本文件被阻擋。
> - 本文件不得視為 `REPOSITORY_RULES.md`、`REPO_POLICY.md` 或任何 `PROJECT_RULES.md` 的補充、替代或第四層規則。
> - 若本文件與現行治理文件、專案規則或使用者當次指示衝突，以較高優先級來源為準。
> - 本文件的目的，是降低未來 CY 桌面程式的視覺決策成本，而不是增加程式開發負擔。

**狀態：Phase 1 Final Draft / Discussion Reference**  
**Checkpoint：2026-09-24**  
**100% / 96 DPI Final Integrated Shell：使用者確認可接受**  
**125% / 150% DPI：Deferred validation；未人工驗證，不得描述成已通過**  
**Phase 2：Layout / Interaction / Workflow / Keyboard / IA**

---

# 0. 快速續作摘要

## 0.1 狀態標記

- **`APPROVED`**：Phase 1 已接受，不應無故重新推翻。
- **`APPROVED — ADVISORY`**：方向已接受，但屬建議，不應限制合理實作。
- **`APP CHOICE`**：應依 App / 畫面語意選擇；AI / 開發先提出建議，使用者可決定。
- **`REFERENCE`**：成熟實作或已驗證數值，可參考但不是跨 App 硬規定。
- **`DEFERRED`**：目前刻意不作封版阻擋，待實際 App 出現需求或問題再處理。
- **`PHASE 2`**：刻意留到 Layout / Interaction / UX 階段。
- **`HISTORICAL`**：保留量測或歷史資料，但不得再當未來設計標準。

## 0.2 Phase 1 總盤點

| 項目 | 狀態 | 結論 |
|---|---|---|
| 視覺定位 | `APPROVED` | Modern Business Desktop；乾淨、低裝飾、桌面優先，可保留中高資訊密度 |
| 規範強度 | `APPROVED` | Core / Recommended Range / App Choice，不以大量硬數值綁死各 App |
| 字體 | `APPROVED` | `Microsoft JhengHei UI`；fallback `Microsoft JhengHei → Segoe UI → system sans-serif` |
| Typography hierarchy | `APPROVED` | 統一角色與相對層級，不要求所有 App 使用完全相同 pt |
| WinForms Standard Input | `REFERENCE` | 96 DPI / 100% 已確認 10 pt + framework natural height 是良好一般參考 |
| Density | `APP CHOICE` | Compact / Standard / Comfortable 只是描述語言，不是跨 App 固定尺寸套餐 |
| Theme | `APPROVED` | Blue / Teal / Coral / Apricot；沒有 Warm Theme |
| Surface | `APPROVED` | Continuous / Sectioned / Carded；Card 只用在真正獨立工作單元 |
| Button | `APPROVED` | 一般按鈕 Native-first；Primary / Danger 有明確價值時可使用成熟 owner-paint |
| Input / Combo / Date | `APPROVED` | Native-first；不為視覺對齊強拉單行欄位高度 |
| Tabs | `APPROVED / APP CHOICE` | Header-only Custom 為較高視覺一致性的 preferred option；原生 Tab 仍是可接受替代方案 |
| Table / List | `APPROVED / APP CHOICE` | Native-first；Header 中性；Grid Continuity 為 MUST；Selection 依資料語意選擇 |
| Dialog / MessageBox | `APPROVED — ADVISORY` | MessageBox 能完整表達時直接使用，不另造 Custom Dialog |
| Accessibility baseline | `APPROVED` | Focus 不得真正消失；狀態不得只靠顏色；客製繪製不可破壞原生可及性 |
| Main Window Shell | `APPROVED — ADVISORY` | Minimal / Business / Workbench 是參考模式，不是模板 |
| Icon Family | `APPROVED DIRECTION` | 新版較方、較撐滿的 shared square-family；舊 INV 偏高窄 geometry 只保留 Historical Reference |
| Final Integrated Shell | `APPROVED @ 100%` | Button / Input / Tab / Table / Theme / MessageBox 整合後可接受 |
| 125% / 150% DPI | `DEFERRED` | 本輪不要求人工驗收；未來實際 App 出問題再修，不能宣稱已通過 |
| UX / Workflow | `PHASE 2` | Phase 1 不鎖死 |

## 0.3 接續原則

1. Phase 1 不再主動增加新的視覺元件規範。
2. 已 `APPROVED` 的角色／層級不因個別 1–2 px 差異反覆重談。
3. 實際 App 導入時，優先使用 framework 原生能力；有限 owner-paint 只在穩定且有明確視覺價值時使用。
4. 125% / 150% DPI 不再作為本輪封版門檻；實際導入 App 時若發現 clipping / alignment / grid continuity 問題再修。
5. Icon Family 的未來標準以 AITeam `shared/cy-visual/icon-family/` 的新 square-family 方向為準；舊 INV 量測只作 Historical Reference。
6. 下一階段是 Phase 2 Layout / Interaction / Workflow / Keyboard，而不是繼續擴張 Phase 1 規格。

---

# 1. 目的與定位 — `APPROVED`

CY 桌面程式跨 WinForms、Qt / PySide6、原生 Win32 / Go 等不同 framework。本 Guide 建立共同的**視覺骨架**，不追求 pixel-perfect identical。

共同目標：

- 一眼看起來屬於同一組 CY 桌面產品。
- 保留不同 App 依功能、資料量與 framework 調整的空間。
- 維持桌面商務軟體的效率、可讀性、穩定性。
- 原生控制項能安全達到目的時，不為細小外觀差異大量重寫。
- 規範用來降低設計決策成本，不得反過來增加不必要的程式風險。

視覺關鍵字：

- **Modern Business Desktop**：現代商務桌面，不做 Web Dashboard 套皮。
- **Clean but Dense**：乾淨，但允許中高資訊密度。
- **Low Decoration**：不靠大量陰影、漸層、彩色區塊堆出設計感。
- **Text-first**：文字標示優先，Icon 輔助。
- **Readable**：繁中、英文、數字、表格、輸入欄位優先清楚。
- **Framework-flexible**：角色與層級一致，比底層 control 完全一樣重要。

---

# 2. 規範強度

## 2.1 Core

跨 App 應維持的共同骨架，例如：

- Theme 角色。
- Danger 語意。
- Typography hierarchy。
- Surface hierarchy。
- Table Grid Continuity。
- Accessibility baseline。

## 2.2 Recommended Range

提供合理範圍，不用單一數字鎖死所有 App。

## 2.3 App Choice

可依 App / 畫面需要選擇：

- Density。
- Surface Model。
- Table selection mode。
- Row Hover。
- Native Tab 或 Header-only Custom。
- 是否需要 Theme Primary / Danger Button。
- Zebra Row / Card / Sidebar 等。

AI / 開發應依資料與操作語意提出建議，但不替使用者強制決定。

## 2.4 App Override

允許少量必要差異；Override 不是每支 App 重新建立一套設計系統。

---

# 3. Typography & Density

## 3.1 字體 — `APPROVED`

主要字體：

`Microsoft JhengHei UI`

Fallback：

`Microsoft JhengHei` → `Segoe UI` → system sans-serif

字重：

- Regular：Body / Input / Table body。
- Medium / Semibold：Section title / Table header / Active Tab / 一般重要控制。
- Bold：少量 Major Title / 特別重要摘要；不整個 UI 都 Bold。

## 3.2 Typography Roles — `APPROVED` hierarchy / `RECOMMENDED RANGE`

| Role | 建議範圍 |
|---|---:|
| Major / App Title | 16–20 pt |
| Page Title | 13.5–16 pt |
| Section Title | 11.5–13 pt |
| Body / Field | 9.5–11 pt |
| Secondary | 8.5–10 pt |
| Button | 9–11 pt |
| Table Header | 9–10.5 pt |
| Table Body | 9–10.5 pt |
| Badge / Status | 8.5–10 pt |

層級關係：

`Major Title > Page Title > Section Title > Body > Secondary`

Secondary 優先靠顏色／字重降低層級，不把文字縮到難讀。

### WinForms Standard Input — `REFERENCE`

Windows 96 DPI / 100% 實機驗證：

- `Microsoft JhengHei UI 10 pt` 中文可讀性良好。
- 原生 TextBox / ComboBox / DateTimePicker 不需為視覺整齊強制同一設定 Height。
- 應尊重 framework natural / preferred height，再由 layout 對齊視覺基準。
- 這是 WinForms 一般畫面的參考，不是所有 App / framework 的固定值。

## 3.3 Density — `APP CHOICE`

`Compact / Standard / Comfortable` 可以繼續用來描述資訊密度，但**不定義成跨 App 固定字級、TextBox 高度、Button 高度或 Row Height 套餐**。

核心原則：

- 高密度畫面自然使用較小字、較小欄位、較緊間距。
- 一般畫面使用 Standard 節奏。
- 低資訊量、Preview、結果頁可較寬鬆。
- 同一畫面不要無理由混用明顯不同密度。
- 不為了符合 Density 名稱而破壞原生 control 的自然高度。
- Dialog 可與主畫面採不同密度，只要自身一致。

## 3.4 DPI — `APPROVED PRINCIPLE / DEFERRED VALIDATION`

優先順序：

1. OS DPI awareness。
2. framework 原生 scaling。
3. layout 自動伸縮。
4. 最後才是必要 override。

不建立額外複雜 DPI 引擎，不重複乘 scaling factor。

應關注：

- TextBox / Button 不裁字。
- Label 不爆版。
- Tab Header 不擠壓或裁切。
- Table Header / Body geometry 仍連續。
- Dialog 不破版。
- Scrollbar 不破壞 Table / Layout geometry。

**目前驗證狀態：**

- 100% / 96 DPI：Final Integrated Shell 已由使用者確認可接受。
- 125% / 150%：本輪 Deferred；未人工驗證，未來實際 App 發現問題再修。

---

# 4. Color & Theme

## 4.1 Core Neutral Roles — `APPROVED`

| Token | 目前參考值 | 用途 |
|---|---|---|
| `Neutral.White` | `#FFFFFF` | Control / Table / Raised |
| `Neutral.Window` | `#F8FAFC` | Window background |
| `Neutral.Subtle` | `#F8FAFC` | Secondary surface |
| `Neutral.ReadOnly` | `#F1F3F5` | Read-only / Disabled surface |
| `Neutral.Border` | `#D1D5DB` | 一般 Border |
| `Neutral.Divider` | `#E5E8EC` | Section Divider |
| `Neutral.Grid` | `#DDE1E6` | Table Grid |
| `Text.Primary` | `#1F2937` | 主文字 |
| `Text.Secondary` | `#667085` | 次要文字 |
| `Text.Disabled` | `#98A2B3` | Disabled |
| `Text.OnAccent` | `#FFFFFF` | Accent 上文字 |

不要持續增加大量只差一點點的灰階。

## 4.2 Theme Set — `APPROVED`

第一版固定四種：**Blue / Teal / Coral / Apricot**。沒有 Warm Theme。

Theme 只改 Accent 家族，不重新建立 Typography / Neutral / Control system。

### Blue

| Token | Value |
|---|---|
| Accent | `#2563EB` |
| Hover | `#1D4ED8` |
| Pressed | `#1E40AF` |
| Soft | `#DBEAFE` |
| Focus | `#60A5FA` |
| Selection | `#DBEAFE` |

### Teal

| Token | Value |
|---|---|
| Accent | `#0D9488` |
| Hover | `#0D8076` |
| Pressed | `#0F766E` |
| Soft | `#D1F2EB` |
| Focus | `#2DD4BF` |
| Selection | `#CCFBF1` |

### Coral

珊瑚粉紅方向，需與 Apricot 橘系、Danger 正紅清楚分開。

| Token | Value |
|---|---|
| Accent | `#D4657B` |
| Hover | `#C4566E` |
| Pressed | `#AD485E` |
| Soft | `#FCE8ED` |
| Focus | `#E39BAC` |
| Selection | `#F7DCE3` |

### Apricot

| Token | Value |
|---|---|
| Accent | `#D8844A` |
| Hover | `#C3733F` |
| Pressed | `#AA6435` |
| Soft | `#FAECDD` |
| Focus | `#E0A178` |
| Selection | `#F6E3D1` |

## 4.3 State Colors

State 與 Theme 分離。

| State | 目前參考值 | Soft |
|---|---|---|
| Success | `#21825C` | `#EAF5F0` |
| Warning | `#A66B10` | `#FAF1E3` |
| Danger | `#B43737` | `#F8EAEA` |
| Info | `#356A9A` | `#EAF1F7` |

**Danger 永遠是 Danger，不得因 Coral Theme 改成 Coral。**

Accent 主要用在 Primary、Focus、Active Tab、Selection、小型 highlight；不要大量鋪滿 Header / Group / Window。

第一版 Light Mode first；Dark Mode 不在 Phase 1。

---

# 5. Surface

## 5.1 Surface Roles — `APPROVED`

- `Surface.Window`
- `Surface.Workspace`
- `Surface.Section`（可 transparent / inherit）
- `Surface.Control`
- `Surface.Raised`
- `Surface.Subtle`
- `Surface.Border`
- `Surface.Divider`

問題不是灰底或白底本身，而是層級是否一致。

## 5.2 Surface Models — `APPROVED`

### Continuous

適合高密度資料輸入／發票／帳務。Section 多半 inherit，Control / Table 可白底。

### Sectioned

適合一般桌面工具／左右工作區／Preview。主要工作區清楚分區，但內部不再層層 Card。

### Carded

只用在 Workflow / 批次 / 結果摘要等真正獨立工作單元，不作普通排版容器。

一般 Raised / Card 優先 White + 1 px Border，不預設陰影。

---

# 6. Spacing & Alignment

## 6.1 Spacing — `APPROVED`

優先 spacing scale：`4 / 8 / 12 / 16 / 24 / 32 px`。

不是 pixel lock；framework / DPI 可小幅近似。

常見節奏：

- 4：微距。
- 8：同組。
- 12：一般欄位。
- 16：小區塊。
- 24：主要 Section。
- 32：大區塊／頁面留白。

Window / Page margin 可依密度約 12–32 px；高密度通常 12–16，一般工具 16–24，Preview / 低密度 24–32。

## 6.2 Alignment — `APPROVED`

- 同一 Section 的 Label Column 一致。
- Label 左邊界一致；Input 左邊界一致。
- 同列控制項視覺高度／文字基準一致。
- Section 間距 > Section 內欄位間距。
- Resize 不破壞 alignment grid。
- 同類資料欄位維持合理一致邊界。
- Table Header / Body geometry 保持連續。

多欄表單可各自維持自己的 Label Column，不要求整頁共用超寬 Label 欄。

---

# 7. Core Components

## 7.1 Button — `APPROVED`

### 一般原則

- 一般 Secondary / 普通功能按鈕優先使用 OS / framework 原生 Button。
- 沒有特殊 Theme 色、Danger 強調或其他明確視覺效果需求時，原生 Button 是首選。
- 不為一般 1–2 px radius 差異重做 Button 行為。
- 不做一般 pill Button；pill 主要留給 Badge。
- 寬度 content-driven，以文字 + 合理 padding 決定，不為填滿視窗過度拉長。

### 常見尺寸參考

| 類型 | 高度 | 文字 |
|---|---:|---:|
| Compact | 30–32 px | 9–9.5 pt |
| Standard | 34–38 px | 9.5–10.5 pt |
| Large | 42–48 px | 10.5–12 pt |

Large 主要給主工作畫面的強 Primary Action，不是 Dialog Footer 常態。

Dialog 優先 Compact / 小 Standard；一般短按鈕自然寬度約 72–100 px，稍長文字約 100–120 px，均只作參考。

### Primary / Secondary / Danger

- Primary：可使用 Accent solid + White text；Hover / Pressed 使用 Theme 階層。
- Secondary：原生 Button 優先。
- Danger：Danger 與 Theme 分離；真正需要強調的不可逆操作可使用 Danger Button。
- Native Secondary + Theme Primary / Danger 可同組混排，前提是高度、字體、對齊與視覺層級協調。
- 不因為可以 owner-paint 就把所有 Button 全部 Theme 化。

### WinForms Theme Button — `REFERENCE`

已驗證方案：原生 `Button` subclass，只接管外觀繪製。

- V7 參考使用固定約 2 px radius，不隨高度放大。
- Hover / Pressed 只改顏色，不改 geometry。
- Fill / Border 共用 rounded path。
- 保留 Click / keyboard / focus / tab / accessibility 等原生行為。
- 若 owner-paint 在 DPI / 邊角上不穩定，直接退回原生 Button。

詳見：`docs/CY_UI_REFERENCE_WINFORMS_THEME_BUTTON.md`

**這些數值是 WinForms Reference，不是 Qt / Win32 / Go 的硬規定。**

## 7.2 TextBox / Numeric / Label — `APPROVED`

- 單行 TextBox 不為追求統一框高強制拉高。
- 原生 control 若無法真正垂直置中文字，就使用自然高度，不包自繪外框硬補。
- 同列 TextBox / ComboBox / DatePicker 以**實際可見高度與文字基準**對齊，不只看設定 Height。
- Disabled / Read-only 可使用淡灰背景；內容仍需清楚可讀。
- Focus 不改 geometry。
- Error：Danger border + 鄰近短訊息；不整格紅、不 glow、不造成 layout jump。
- Placeholder 不取代 Label。

Label 預設左側 + Control 右側；Label 本身左對齊。短中文可用全形空白分散，例如：

```text
姓　　名 [____________]
地　　址 [____________]
聯絡電話 [____________]
```

Numeric：

- 金額／數量／單價／百分比預設右對齊。
- Numeric spinner 預設不顯示；做不到時可用 TextBox + validation。
- 郵遞區號、統編、發票號碼等 identifier 不因為是數字就強制右對齊。

## 7.3 ComboBox / DatePicker — `APPROVED`

- 與 TextBox 同視覺家族與同列對齊基準。
- 原生 dropdown arrow / calendar popup 保留。
- 不為 radius / arrow 另外包一層自繪殼。
- 以 framework natural height / 字體與 layout 做視覺對齊。

## 7.4 Tabs — `APPROVED / APP CHOICE`

共通視覺：

- 低存在感。
- Active：稍強字重；Custom 方案可搭 Accent 約 2 px underline。
- Inactive：Neutral text。
- Standard / Large 尺寸皆可，依畫面層級選擇。
- 不做大型 pill / hero navigation。

### Preferred：Header-only Custom

適合需要較高 CY 視覺一致性的畫面。

- **只自訂上方 Header。**
- 下方內容仍使用原本 `TabControl / TabPage` 或既有 Page / UserControl。
- 不重畫整個頁面內容。
- 可避開 WinForms 原生 selected-tab bevel / shadow / geometry jump。
- 保留原本 page lifetime、control parenting、focus routing 與切頁狀態。
- 鍵盤切頁能力應保留，例如 `Ctrl+Tab` / `Ctrl+Shift+Tab`。

### Alternative：Native Tab

原生 Tab **可以直接使用**，不因有傳統框線或陰影就判定不合格。

唯一共同注意：

> 不顯示 Windows 傳統黑色虛線 focus rectangle。

但這不代表 Focus 可以消失。若拿掉虛線框，仍須用其他方式讓鍵盤焦點可辨識，例如 soft focus surface、文字狀態或其他低存在感提示。

### 不採用方向

WinForms `FlatButtons` 外觀因過度像舊式按鈕列，已淘汰，不作 CY Tab 方案。

## 7.5 Section / Group / Divider / Card — `APPROVED`

- Section 預設 transparent / inherit。
- Section Title 約 11.5–13 pt、Semibold、Text.Primary。
- Divider 約 1 px、低對比。
- 穩定 GroupBox 可保留，不強迫重寫。
- Card 只用於 Preview / Result / Drop Area / 獨立 Workflow Unit。

---

# 8. Table / ListView / DataGrid — `APPROVED / APP CHOICE`

Native-first。WinForms `DataGridView` 已證明能達成 Phase 1 需求，不需要為整張表全面自繪。

## 8.1 Density

Row Height 依 App 資料密度自行決定；下列只作常見參考：

| 類型 | Row Height 參考 |
|---|---:|
| 高密度 | 約 26–29 px |
| 一般 | 約 30–34 px |
| 寬鬆 | 約 35–40 px |

## 8.2 Visual

- Header：淺 Neutral / Subtle，Semibold。
- Header 一般保持**中性**，不因 current cell / selected cell 把整個 column header 染成深 Accent。
- Sorting 可用小箭頭或其他 subtle state 表示，不靠大面積 Accent fill。
- Body：通常 White。
- Zebra：可選，非常淡。
- Grid：低對比。
- Selection：Accent.Selection soft tint + 深文字。

## 8.3 Alignment

- Text / name / address：左。
- Money / numeric value：右。
- 短 status：可中。
- Identifier 依內容決定。

## 8.4 Selection 行為 — `APP CHOICE`

Selection 應依資料與操作語意決定，不寫死一種模式。

- **Record List**：通常建議 Full Row Select，因為一列代表一筆記錄。
- **Editable / Cell Grid**：通常建議 Cell Select，因為使用者操作的是單一儲存格。
- **Row Hover**：Optional。可在需要強化列追蹤時使用，但不是跨 App 必備。

WinForms `DataGridView` 沒有直接提供「整列 hover style」的單一屬性；若要做需要額外事件／樣式處理，因此不應為視覺一致性強制所有 App 實作。

AI / 開發應先依 use case 推薦，最後由使用者決定。

## 8.5 Grid Continuity — MUST

Header / Body 必須共享同一組 column geometry；肉眼可見約 1 px 欄線錯位視為 UI defect。

應考慮：

- resize。
- maximize / restore。
- scrollbar 出現／消失。
- control border / viewport width。
- DPI scaling。
- owner-draw border ownership。

如果 framework 無法可靠維持垂直線對齊，**寧可取消垂直線，也不要保留錯位格線**。

CYInvoice 成熟實作參考：`docs/CY_UI_REFERENCE_CYINVOICE_TABLE.md`

現行 CYInvoice 顏色、字級、row height、欄寬、padding 不是新版固定標準。

---

# 9. Secondary Controls & Supporting States

## Checkbox / Radio

Native-first；Body font；視覺垂直對齊；不做 oversized Web-style controls。

## Toggle

只用於真正 immediate On / Off；沒有成熟 Toggle 時直接使用 Checkbox。

## Progress / Loading

有明確進度使用 Progress；時間不確定使用 native spinner / marquee。文字放進度條外。

## Scrollbar

Native-first，不客製；但必須把 scrollbar 對 layout / table geometry 的影響算進去。

## Menu / Context Menu — `APPROVED — ADVISORY`

- Native-first。
- 不為統一全面自繪 Windows ContextMenu / MenuStrip。
- Hover 低存在感。
- Danger item 可用 Danger text，但不預設整條紅底。
- Shortcut / submenu arrow 優先交給原生。

## TreeView / Sidebar / Navigation List — `APPROVED — ADVISORY`

- 不預設深色 Sidebar。
- Active item 可使用 Accent.Soft + 較強文字／細 indicator。
- 不把每個 item 包成 pill 或 Card。
- Tree indentation / expand arrow 原生優先。
- Navigation 架構留 Phase 2。

## Splitter / Pane Divider

視覺線保持細；hit area 可比線寬；不做粗灰 bar。

## Empty / No Data / Drop Area

- 簡潔文字與小圖示。
- 不做巨大插畫。
- Drop Area 可使用淡 dashed border；drag hover 才使用 Accent.Soft。

## Toolbar / ToolStrip

沿用 Button / Icon / Divider 系統；Native-first；功能擺放與排序留 Phase 2。

---

# 10. Accessibility Baseline — `APPROVED`

第一版不建立大型 Accessibility Design System，但以下是底線：

- Keyboard focus 不得真正消失。
- 若拿掉傳統 focus rectangle，必須有其他可辨識的鍵盤 focus state。
- Status / Error / Success 不得只靠顏色傳達。
- Disabled / Secondary 文字仍需可讀。
- 不使用低到難辨識的文字對比。
- 客製繪製不可刻意破壞原生 High Contrast / keyboard / accessibility 行為。

---

# 11. Dialog / MessageBox / Status — `APPROVED — ADVISORY`

## 11.1 Dialog Shell

- Native title bar / window behavior 優先。
- CY 統一內容區 typography / surface / input / button。
- 不為漂亮全面改 borderless / custom chrome。

## 11.2 Native MessageBox

**能由原生 MessageBox 完整表達的簡單訊息／警告／OK／Yes-No／Question／覆蓋確認，直接使用原生 MessageBox。**

- 按鈕排列跟隨原生。
- Window size、DPI、Enter / Esc、Focus 等交給 framework / OS。
- 不為套 Theme 色把單純 MessageBox 改成 Custom Form。

## 11.3 Custom Dialog 使用時機

只有 MessageBox 無法合理承載時才使用，例如：

- 表單。
- 密碼輸入。
- 設定。
- Preview。
- 多項選擇。
- 大量詳細資訊。
- 技術細節展開。
- Wizard。

## 11.4 Dialog Button Position

不寫死。右下 Action Group 可作商務桌面預設參考，但小型 Dialog、內容重心或 App 設計若更適合置中，可合理調整。

固定的是：

- 不過度拉寬。
- 同組間距一致。
- Primary / Secondary / Danger 語意清楚。

## 11.5 Status / Validation

- Banner：Soft state background + 相關文字／icon；不使用整片高飽和紅／綠。
- Badge：可使用 pill；小型、低彩度；不是 Button。
- Inline Validation：Danger border + 鄰近短訊息；不可造成 layout jump。

---

# 12. Main Window Shell — `APPROVED — ADVISORY`

- Native Windows title bar 優先。
- Title Bar 顯示 App Icon + App Name 即可，不塞大量公司／版本／環境資訊。
- Internal Header 是 Optional；只有持續資訊價值才使用。
- Header 不重複 App Name。
- Header 優先 White / Neutral / Accent.Soft，不預設整條深色 Accent。
- Footer / Status Bar 只有真正有持續狀態資訊才使用。
- 避免 Title Bar + Header + Toolbar + Tabs + Page Title + Section Title 全部堆在一起。

三種參考模式：

- **Minimal**：Title Bar + Content。
- **Business**：Title Bar + Tabs / Toolbar + Content + optional Status。
- **Workbench**：Title Bar + optional compact Header / Toolbar + Main Workspace + optional Status。

這些是參考模式，不是模板。

---

# 13. App Icon Family — `APPROVED DIRECTION / SHARED REFERENCE`

Desktop Visual Guide 只保留 Icon Family 的**方向與邊界**。正式 icon-family 細節放在 AITeam 的 shared visual package，不在本 Guide 維護第二套標準。

Canonical source under preparation：

`simonliu1118-byte/AITeam/shared/cy-visual/icon-family/`

目前推進工作：AITeam PR #60；在該 PR 合併以前，不應宣稱 AITeam `main` 已正式存在 canonical package。

後續不預設把整套 family 文件 mirror 到 CYapps / CYapps_pvt。完整 Icon Family 定案後，由各 App 負責 AI 只帶回該 App 的正式 icon assets。

## 13.1 新 Square-family 方向

已核准方向是**新版較方、較撐滿的 family master**：

- square canvas。
- visually square rounded-card body。
- white interior。
- 一個主要 accent color。
- 一致的 rounded-square outer frame / visual mass / hierarchy。
- 上方大型 identifier。
- 下方一個簡單 app-specific motif。
- flat / high contrast / modern business desktop。
- 不使用 3D、gloss、heavy shadow、複雜插畫。

## 13.2 Identifier

預設使用短縮寫大寫：

- INV — CYInvoice
- ACC — CYAccounting
- ENV — CYEnvelope
- WTM — CYWatermark
- CVT — SMARTCOPIConverter
- CAL — TriINVCalc

辨識性明顯不足時，可個案使用更直觀字樣。目前核准例外：

- `Auto` — CYERPAutoInput

例外只改 identifier 策略，不代表可以改 family geometry。

## 13.3 Lower Motif

下方 motif 是 **App-specific**。

- INV：invoice / detail lines。
- ACC：accounting / chart / money。
- ENV：envelope。
- Auto：automated input / document + direction。
- WTM：watermark / marked document。
- CVT：conversion / document transform。
- CAL：calculator。

**INV 的兩條橫線不是全家族共同元素。**

## 13.4 Historical INV Geometry — `HISTORICAL`

舊 CYInvoice `INV` icon 是家族發展的歷史視覺祖先，先前曾量測其 Live Area 約：

- Canvas width 80–83%。
- Canvas height 86–88%。
- 48 px layer 約 39×42 px。

這些數值現在只作**Historical / Source Measurement**：

> **不得再把舊 INV 偏高窄 silhouette 當未來 family master 或新 icon 的 geometry target。**

未來正式 family consistency 以新版 square/full vector master 為準。若日後 CYInvoice 本身也遷移新家族，INV 亦應改用同一新版 square master。

舊量測文件 `docs/CY_UI_REFERENCE_ICON_FAMILY_GEOMETRY.md` 應視為 Historical Reference，不是 future family rule。

## 13.5 Production Workflow

生成式 AI 只用於概念探索，不能保證每次生成完全相同的 radius / frame / live area / identifier scale。

正式 production 應：

1. 用 AI / 草圖探索概念。
2. Side-by-side 比較家族視覺量體。
3. 由共用 editable vector family master 重建／清理。
4. 產出 `16 / 24 / 32 / 48 / 64 / 128 / 256` 等層級。
5. 小尺寸逐層檢查，可作 optical adjustment。
6. 再輸出正式 PNG / ICO。

目前尚未完成：

- precision vector master。
- 各 App 最終 accent HEX。
- 全部正式 SVG / PNG / ICO migration。

這些不阻擋 Desktop Visual Guide Phase 1。

---

# 14. Cross-framework / Complexity Guardrails — `APPROVED`

- 角色一致 > 底層 control 完全一樣。
- Native control 足夠接近時不重寫。
- 不因 radius、arrow、scrollbar、一般 1–2 px 細節引入不穩定自繪。
- 有成熟、經真機驗證且具明確視覺價值的有限 owner-paint 可以使用，例如 Theme Primary / Danger Button 或 Header-only Custom Tab。
- owner-paint 應盡量只接管外觀，不重寫原生 control 行為。
- DPI / font rendering / OS theme 的合理差異可接受。
- visual token 成本過高可採合理近似。

Complexity Guardrails：

1. 不因 Guide 強迫更換 framework。
2. 不因 Guide 重寫穩定原生控制項。
3. 不為一般 1–2 px 差異阻擋功能交付；**Table Grid Continuity 是例外，明顯錯位仍是 defect。**
4. 不要求每個 App 實作所有 Theme。
5. 不要求每個 App 實作所有 Surface Model。
6. 不要求所有元件都建立專用 component library。
7. 先在代表 App / Shell 驗證再擴散。
8. 優先規範角色、比例、視覺結果，不把所有 x/y/padding 寫成硬限制。
9. App Override 是必要差異，不是重新設計。
10. Phase 1 完成後，不因「可能還用得到」繼續擴張元件清單。

---

# 15. Theme / Surface Example — `REFERENCE`

僅示意，不是 App 正式配置：

| App | Theme 示例 | Surface 示例 |
|---|---|---|
| CYInvoice | Blue | Continuous |
| CYAccounting | Blue / Teal | Continuous / Sectioned |
| CYEnvelope | Coral / Apricot | Sectioned |
| TriINVCalc | Teal | Continuous / Sectioned |
| CYWatermark | Blue / Teal | Carded / Sectioned |

---

# 16. UI Shell Validation

## 16.1 已完成的 Phase 1 Lab / Shell 結論

- Button：V7 Theme Button 可接受；一般 Button 仍 Native-first。
- Input：WinForms 10 pt + natural height 可接受；不硬拉單行欄位高度。
- Theme：Blue / Teal / Coral / Apricot 已同畫面確認。
- Dialog：簡單訊息改用原生 MessageBox。
- Tab：Header-only Custom 較理想；Native Tab 仍可用；FlatButtons 方案淘汰。
- Table：Native DataGridView 可達成需求；Header 保持中性；Selection 依 use case 選擇。
- Final Integrated Shell：100% / 96 DPI 使用者確認「應該沒什麼問題」。

## 16.2 Final Integrated Shell 的角色

Final Shell 是無引擎驗證面：

- UI 是真的。
- 資料是假的。
- 不連正式 API / DB。
- 不正式開立發票／列印／修改資料。

它證明 Phase 1 視覺語言可以在實際 WinForms 商務畫面整合，而不是只存在於紙面規格。

## 16.3 DPI 狀態

- 100%：已人工確認。
- 125% / 150%：Deferred validation。

Deferred 不等於通過。日後若實際 App 在高 DPI 出現問題，應修實際 App / 共用實作；不需要為了補測而阻擋本輪 Phase 1 文件收斂。

---

# 17. Phase Boundary

## 17.1 Phase 1 — `COMPLETE AS DRAFT`

已處理：

- Color / Theme。
- Surface。
- Typography / Density。
- Spacing / Alignment。
- Button / Input / Combo / Date。
- Tabs。
- Table / List。
- Secondary Controls。
- Dialog / MessageBox / Status。
- Shell visual pattern。
- Icon Family direction / governance boundary。
- 100% WinForms integrated validation。

Phase 1 不再主動擴張新的紙面視覺項目。

## 17.2 Phase 2 — `PHASE 2`

刻意不在 Phase 1 寫死：

- 功能流程。
- 控制項最終放左／右／上／下。
- Primary Action 最終位置。
- Toolbar button 順序。
- Tab 順序與頁面資訊架構。
- Enter / Esc / Tab 鍵行為。
- Dialog 何時出現。
- 表單欄位順序。
- Search / Query 流程。
- Workflow step 數量。
- 快捷鍵。
- 大型 Navigation / IA 重構。

Dialog Button 靠右／置中也屬可依個案調整的 Layout 決策，本 Guide 只提供常見建議，不做硬限制。

---

# 18. 正式化方向

Desktop Visual Guide 未來若由使用者明確核准正式採用，canonical source 建議放在：

`simonliu1118-byte/AITeam/shared/cy-visual/desktop/`

原則：

- 與 Governance version 分離。
- 不形成第四層永久治理。
- AITeam 保存 shared design source。
- 不預設把完整文件 mirror 到每個下游 repo；只有實際開發／維護需要時才建立必要的下游副本或 App-specific assets。

**在使用者明確要求以前，不合併 PR #61，也不把本文件升格成現行開發門檻。**

---

# 19. 恢復工作時最短指引

1. 先確認本文件仍是 Draft / design reference，而非治理規則。
2. Phase 1 已收斂，不再主動擴張新視覺規格。
3. Theme 第一版固定 **Blue / Teal / Coral / Apricot**，不要恢復 Warm Theme。
4. 一般互動 control Native-first；有限 owner-paint 只在成熟、穩定且有明確價值時使用。
5. WinForms 一般 Input 可參考 10 pt + natural height，但不可變成跨 App 固定尺寸。
6. Tab：Header-only Custom preferred；Native Tab 可接受；不要傳統黑色虛線 focus rectangle，但 Focus 仍需可辨識。
7. Table：Native-first；Header 中性；Grid Continuity 為 MUST；Selection / Row Hover 是 App Choice。
8. Dialog：MessageBox 足夠時直接使用 MessageBox。
9. Icon Family：新 square/full master 是未來方向；舊 INV geometry 只作 Historical Reference；正式細節應讀 AITeam `shared/cy-visual/icon-family/`。
10. 100% Final Shell 已確認；125% / 150% 是 Deferred，不可寫成已驗證。
11. 下一個設計階段是 Phase 2 Layout / Interaction / Workflow / Keyboard。
12. 不經使用者明確指示，不 merge Draft / design PR，也不要求既有 App 立即重製 UI。
