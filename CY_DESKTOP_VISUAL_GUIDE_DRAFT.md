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
**本次 checkpoint：2026-09-22**  
**不在本階段：功能流程、快捷鍵、資料流程、IA 與大規模 UX 重構**

---

# 0. 對話續作 Checkpoint

重新接手本 Visual Guide 時，**先讀本章，再讀各細節章節**。

## 0.1 狀態標記

- **`APPROVED`**：本輪已由使用者接受；Phase 1 不應無故重新推翻。
- **`APPROVED — ADVISORY`**：方向已接受，但屬建議型規範，不應為此限制程式實作。
- **`PROVISIONAL`**：方向已接受，但實際數值仍需 Shell / DPI / 真機驗證。
- **`OPEN`**：尚未完成。
- **`PHASE 2`**：刻意延後到 Layout / Interaction / UX 階段。
- **`REFERENCE`**：可參考成熟作法，但不可把現行數值直接當未來標準。
- **`PRESERVE`**：既有成熟成果明確保留，不因本 Guide 強制重製。

## 0.2 目前總盤點

| 項目 | 狀態 | 目前結論 |
|---|---|---|
| 整體視覺定位 | `APPROVED` | Modern Business Desktop；乾淨、低裝飾、桌面優先，可保留中高資訊密度 |
| 規範強度 | `APPROVED` | Core / Recommended Range / App Choice；不以大量硬數值綁死各 App |
| 主要字體 | `APPROVED` | `Microsoft JhengHei UI`，fallback `Microsoft JhengHei → Segoe UI → system sans-serif` |
| Typography hierarchy | `APPROVED` | 統一角色與相對層級，不要求所有 App 使用完全相同 pt |
| Typography 實際 pt | `PROVISIONAL` | 需做 100% / 125% / 150% DPI 真機驗證 |
| Density | `APPROVED` | Compact / Standard / Comfortable 三級；精確 px 為 `PROVISIONAL` |
| Theme 數量 | `APPROVED` | **Blue / Teal / Coral / Apricot 共四種；沒有 Warm Theme** |
| Theme 精確 HEX | `PROVISIONAL` | 視覺方向已核准，HEX 可在 Shell 真機做小幅修正 |
| State Colors | `APPROVED` | Info / Success / Warning / Danger 與 Theme 分離；Danger 不得跟 Coral 混用 |
| Surface Roles | `APPROVED` | Window / Workspace / Section / Control / Raised / Subtle / Border / Divider |
| Surface Models | `APPROVED` | Continuous / Sectioned / Carded；Card 只用於真正獨立工作單元 |
| Spacing / Margin / Padding / Alignment | `APPROVED` | 使用有限 spacing scale；Section 間距大於 Section 內間距；維持 alignment grid |
| Button | `APPROVED` | 高度與字級比例已定；寬度以內容驅動，不為填滿空間過度拉寬 |
| Dialog Button Size | `APPROVED — ADVISORY` | 優先 Compact / 小 Standard；通常不使用 Large；一般寬度由文字 + padding 決定 |
| Dialog Button Position | `APPROVED — ADVISORY` | 一般商務程式靠右可作預設參考，但**不寫死**；個案可置中或其他合理排列 |
| TextBox / Numeric / ComboBox | `APPROVED` | 視覺置中、左側 Label、Numeric 預設無 spinner、Focus/Error 等已定 |
| Tabs | `APPROVED` | 低存在感、Active Accent 底線／字重、不使用大型 pill |
| Section / Group / Divider | `APPROVED` | Section 預設 inherit/transparent；Card 不作一般排版容器 |
| Table / List | `APPROVED` | 高密度商務表格；Grid Continuity 為 MUST |
| CYInvoice 清單 | `REFERENCE` | 參考 geometry / viewport / scrollbar / owner-draw；現行色值與欄寬不是新標準 |
| Secondary Controls | `APPROVED` | Native-first；Scrollbar 不客製；Toggle 只用於真正 On/Off |
| Dialog / MessageBox / Status | `APPROVED — ADVISORY` | 原生穩定性優先；簡單 MessageBox 原生；複雜情境才 Custom Dialog |
| Main Window Shell | `APPROVED — ADVISORY` | Minimal / Business / Workbench 只是參考模式，不是強制模板 |
| App Icon Family | `PROVISIONAL` | V1 備案 + 目前候選保留；正式各 App ICO 尚未製作 |
| 現行 CYInvoice Icon | `PRESERVE` | **完成版，不得因 Icon Family 設計而改動** |
| UI Shell Prototype | `OPEN` | Phase 1 文件整理後做無引擎 Windows Shell 真機驗證 |
| UX / Layout / 操作流程 | `PHASE 2` | Phase 1 不鎖死 |

## 0.3 下一步

1. 完成 Phase 1 文件最後盤點。
2. 建立代表 App 的無引擎 UI Shell，驗證 100% / 125% / 150% DPI。
3. 依真機結果只微調 `PROVISIONAL` 數值，不重新推翻已核准的角色／原則。
4. Phase 1 確認後，再進 Phase 2 Layout / Interaction / UX。

---

# 1. 目的 — `APPROVED`

CYApps 跨 WinForms、Qt、Win32 / Go 等不同 UI framework。本 Guide 只建立共同的**視覺骨架**，不追求 pixel-perfect identical。

共同目標：

- 一眼看起來屬於同一組 CY 桌面產品。
- 保留不同 App 依功能與資訊密度調整的空間。
- 維持桌面商務軟體的效率、可讀性與穩定性。
- 原生控制項能安全達到目的時，不為 1–2 px 差異大量自繪。
- 規範用來降低設計決策成本，不得反過來拖累開發。

---

# 2. 視覺定位 — `APPROVED`

- **Modern Business Desktop**：現代商務桌面，而不是 Web Dashboard。
- **Clean but Dense**：乾淨，但允許中高資訊密度。
- **Low Decoration**：不靠大量陰影、漸層、彩色塊堆出設計感。
- **Text-first**：文字標示優先；Icon 輔助。
- **Readable**：繁中、英文、數字、表格與輸入欄位優先清楚。
- **Framework-flexible**：不同 framework 可有細節差異，只要角色與層級一致。

---

# 3. 規範強度 — `APPROVED`

## 3.1 Core

跨 App 應維持的共同骨架，例如 Theme 角色、Danger 語意、Typography hierarchy、Grid Continuity。

## 3.2 Recommended Range

提供合理範圍，不用單一數字鎖死所有 App。

## 3.3 App Choice

App 可依需要選 Theme、Surface Model、Density、Zebra Row、Card 等。

## 3.4 App Override

允許少量必要差異；不可把 Override 變成每支 App 重新建立一套設計系統。

---

# Part A — Typography & Density

## 4. 字體 — `APPROVED`

主要字體：

`Microsoft JhengHei UI`

Fallback：

`Microsoft JhengHei` → `Segoe UI` → system sans-serif

字重：

- Regular：Body / Input / Table body。
- Medium / Semibold：Section title / Table header / Active Tab / 一般重要控制。
- Bold：少量 Major Title / 特別重要摘要；不整個 UI 都 Bold。

## 5. Typography Roles — hierarchy `APPROVED`, values `PROVISIONAL`

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

核心關係：

`Major Title > Page Title > Section Title > Body > Secondary`

Secondary 優先靠顏色／字重降低層級，不把文字縮到難讀。

## 6. Density — `APPROVED` concept / `PROVISIONAL` values

### Compact

- TextBox / Combo：約 28–31 px。
- Button：約 30–32 px。
- Table Row：約 26–29 px。
- Body：約 9–9.5 pt。

### Standard

- TextBox / Combo：約 31–35 px。
- Button：約 34–38 px。
- Table Row：約 30–34 px。
- Body：約 9.5–10.5 pt。

### Comfortable

- TextBox：約 36–40 px。
- Button：約 40–44 px。
- Table Row：約 35–40 px。
- Body：約 10.5–11.5 pt。

同一頁不要無理由混三種 density；Dialog 可與主畫面使用不同 density。

## 7. DPI — `APPROVED`

至少驗證：

- 100%
- 125%
- 150%

驗收：TextBox 不裁字、Button 不切字、Label 不爆版、Table Header/Body 保持對齊、Dialog 不破版、Scrollbar 不破壞 geometry。

優先順序：

1. OS DPI awareness。
2. framework 原生 scaling。
3. layout 自動伸縮。
4. 最後才是必要 override。

不建立額外複雜 DPI 引擎，不重複乘 scaling factor。

---

# Part B — Color & Theme

## 8. Core Neutral Palette — roles `APPROVED`, HEX `PROVISIONAL`

| Token | Candidate | 用途 |
|---|---|---|
| `Neutral.White` | `#FFFFFF` | Control / Table / Raised |
| `Neutral.Window` | `#F5F7FA` | Window background |
| `Neutral.Subtle` | `#F8FAFC` | Secondary surface |
| `Neutral.ReadOnly` | `#F1F3F5` | Read-only / Disabled surface |
| `Neutral.Border` | `#D9DEE5` | 一般 Border |
| `Neutral.Divider` | `#E5E8EC` | Section Divider |
| `Neutral.Grid` | `#DDE1E6` | Table Grid |
| `Text.Primary` | `#1F2937` | 主文字 |
| `Text.Secondary` | `#667085` | 次要文字 |
| `Text.Disabled` | `#98A2B3` | Disabled |
| `Text.OnAccent` | `#FFFFFF` | Accent 上文字 |

不要持續增加大量只差一點點的灰階。

## 9. Theme Set — names `APPROVED`, HEX `PROVISIONAL`

**第一版就是四種：Blue / Teal / Coral / Apricot。沒有 Warm Theme。**

Theme 只改 Accent 家族，不重新建立另一套 Typography / Neutral / Control system。

### 9.1 Blue

標準商務方向。

| Token | Candidate |
|---|---|
| `Accent` | `#2E4A71` |
| `Hover` | `#273F61` |
| `Pressed` | `#203550` |
| `Soft` | `#E8EEF5` |
| `Focus` | `#6F8FB8` |
| `Selection` | `#E0EAF5` |

### 9.2 Teal

工具／計算／技術方向。

| Token | Candidate |
|---|---|
| `Accent` | `#2F6F73` |
| `Hover` | `#275E61` |
| `Pressed` | `#204E51` |
| `Soft` | `#E5F1F0` |
| `Focus` | `#69A1A3` |
| `Selection` | `#DCEDEB` |

### 9.3 Coral

暖感但**偏橘珊瑚，不偏危險紅**。

| Token | Candidate |
|---|---|
| `Accent` | `#C9754B` |
| `Hover` | `#B66841` |
| `Pressed` | `#9E5936` |
| `Soft` | `#F8E9E0` |
| `Focus` | `#D8926F` |
| `Selection` | `#F3E1D6` |

### 9.4 Apricot

較亮、較輕、較活潑的暖色方向。

| Token | Candidate |
|---|---|
| `Accent` | `#D8844A` |
| `Hover` | `#C3733F` |
| `Pressed` | `#AA6435` |
| `Soft` | `#FAECDD` |
| `Focus` | `#E0A178` |
| `Selection` | `#F6E3D1` |

## 10. State Colors — `APPROVED` roles / `PROVISIONAL` HEX

State 與 Theme 分離：

| State | Candidate | Soft |
|---|---|---|
| Success | `#21825C` | `#EAF5F0` |
| Warning | `#A66B10` | `#FAF1E3` |
| Danger | `#B43737` | `#F8EAEA` |
| Info | `#356A9A` | `#EAF1F7` |

**Danger 永遠是 Danger，不得因 Coral Theme 改成 Coral。**

Accent 主要用在 Primary、Focus、Active Tab、Selection、小型 highlight；不要大量鋪滿 Header / Group / Window。

Light Mode first；第一版不做 Dark Mode。

---

# Part C — Surface

## 11. Surface Roles — `APPROVED`

- `Surface.Window`
- `Surface.Workspace`
- `Surface.Section`（可 transparent / inherit）
- `Surface.Control`
- `Surface.Raised`
- `Surface.Subtle`
- `Surface.Border`
- `Surface.Divider`

重點：問題不是灰底或白底，而是層級是否一致。

## 12. Surface Models — `APPROVED`

### Continuous

高密度資料輸入／發票／帳務。Section 多半 inherit，Control / Table 可白底。

### Sectioned

一般桌面工具／左右工作區／Preview。主要工作區清楚分區，但內部不再層層 Card。

### Carded

Workflow / 批次／結果摘要。Card 只代表真正獨立工作單元，不作普通排版容器。

一般 Raised / Card 優先：White + 1 px Border；不預設陰影。

---

# Part D — Spacing / Alignment

## 13. Spacing — `APPROVED`

優先 spacing scale：

`4 / 8 / 12 / 16 / 24 / 32 px`

不是硬性 pixel lock；framework / DPI 可小幅近似。

常見節奏：

- 4：icon / text 微距。
- 8：同組元件。
- 12：一般欄位。
- 16：小區塊。
- 24：主要 Section。
- 32：大區塊／頁面留白。

## 14. Margin / Padding — `APPROVED` principle

- Window / Page margin 約 12–32 px 依 density 選擇。
- 高密度商務：通常 12–16 px。
- 一般工具：16–24 px。
- Preview / 低密度：24–32 px。
- Card / framed Section 內部通常 12–24 px。

## 15. Alignment — `APPROVED`

MUST：

- 同一 Section 的 Label Column 一致。
- Label 左邊界一致；Input 左邊界一致。
- 同列控制項視覺高度／基準一致。
- Section 間距 > Section 內欄位間距。
- Resize 不破壞 alignment grid。
- 同類資料欄位盡量維持合理一致的邊界。
- Table header/body geometry 保持連續。

多欄表單可各自維持自己的 Label Column，不要求整頁共用超寬 Label 欄。

典型 Label-to-Control 距離約 8–12 px；Button group 常見間距約 8 px，均為建議值。

---

# Part E — Core Components

## 16. Button — `APPROVED`

### 16.1 Size

| 類型 | 高度 | 文字 |
|---|---:|---:|
| Compact | 30–32 px | 9–9.5 pt |
| Standard | 34–38 px | 9.5–10.5 pt |
| Large | 42–48 px | 10.5–12 pt |

Large 主要給主工作畫面的強 Primary Action，不是 Dialog Footer 的常態。

### 16.2 Width

**Content-driven。**

- 依文字 + 合理水平 padding 決定。
- 一般 horizontal padding 約 14–20 px 可作參考。
- 同組可等寬，但以最長文字的自然需求為基準。
- 不因視窗很寬就把 Button 拉到 130–160 px。
- 不為填滿 Footer 做兩顆半寬大型按鈕。

### 16.3 Dialog Button Size — `APPROVED — ADVISORY`

- 優先 Compact / 小 Standard，約 30–34 px 高。
- 常見短按鈕自然寬度約 72–100 px；稍長文字約 100–120 px。
- 這些是參考範圍，不是硬限制。

### 16.4 Geometry

- Standard radius 以約 4 px 為代表。
- Large 可 5–6 px。
- Compact 2–4 px。
- 不做一般 pill Button。

### 16.5 Primary

Accent solid + White text；Hover / Pressed 使用 Theme 階層；一般不使用 gradient / shadow。

### 16.6 Secondary

White / Control Surface + 1 px Neutral Border + Text.Primary；Hover 使用 subtle tint。

### 16.7 Danger

- 一般破壞性操作：白底紅框紅字。
- 最終不可逆確認：實心 Danger Red + White。

### 16.8 Disabled / Focus

Disabled 仍可讀；Focus 明確但不改變 geometry，不做 glow。

### 16.9 Icon + Text

Icon 是輔助，文字是主要辨識；Icon-only 只用於成熟、直覺、高密度功能。

---

## 17. TextBox / Numeric / Label — `APPROVED`

- Background：Surface.Control。
- Border：1 px Neutral。
- Radius：約 3 px。
- 文字只要求**視覺上接近垂直置中**；不為此重寫原生 TextBox。
- Disabled / Read-only 可使用相同淡灰視覺；Read-only 內容仍應清楚可讀。
- Focus 只換 Accent border，避免厚度改變造成跳動。
- Error：1 px Danger Border + 短 helper text；不整格紅、不 glow、不改 geometry。
- Placeholder：Secondary、非斜體、不可取代 Label。

### Label

預設左側 Label + 右側 Control：

```text
姓　　名 [____________]
地　　址 [____________]
聯絡電話 [____________]
```

Label 本身左對齊；短中文可使用全形空白分散。Top Label 只作特殊情境例外。

### Numeric

- 金額／數量／單價／百分比預設右對齊。
- Numeric spinner 預設不顯示；做不到時可用 TextBox + validation。
- 郵遞區號、統編、發票號碼等 identifier 不因為是數字就強制右對齊。

---

## 18. ComboBox — `APPROVED`

與 TextBox 同視覺家族；原生 dropdown arrow 可保留，不為 radius / arrow 重新自繪。

---

## 19. Tabs — `APPROVED`

- 低存在感。
- Active：Accent 約 2 px underline + 稍強字重。
- Inactive：Neutral text。
- 約 32–40 px 高為常見參考。
- 不做大型 pill / hero navigation。

---

## 20. Section / Group / Divider / Card — `APPROVED`

- Section 預設 transparent / inherit。
- Section Title 約 11.5–13 pt、Semibold、Text.Primary。
- Divider 1 px、低對比。
- 穩定 GroupBox 可保留，不強迫重寫。
- Card 只用於 Preview / Result / Drop Area / 獨立 Workflow Unit。

---

## 21. Table / ListView / DataGrid — `APPROVED`

### Density

| 類型 | Row Height |
|---|---:|
| Compact | 26–29 px |
| Standard | 30–34 px |
| Comfortable | 35–40 px |

### Visual

- Header：淺 Neutral / Subtle，Semibold，略高於 Body row。
- Body：通常 White。
- Zebra：可選，非常淡。
- Grid：低對比。
- Selection：Accent.Selection soft tint + 深文字。

### Alignment

- Text / name / address：左。
- Money / numeric value：右。
- 短 status：可中。
- Identifier 依內容決定。

### Grid Continuity — MUST

Header / Body 必須共享同一組 column geometry；肉眼可見約 1 px 欄線錯位視為 UI defect。

需驗證 resize、maximize/restore、scrollbar 出現／消失、100/125/150% DPI。

如果 framework 無法可靠維持垂直線對齊，**寧可取消垂直線，也不要保留錯位格線**。

### CYInvoice Reference — `REFERENCE`

[CYInvoice Table / List Implementation Reference](docs/CY_UI_REFERENCE_CYINVOICE_TABLE.md)

參考 geometry / viewport / scrollbar compensation / column width source of truth / owner-draw border ownership / resize relayout。

**現行 CYInvoice 顏色、字級、row height、欄寬、padding 不是新版固定標準。**

---

# Part F — Secondary Controls

## 22. Checkbox / Radio — `APPROVED`

Native-first；Body font；視覺垂直對齊；不做 oversized Web-style controls。

## 23. Toggle — `APPROVED`

只用於真正 immediate On / Off；無成熟 Toggle 時用 Checkbox；動畫非必要。

## 24. Progress / Loading — `APPROVED`

- 一般 8–12 px；主要流程可 12–16 px。
- 文字放條外。
- 不確定進度用 native spinner / marquee。

## 25. Scrollbar — `APPROVED`

Native-first，不客製；但必須把 scrollbar 對 layout / table geometry 的影響算進去。

## 26. DatePicker / Tooltip / Slider — `APPROVED`

DatePicker 高度／字體與 Input 接近，popup native。Tooltip native。Slider 非核心，有需求再用。

---

# Part G — Dialog / MessageBox / Status

## 27. Dialog / MessageBox / Status — `APPROVED — ADVISORY`

### 27.1 Dialog Shell

- Native title bar / window behavior 優先。
- CY 統一內容區 typography / surface / input / button。
- 不為漂亮全面改 borderless / custom chrome。
- Footer 可有 1 px divider；不必另做厚重背景。

### 27.2 Native MessageBox

簡單 OK / Yes-No / Warning / Question 可使用 framework / Windows 原生 MessageBox，**按鈕排列跟隨原生，不重排**。

### 27.3 Custom Dialog 使用時機

表單、設定、Preview、多項選擇、重要 destructive confirm、詳細資訊、多步驟等才使用 CY styled dialog。

### 27.4 Button Position — `APPROVED — ADVISORY`

**不寫死。**

- 一般 Windows 商務程式常見的右下 Action Group，可作預設參考。
- 若小型 Dialog、內容重心、視窗寬度或 App 本身設計更適合置中，可個案置中。
- Wizard / 大型工作型 Dialog 也可依版面合理排列。
- 不能為了「一致」而犧牲視覺平衡與操作效率。

真正固定的是：按鈕不要過度拉寬、同組間距一致、Primary / Secondary / Danger 語意清楚。

### 27.5 Danger Confirm — MUST

Danger 與 Theme 分離。一般 Danger 白底紅框；最終不可逆確認可實心紅底白字。

### 27.6 Error

主訊息先用人話說明結果；HTTP code / API response / stack trace 等技術資訊可折疊或另顯示。

### 27.7 Status Banner

Soft state background + 1 px related border + icon/text；不使用整片高飽和紅／綠。

### 27.8 Badge

Badge 可使用 pill；小型、低彩度、soft background + deep text；不是 Button，不做 hover action。

### 27.9 Inline Validation

1 px Danger border + 附近短訊息；不可造成 layout jump。

### 27.10 Processing

短動作可把按鈕改為「處理中…」並 Disabled；有明確進度用 Progress；時間不確定用 spinner / marquee。

### 27.11 Toast

MAY；不是核心元件，不要求所有 framework 實作。

---

# Part H — Main Window Shell

## 28. Main Window Shell — `APPROVED — ADVISORY`

以下是**建議型 pattern，不是強制模板**。

### 共通方向

- Native Windows title bar 優先。
- Title Bar 顯示 App Icon + App Name 即可，不塞大量公司／版本／環境資訊。
- Internal Header 是 Optional；只有真的有持續資訊價值才使用。
- Header 不重複 App Name。
- Header 約 40–52 px、Footer 約 20–26 px 都只是參考值。
- Header 優先 White / Neutral / Accent.Soft，不預設整條深色 Accent。
- Footer / Status Bar 只有真的有持續狀態資訊才使用。
- Status color 只作用在 dot / icon / badge / text，不把整條 Footer 染色。
- 避免 Title Bar + Header + Toolbar + Tabs + Page Title + Section Title 全部堆在一起。

### 三種參考模式

- **Minimal**：Title Bar + Content。
- **Business**：Title Bar + Tabs/Toolbar + Content + optional Status。
- **Workbench**：Title Bar + optional compact Header/Toolbar + Main Workspace + optional Status。

任何 App 都可依 framework / 功能需求調整，不因不符合這三張參考就判定不合格。

---

# Part I — App Icon Family

## 29. Icon Family — `PROVISIONAL`

App Icon 不要求放 `CY` 或公司 Logo。

目標：像 Office / Adobe 一樣，同一家族但各 App 可辨識。

### 29.1 CYInvoice — `PRESERVE`

**`apps/CYInvoice/assets/CYInvoice.ico` 是完成版，不得為 Icon Family 改動。**

INV DNA：

- 扁平。
- 高對比。
- 大型 `INV`。
- 下方兩條簡單橫線。
- 無 gradient / shadow / 3D 細節。
- 小尺寸可辨識。

### 29.2 目前候選延伸

其他 App 保留大型英文縮寫（ACC / ENV / CAL / CVT / WM 等），縮寫下方只搭配**一個極簡功能符號**；符號優先為小尺寸辨識服務，不做複雜主圖。

### 29.3 只保留兩張概念圖

- [V1 備案](design/cy-desktop-visual-guide/icon-concepts/icon-family-v1-backup.jpg)
- [目前候選](design/cy-desktop-visual-guide/icon-concepts/icon-family-current-option.jpg)

中間迭代不保留。

### 29.4 尚未完成

- 各 App Master SVG / PNG。
- 16 / 24 / 32 / 48 / 64 / 128 / 256 小尺寸優化。
- ICO 生成。
- Windows Taskbar / Explorer 真機檢查。
- 各 App 最終代表色。

---

# Part J — Cross-framework / Complexity Guardrails

## 30. Cross-framework — `APPROVED`

- 角色一致 > 底層 control 完全一樣。
- Native control 足夠接近時不重寫。
- 不因 radius、arrow、scrollbar、1–2 px 細節引入不穩定自繪。
- DPI / font rendering / OS theme 的合理差異可接受。
- visual token 成本過高可採合理近似。

## 31. Complexity Guardrails — `APPROVED`

1. 不因 Guide 強迫更換 framework。
2. 不因 Guide 重寫穩定原生控制項。
3. 不為一般 1–2 px 差異阻擋功能交付；**Table Grid Continuity 是例外，明顯錯位仍是 defect。**
4. 不要求每個 App 實作所有 Theme。
5. 不要求每個 App 實作所有 Surface Model。
6. 不要求所有元件都建立專用 component library。
7. 先在代表 App / Shell 驗證再擴散。
8. 優先規範角色、比例、視覺結果，不把所有 x/y/padding 寫成硬限制。
9. App Override 是必要差異，不是重新設計。

---

# Part K — App Example（非正式配置）

## 32. Theme / Surface Example — `REFERENCE`

僅示意，不是 App 正式配置：

| App | Theme 示例 | Surface 示例 |
|---|---|---|
| CYInvoice | Blue | Continuous |
| CYAccounting | Blue / Teal | Continuous / Sectioned |
| CYEnvelope | Coral / Apricot | Sectioned |
| TriINVCalc | Teal | Continuous / Sectioned |
| CYWatermark / Harness | Blue / Teal | Carded / Sectioned |

---

# Part L — UI Shell Validation

## 33. 無引擎 UI Shell — `OPEN`

Guide 大致完成後，不直接改正式引擎；先做 UI Shell Prototype：

- UI 是真的。
- 資料／引擎是假的。
- 不連正式 API / DB。
- 不正式開立發票／列印／浮水印／修改資料。

真機測：

- Resize / maximize / restore。
- Tabs / Inputs / Combo / Table selection。
- Hover / Pressed / Disabled / Focus。
- Dialog / Status / fake Progress。
- 100% / 125% / 150% DPI。

第一輪代表：

- CYInvoice：高密度商務資料輸入。
- CYWatermark / Harness：Workflow 工具。
- CYEnvelope：Input + Preview Workbench。

Prototype 優先使用各 App 正式 framework，避免 mock 做得到、正式 framework 做不到。

---

# Part M — Phase Boundary

## 34. Phase 1

處理：Color / Theme / Surface / Typography / Density / Border / Radius / Spacing / Component Visual / Dialog / Status / Shell Visual / App Icon Family。

## 35. Phase 2 — `PHASE 2`

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

**注意：Dialog Button 的靠右／置中也屬可依個案調整的 Layout 決策，本 Guide 只提供商務桌面常見建議，不做硬限制。**

---

# 36. 恢復工作時最短指引

1. 先讀最上方 CAUTION：本文件仍是 Draft，不得干擾現行專案。
2. 讀 `0. 對話續作 Checkpoint`。
3. 不重新討論 `APPROVED`，除非使用者主動要求修改。
4. `PROVISIONAL` 以 UI Shell / Windows 真機驗證為主，不靠文字無限微調。
5. Table 參考 `docs/CY_UI_REFERENCE_CYINVOICE_TABLE.md`。
6. Icon 只保留兩張候選；現行 CYInvoice Icon 不改。
7. Theme 第一版就是 **Blue / Teal / Coral / Apricot**，不要恢復舊 Warm Theme。
8. Shell / Dialog / Button Position 多數屬 advisory；不要為了 Guide 限制正常程式設計。
9. 在使用者明確核准以前，本文件仍不能變成治理規則或現行開發門檻。
