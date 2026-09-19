# CY Desktop Visual Guide — Draft

> [!CAUTION]
> **本文件仍在討論與調整中，尚未定案，也不是目前有效的開發規範。**
>
> 在使用者明確核准本文件成為正式標準以前：
>
> - 現有 CYApps 專案在開發、修正、測試、PR、CI、Build 或 Release 時，**可以完全忽略本文件**。
> - 不得因本文件而阻擋、延後或要求重做既有專案工作。
> - 不得把本文件視為 `REPOSITORY_RULES.md`、`REPO_POLICY.md` 或任何 `PROJECT_RULES.md` 的補充、替代或第四層規則。
> - 本文件目前只用來整理與試作未來可能採用的 CY 桌面程式視覺語言。
> - 若本文件與現行治理文件、專案規則或使用者當次指示衝突，**一律以現行有效規則與使用者當次指示為準**。

**狀態：Draft / Discussion Only**  
**目前階段：Phase 1 — Visual Foundation**  
**不在本階段：功能流程、控制項擺放位置、快捷鍵、操作步驟、資料流程與 UX 重構**

---

## 1. 目的

CYApps 目前跨越 WinForms、Qt、Win32／Go 等不同技術世代與 UI framework。各程式功能取向不同，因此不適合強制所有畫面使用完全相同的版型、字級或控制項尺寸。

本草案的目標是建立一套共同的**視覺骨架**：

- 一眼看起來屬於同一組 CY 桌面產品。
- 保留不同程式依功能、資訊密度與使用情境調整的空間。
- 維持商務桌面軟體的效率與可讀性，不為了追求 Web／App 風格而犧牲操作感。
- 優先使用各 framework 穩定、容易維護的實作方式，不為追求 1–2 px 的完全一致而大量客製控制項。
- 視覺規範應降低日後設計決策成本，而不是增加程式製作難度。

---

## 2. 視覺定位

CY 桌面程式的共同方向暫定為：

- **Modern Business Desktop**：現代商務桌面軟體，而不是 Web Dashboard。
- **Clean but Dense**：乾淨，但允許商務工具需要的中高資訊密度。
- **Low Saturation**：低彩度、穩定，不使用大量鮮豔色塊。
- **Text-first**：文字標示優先；圖示作輔助，不依賴 icon-only 操作。
- **Readable**：優先確保繁體中文、數字、表格與輸入欄位的清楚閱讀。
- **Framework-flexible**：不同 framework 可以有細節差異，只要視覺角色與層級一致。

本草案不追求所有程式 pixel-perfect identical；目標是建立**同一家族的視覺語言**。

---

## 3. 規範強度概念

未來若本 Guide 定案，建議把項目分成三層，而不是把所有數字都寫成硬限制。

### 3.1 Core

預期各程式都應維持的共同視覺骨架，例如：

- 字體家族方向。
- 色彩角色命名與用途。
- Primary / Danger 的語意不可混用。
- 文字層級必須清楚。
- 同一程式內同類控制項應維持一致。

### 3.2 Recommended Range

提供基準值與合理範圍，程式可依需要調整，例如：

- Body 10.5 pt，可在 9.5–11.5 pt 調整。
- 一般控制項高 35 px，可在 32–38 px 調整。
- Section Title 12 pt，可在 11–13.5 pt 調整。

### 3.3 App Choice

依程式性質自由選擇，例如：

- Blue / Warm / Teal Theme。
- Continuous / Sectioned / Carded Surface Model。
- 表格是否使用 zebra row。
- 是否需要較大的標題或較緊湊的資料列。

---

# Part A — Typography

## 4. 字體家族

### 4.1 主要字體

候選標準：

`Microsoft JhengHei UI`

建議 fallback：

`Microsoft JhengHei` → `Segoe UI` → system sans-serif

原因：

- Windows 繁體中文顯示穩定。
- CYApps 現有多支程式已使用。
- 商務桌面程式閱讀性佳。
- 數字、中文與英文混排相對穩定。

### 4.2 字重

優先使用：

- Regular：一般內容。
- Semibold / Bold：標題、表頭、Primary Action。

避免大量使用超粗字重；重要層級主要透過**字級、字重與留白**建立，不依賴不同顏色堆疊。

---

## 5. 字級角色

以下數字是**候選基準與範圍，不是固定值**。

| Role | 建議基準 | 建議範圍 | 用途 |
|---|---:|---:|---|
| App Title | 17 pt | 16–20 pt | 視窗／產品主標題 |
| Page / Major Title | 14.5 pt | 13.5–16 pt | 頁面或主要功能標題 |
| Section Title | 12 pt | 11–13.5 pt | 功能區塊標題 |
| Body / Field | 10.5 pt | 9.5–11.5 pt | 一般內容、欄位文字 |
| Secondary | 9 pt | 8.5–10 pt | 說明、輔助資訊 |
| Button | 10 pt | 9–11 pt | 一般按鈕 |
| Table Header | 9.5 pt | 9–11 pt | 表頭，通常使用 Semibold/Bold |
| Table Body | 9.5 pt | 9–11 pt | 表格內容 |
| Badge / Small Status | 9 pt | 8.5–10 pt | 小型狀態標記 |

### 5.1 調整原則

各程式可以調整實際字級，但應保留相對層級：

`App Title > Page Title > Section Title > Body > Secondary`

例如高密度的發票／帳務程式可以縮小部分表格字級；預覽型工具可以放大 Body。**不要求不同程式使用完全相同 pt 數值。**

---

# Part B — Color System

## 6. Core Neutral Palette

中性色應盡量跨 Theme 共用，避免每個 Theme 變成另一套 Design System。

以下為第一版候選：

| Token | Candidate | 用途 |
|---|---|---|
| `Neutral.White` | `#FFFFFF` | 控制項、表格、必要白色 surface |
| `Neutral.Window` | `#F5F7FA` | 預設視窗／Canvas 背景 |
| `Neutral.Subtle` | `#F8FAFC` | 次要區、read-only、摘要底 |
| `Neutral.Border` | `#DCE0E6` | 一般邊框 |
| `Neutral.Divider` | `#E4E7EC` | 分隔線 |
| `Text.Primary` | `#1F2937` | 主文字 |
| `Text.Secondary` | `#667085` | 次要文字 |
| `Text.Disabled` | `#98A2B3` | 停用文字 |
| `State.Success` | `#21825C` | 成功 |
| `State.Warning` | `#A66B10` | 警告 |
| `State.Danger` | `#B43737` | 危險／破壞性操作 |
| `State.Info` | `#356A9A` | 一般資訊 |

這些值仍屬草案；正式採用前應在實際 Windows 顯示與 DPI 下目視確認。

---

## 7. Theme Palette

Theme 主要改變**Accent 家族**，而不是把整個 UI 的中性色、字體、尺寸全部重做。

每個 Theme 最少包含：

- `Accent`
- `Accent.Hover`
- `Accent.Pressed`
- `Accent.Soft`
- `Accent.Focus`
- `Accent.Selection`

### 7.1 CY Blue — 標準商務

適合：發票、帳務、ERP、一般商務工具。

| Token | Candidate |
|---|---|
| `Accent` | `#2E4A71` |
| `Accent.Hover` | `#273F61` |
| `Accent.Pressed` | `#203550` |
| `Accent.Soft` | `#E8EEF5` |
| `Accent.Focus` | `#6F8FB8` |
| `Accent.Selection` | `#E0EAF5` |

視覺感受：穩定、正式、可靠。

### 7.2 CY Warm — 暖灰／米棕商務

適合：文件、列印、信封、辦公型工具。

| Token | Candidate |
|---|---|
| `Accent` | `#765746` |
| `Accent.Hover` | `#614638` |
| `Accent.Pressed` | `#503A2F` |
| `Accent.Soft` | `#F3EAE4` |
| `Accent.Focus` | `#A98168` |
| `Accent.Selection` | `#EEE0D7` |

視覺感受：溫和、實體辦公、紙張感，但避免復古或泛黃。

### 7.3 CY Teal — 青綠工具型

適合：計算、資料轉換、技術工具。

| Token | Candidate |
|---|---|
| `Accent` | `#2F6F73` |
| `Accent.Hover` | `#275E61` |
| `Accent.Pressed` | `#204E51` |
| `Accent.Soft` | `#E5F1F0` |
| `Accent.Focus` | `#69A1A3` |
| `Accent.Selection` | `#DCEDEB` |

視覺感受：精準、工具性、科技但不冰冷。

### 7.4 暫不完整定義的候選 Theme

保留未來需要時再建立，不在第一版先增加維護成本：

- **CY Slate**：石墨／鋼灰，偏系統管理與工程工具。
- **CY Sage**：柔和灰綠，偏流程管理與 operational 工具。

原則：**沒有實際程式需要，就不增加 Theme。**

---

# Part C — Color & Surface

## 8. Surface Role

畫面是否使用白底、灰底、次級底色或透明繼承，應由 Surface Role 決定，而不是規定「功能區一定白底」。

| Token | 角色 |
|---|---|
| `Surface.Window` | 整個視窗／Canvas 背景 |
| `Surface.Workspace` | 主要工作區背景 |
| `Surface.Section` | 功能區塊背景，可為 transparent / inherit |
| `Surface.Control` | TextBox、ComboBox、Table 等可操作控制項表面 |
| `Surface.Raised` | 真正需要凸顯的結果、預覽、獨立 Card |
| `Surface.Subtle` | 摘要、read-only、次要區塊 |
| `Surface.Border` | 區塊／控制項邊界 |
| `Surface.Divider` | 內容分隔線 |

### 8.1 關鍵原則

`Surface.Section` **允許 transparent / inherit**。

因此像高密度商務程式可以直接讓各功能區使用視窗灰底，再以標題、分隔線、控制項白底建立層級；不必把每一區包成白色卡片。

問題不在「灰底」或「白底」本身，而在於同一畫面的 Surface 是否有一致且可理解的層級。

---

## 9. Surface Model

同一套 Theme 可搭配不同 Surface Model。

### 9.1 Continuous

適合高密度資料輸入、發票、帳務。

候選配置：

- Window：Neutral.Window。
- Workspace：inherit。
- Section：transparent / inherit。
- Control：White。
- Table：White。
- Raised：White 或 Subtle。

特點：畫面連續、不碎裂；靠標題、Divider、控制項與間距區分功能。

### 9.2 Sectioned

適合一般桌面工具、左右工作區、預覽型程式。

候選配置：

- Window：Neutral.Window。
- Workspace：White 或非常淺的 neutral surface。
- Section：transparent / inherit 為主。
- 少量獨立區塊使用 Raised / Subtle。

特點：有明確主工作區，但避免每一小塊都卡片化。

### 9.3 Carded

適合流程型、批次工具、結果摘要。

候選配置：

- Window：Neutral.Window。
- Workspace：inherit。
- 主要獨立工作單元：White card。
- Card 內次要摘要：Subtle。

特點：較現代、資訊階層清楚。

注意：Card 應代表真正獨立的工作單元，不應只是為了排版而增加白色框。避免 Card → Card → Card 的多層巢狀。

---

# Part D — Geometry & Spacing

## 10. Spacing Scale

建議採少量固定級距，避免每個畫面自行發明 7 px、13 px、19 px 等間距。

候選 scale：

`4 / 8 / 12 / 16 / 24 / 32 px`

典型用途：

- 4：非常緊密的文字／圖示微距。
- 8：同組元件。
- 12：一般欄位間距。
- 16：小區塊。
- 24：主要區塊。
- 32：大區塊／頁面留白。

這是視覺節奏參考，不要求所有 framework 精確使用相同 pixel。

---

## 11. Control Size

| 類型 | 建議基準 | 建議範圍 |
|---|---:|---:|
| Compact Control | 31 px | 30–33 px |
| Standard Control | 35 px | 33–38 px |
| Large / Primary Action | 42 px | 38–48 px |
| Table Row | 29 px | 26–34 px |
| Table Header | 32 px | 30–36 px |

程式可依資料密度與主要使用者需求選擇 Compact / Standard / Large，不要求所有程式一樣高。

---

## 12. Border / Radius

候選方向：

- 一般 Border：`1 px`。
- 一般控制項 Radius：`4–6 px`。
- Raised Surface / Card Radius：`6–8 px`。
- 一般按鈕不使用大型膠囊造型。
- Pill shape 主要保留給 Badge / Status Chip。
- 陰影只在真的需要浮層關係時使用；一般工作區優先依靠背景、Border 與留白建立層次。

跨 framework 若無法低成本做到相同 Radius，可接受接近值；不應為此引入高風險自繪控制項。

---

# Part E — Core Components

## 13. Button

第一版只建立四種視覺角色：

### 13.1 Primary

用途：目前區域最重要的主要動作。

候選外觀：

- Background：Theme `Accent`。
- Hover：`Accent.Hover`。
- Pressed：`Accent.Pressed`。
- Text：White。
- Border：Accent 或無額外視覺邊界。
- Font：Button role，通常 Semibold / Bold。

### 13.2 Secondary

用途：一般操作。

候選外觀：

- Background：White 或 Surface.Control。
- Border：Neutral.Border。
- Text：Text.Primary。
- Hover：Neutral.Subtle 或非常淡的 Theme Soft。

### 13.3 Danger

只用於刪除、作廢、不可逆破壞性操作。

候選外觀：

- Background：State.Danger 或 White + Danger border，依情境選擇。
- Text：White 或 Danger。
- 不得把紅色當一般強調色使用。

### 13.4 Disabled

- 仍應看得出原本控制項形狀。
- 背景、Border、文字降低對比。
- 不依賴透明度造成文字難讀。

### 13.5 Button Anatomy

候選基準：

- Standard height：35–36 px。
- Large action：42–46 px。
- 最小可用寬度：依文字決定，短文字建議約 88–100 px 起。
- 水平 padding：約 14–18 px。
- 文字水平、垂直置中。
- Icon + Text 時 icon 16–20 px，icon 與文字距離約 6–8 px。

不要求為符合 Radius、hover 或 icon 而重寫 framework 原生 Button；穩定與可維護性優先。

---

## 14. TextBox / Numeric Input

候選方向：

- Background：Surface.Control。
- Border：Neutral.Border，1 px。
- Text：Text.Primary。
- Placeholder / helper：Text.Secondary。
- Focus：Theme Accent / Focus，避免高飽和發光效果。
- Disabled：Subtle surface + Disabled text。
- 文字水平 padding：約 8–10 px。
- 高度使用 Compact 或 Standard Control。

不要使用內陰影、漸層或厚重立體邊框。

---

## 15. ComboBox

- 高度與同頁 TextBox 一致。
- 字級與 Body / Field role 一致。
- Border / Focus 與 TextBox 同系統。
- 下拉箭頭可保留 framework 原生樣式，只要不明顯破壞整體感。
- 不建議單純為了完全一致而自繪整個 ComboBox。

---

## 16. Tabs

候選方向：

- 文字約 10–11.5 pt，依程式調整。
- Active 狀態可透過 Accent、底線、字重或淡色背景呈現。
- Inactive 保持低彩度。
- 避免大型圓角膠囊式 navigation，除非該程式有明確理由。
- 不需要把所有原生 Tab 完全重畫；優先控制字體、間距與 Active state。

---

## 17. Table / List

商務桌面程式需保持資料掃描效率。

候選方向：

- Header：淺 neutral surface，Semibold / Bold。
- Body：White 或 Workspace 對應 surface。
- Row height：26–34 px，依密度調整。
- Grid line：Neutral.Border / Divider，低對比。
- Selected row：Theme `Accent.Selection`。
- Zebra row：可選；若使用，差異必須非常輕。
- 金額與純數字欄位通常右對齊；一般文字左對齊。
- 表頭與內容不可因追求留白而放大到降低掃描效率。

### 17.1 Grid continuity

若表格使用垂直欄線，Header 與 Body 必須共享同一組 column geometry；欄線應由表頭順暢延伸到表身，任何肉眼可見的約 1 px 欄位錯位都視為 UI 缺陷。

視窗 resize、scrollbar 出現／消失與 DPI scaling 後，也必須維持相同對齊結果。若某 framework 無法低成本可靠做到垂直欄線，寧可使用無垂直線設計，也不要保留錯位格線。

**實作參考附件：** [CYInvoice Table / List Implementation Reference](docs/CY_UI_REFERENCE_CYINVOICE_TABLE.md)

> 附件參考的是 CYInvoice 清單的幾何、欄寬、viewport、scrollbar 與 owner-draw 經驗；**目前 CYInvoice 的顏色、字級、列高、欄寬與 padding 均不是未來 Visual Guide 的固定標準**，日後應依新版 Theme / Typography / Density 重新決定。

---

## 18. Section / Group

未來不強制所有傳統 GroupBox 改成 Card。

候選視覺語言：

- Section Title 使用 Typography `Section Title`。
- 可搭配 1 px Divider。
- Continuous Surface 下，Section 背景優先 transparent / inherit。
- 只有真正獨立的功能單元才使用 Raised Surface / Card。
- 同一頁不要同時混用太多種 GroupBox、Card、框線與底色語言。

---

# Part F — Secondary Visual Elements

## 19. Status / Badge

- Success / Warning / Danger / Info 使用固定 State 色系。
- 小 Badge 可使用 soft background + 較深文字。
- Badge 可使用較大的 radius / pill；這是少數允許膠囊造型的元件。
- 狀態不能只靠顏色傳達，仍需要文字。

---

## 20. Progress

- 保持簡潔，避免動畫裝飾。
- 高度可約 6–10 px；若 framework 原生 ProgressBar 穩定，可保留原生結構並調整色彩。
- 進度文字應放在條外或可清楚閱讀的位置，不建議壓在細進度條內。

---

## 21. Icons

- 文字優先，Icon 輔助。
- 常用尺寸：16 / 20 / 24 px。
- 同一程式避免混用 filled、outline、emoji 等不同圖示語言。
- 不要求每個按鈕都有 Icon。
- 若 icon 只是增加裝飾而沒有辨識價值，寧可不用。

---

## 22. Dialog Visual Shell

此階段只定外觀方向，不定 Dialog 流程。

候選方向：

- Title hierarchy 明確。
- Body 使用正常 Body typography。
- 內容區避免過多 Card。
- Footer actions 與主畫面 Button system 一致。
- 是否使用原生 title bar 或自訂 header，依 framework 與穩定性決定，不在 Phase 1 強制統一。

---

# Part G — App Flexibility

## 23. App-level Override

未來正式採用後，各程式可以依需求調整：

- Body / Table / Button 實際字級。
- Compact / Standard / Large control density。
- Theme。
- Surface Model。
- Table row height。
- 是否使用 Card、Badge、Icon。

但應避免調整到破壞共同視覺角色，例如：

- 把 Danger 改成一般主色。
- 同一程式內同種按鈕有多種無規則尺寸。
- 各頁面自行發明不同文字層級。
- 同一 Theme 在不同畫面出現完全不同 Accent。

正式導入時，App Override 應以「少量必要差異」為原則，而不是變成每支程式重新設計一次。

---

## 24. 跨 Framework 實作原則

CYApps 可能同時存在 Qt、WinForms、Win32 / Go 等 UI 技術。

未來若採用本 Guide：

- 優先實現**角色一致**，不要求底層 control 完全一樣。
- 原生控制項已足夠接近時，不為追求 pixel-perfect 而重寫。
- 不因 1–2 px、圓角細節或箭頭樣式差異引入不穩定的自繪元件。
- DPI、字型 rendering、OS theme 造成的小差異可接受。
- 若某個 visual token 在 framework 中成本過高，可選擇合理近似值。

一句話：**看起來像同一家產品，比每個 pixel 一模一樣重要。**

---

# Part H — Complexity Guardrails

## 25. 避免 Design System 反過來拖累開發

這份 Guide 的成功條件之一，就是不能讓簡單功能因為 UI 規範變得難以維護。

未來正式採用時，建議保留以下 guardrails：

1. **不因視覺規範強迫更換 framework。**
2. **不因視覺規範重寫穩定原生控制項。**
3. **不為了精準 1–2 px 差異阻擋功能交付。**
4. **不要求每個程式實作所有 Theme。** 程式只需使用自己選定的一套。
5. **不要求每個程式實作所有 Surface Model。** 一個主要模型即可。
6. **不要求所有元件都有專用 component library。** 能集中 token / helper 就集中；無必要時不重構。
7. **新規範先在少數代表程式驗證，再擴散。**

---

## 26. 目前已知風險

### 26.1 規範過細

風險：開發者花大量時間調整 pixel，而不是完成產品。

對策：採「角色 + 基準 + 範圍」，少用硬數值。

### 26.2 Theme 過多

風險：每增加一套 Theme，就多一套需要維護與驗證的顏色。

對策：第一階段只完整定義 Blue / Warm / Teal；Slate / Sage 等到實際需求出現再做。

### 26.3 App Override 過度自由

風險：最後每支程式仍長得完全不同。

對策：只允許在字級範圍、密度、Theme、Surface Model 等已定義軸線上調整。

### 26.4 大量白色 Card 導致畫面碎裂

風險：灰背景上出現很多彼此無關的白色色塊，看起來像拼裝 Dashboard。

對策：使用 Surface Role / Surface Model 決定哪些區塊需要白底；Continuous Model 可讓 Section 直接繼承視窗背景。

### 26.5 跨 Framework 無法完全一致

風險：為追求一致而大量自繪，反而增加 bug。

對策：以視覺家族一致為目標，允許 framework-native 細節差異。

### 26.6 可讀性被「設計感」犧牲

風險：灰字太淡、字太小、留白過大、控制項過扁。

對策：商務桌面工具優先保留可讀性與資訊密度；設計感來自比例、色彩與一致性，而不是裝飾。

---

# Part I — 非定案的示意套用

## 27. 僅作討論用途的搭配示例

> 以下只用來說明 Theme 與 Surface Model 可以獨立組合，**不是各程式已決定的正式配置**。

| App | Theme 示例 | Surface 示例 | 備註 |
|---|---|---|---|
| CYInvoice | CY Blue | Continuous | 保留高密度商務輸入感 |
| CYAccounting | CY Blue / Teal | Continuous / Sectioned | 依現有 Qt styling 調整 |
| CYEnvelope | CY Warm | Sectioned | 文件／紙張／預覽型工具 |
| TriINVCalc | CY Teal | Continuous / Sectioned | 保留既有計算與發票特色 |
| Future workflow tool | CY Blue / Slate | Carded | 適合批次、檢查、結果摘要 |

---

# Part J — Phase Boundary

## 28. Phase 1 到此為止

本草案第一階段只處理：

- 色彩。
- Theme。
- Surface。
- 字體與字級層級。
- Border / Radius。
- Control size。
- Spacing。
- Button / Input / ComboBox / Tab / Table / Section 等核心元件外觀。

以下項目**刻意留到下一階段**：

- 功能流程。
- 控制項擺放位置。
- Primary Action 應放哪裡。
- Tab 應有哪些頁面。
- Enter / Esc / Tab 鍵行為。
- Dialog 何時出現。
- 驗證與錯誤處理流程。
- 表單欄位順序。
- 搜尋／查詢流程。
- 工作流程的步驟數量。

Phase 1 的目的只有一個：

> **先讓不同 CYApps 在不改變既有操作邏輯的前提下，逐步形成一致、現代、簡潔且可維護的商務桌面視覺語言。**

---

## 29. 下一輪討論建議

正式定案前，建議至少再完成以下工作：

1. 實際比較 CY Blue / Warm / Teal 三套色票。
2. 用同一個 Button / TextBox / Table 範例做視覺對照。
3. 確認 Continuous / Sectioned / Carded 三種 Surface Model 的邊界。
4. 選 1–2 支程式做純視覺 mock / prototype，不先改正式功能。
5. 確認字級與控制項高度在 100% / 125% / 150% DPI 下仍自然。
6. 使用者明確核准後，再決定是否移除 Draft 標示並建立正式採用方式。
