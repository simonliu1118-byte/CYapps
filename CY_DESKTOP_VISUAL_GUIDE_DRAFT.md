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
**本次 checkpoint：2026-09-22**

---

# 0. 對話續作 Checkpoint

這一章是為了避免聊天紀錄到達上限後遺失決策。重新接手本 Visual Guide 時，**先讀本章，再讀各細節章節**。

## 0.1 狀態標記

- **`APPROVED`**：本輪已由使用者接受，Phase 1 不應無故重新推翻。
- **`PROVISIONAL`**：方向已接受或已有候選，但仍需實機／Shell／DPI 驗證後才能成為正式值。
- **`OPEN`**：尚未討論完成或尚未定案。
- **`PHASE 2`**：刻意延後到 Layout / Interaction / UX 階段，不應在 Phase 1 先鎖死。
- **`REFERENCE`**：可參考成熟現有作法，但不能把現行色值、字級或尺寸直接當未來標準。
- **`PRESERVE`**：既有成熟成果明確保留，不因本 Guide 強制重製。

## 0.2 目前總盤點

| 項目 | 狀態 | 目前結論 |
|---|---|---|
| 整體視覺定位 | `APPROVED` | 現代商務、桌面優先、低裝飾、資訊密度可偏高 |
| 規範強度 | `APPROVED` | 使用 Core / Recommended Range / App Choice，不以大量硬數值綁死各 App |
| 主要字體 | `PROVISIONAL` | `Microsoft JhengHei UI` 為主要方向；正式字級仍需 DPI / Shell 驗證 |
| Typography hierarchy | `APPROVED` | 統一角色與層級，不要求所有 App 使用完全相同 pt |
| Core Neutral 色票 | `PROVISIONAL` | 角色已定，精確 HEX 尚未正式目視核准 |
| CY Blue / Warm / Teal | `PROVISIONAL` | 三套 Theme 方向保留；精確色票尚未最終定案 |
| Slate / Sage | `OPEN` | 僅保留未來可能性，不在第一版先完成 |
| Surface Roles | `APPROVED` | Window / Workspace / Section / Control / Raised / Subtle / Border / Divider |
| Surface Models | `APPROVED` | Continuous / Sectioned / Carded 三種模型可依 App 選擇 |
| Surface 實際色彩配方 | `OPEN` | 概念已定，Blue/Warm/Teal 對應的完整 Surface 配色尚待完成 |
| Button | `APPROVED` | 微圓角、尺寸階層、字級比例、Primary/Secondary/Danger/Disabled 已定 |
| TextBox / Numeric / ComboBox | `APPROVED` | 視覺置中、左側 Label、Numeric 無 spinner、Focus/Error 等已定 |
| Tabs | `APPROVED` | 低存在感、Active Accent 底線／字重、不使用大型 pill |
| Section / Group / Divider | `APPROVED` | Section 預設 inherit/transparent；Title + spacing + divider；Card 不作一般排版工具 |
| Table / List | `APPROVED` | 高密度商務表格；Grid continuity 為重要驗收條件 |
| CYInvoice 清單 | `REFERENCE` | 參考幾何／對齊／viewport／owner-draw 經驗，不保留舊色值與欄寬為標準 |
| Checkbox / Radio / Toggle | `APPROVED` | 原生優先；Toggle 只用於真正 On/Off，做不到可回 Checkbox |
| Progress / Scrollbar / DatePicker / Tooltip | `APPROVED` | 穩定原生優先；Scrollbar 不客製；Progress 簡潔細條 |
| Dialog / MessageBox / Status | `PROVISIONAL` | 已提出方向且 UI 樣本觀感獲接受，但尚未逐條正式確認 |
| 主視窗 Title/Header/Footer/Status | `OPEN` | 已提出建議，但因轉入 Icon 討論而尚未正式通過 |
| App Icon Family | `PROVISIONAL` | V1 備案 + 目前候選均已保留；正式各 App ICO 尚未製作 |
| 現行 CYInvoice Icon | `PRESERVE` | **完成版，不得因 Icon Family 設計而改動** |
| Spacing / Margin / Padding / Alignment | `OPEN` | 下一個重要視覺議題，尚未正式討論 |
| UI Shell Prototype | `OPEN` | Guide 接近定案後，做「只有 UI/假資料、無實體引擎」的 Windows Shell 實機驗證 |
| UX / Layout / 操作流程 | `PHASE 2` | Phase 1 不處理 |

## 0.3 目前下一步建議順序

1. `OPEN`：Spacing / Margin / Padding / Alignment。
2. `PROVISIONAL`：正式比較 Blue / Warm / Teal 色票與 Surface 配方。
3. `PROVISIONAL`：Typography 實際字級與控制高度做 100% / 125% / 150% DPI 驗證。
4. `OPEN / PROVISIONAL`：快速完成 Header/Footer、Dialog/Status 最終確認。
5. 回填完整 Guide 後，建立代表 App 的無引擎 UI Shell，實機驗證。
6. Phase 1 確認後，才進 Phase 2 Layout / Interaction / UX。

---

## 1. 目的 — `APPROVED`

CYApps 目前跨越 WinForms、Qt、Win32／Go 等不同技術世代與 UI framework。各程式功能取向不同，因此不適合強制所有畫面使用完全相同的版型、字級或控制項尺寸。

本草案的目標是建立一套共同的**視覺骨架**：

- 一眼看起來屬於同一組 CY 桌面產品。
- 保留不同程式依功能、資訊密度與使用情境調整的空間。
- 維持商務桌面軟體的效率與可讀性，不為了追求 Web／App 風格而犧牲操作感。
- 優先使用各 framework 穩定、容易維護的實作方式，不為追求 1–2 px 的完全一致而大量客製控制項。
- 視覺規範應降低日後設計決策成本，而不是增加程式製作難度。

---

## 2. 視覺定位 — `APPROVED`

CY 桌面程式共同方向：

- **Modern Business Desktop**：現代商務桌面軟體，而不是 Web Dashboard。
- **Clean but Dense**：乾淨，但允許商務工具需要的中高資訊密度。
- **Low Saturation**：低彩度、穩定，不使用大量鮮豔色塊。
- **Text-first**：文字標示優先；圖示作輔助，不依賴 icon-only 操作。
- **Readable**：優先確保繁體中文、數字、表格與輸入欄位的清楚閱讀。
- **Framework-flexible**：不同 framework 可以有細節差異，只要視覺角色與層級一致。

本 Guide 不追求所有程式 pixel-perfect identical；目標是建立**同一家族的視覺語言**。

---

## 3. 規範強度 — `APPROVED`

### 3.1 Core

預期各程式都應維持的共同視覺骨架，例如：

- 字體家族方向。
- 色彩角色命名與用途。
- Primary / Danger 的語意不可混用。
- 文字層級必須清楚。
- 同一程式內同類控制項應維持一致。

### 3.2 Recommended Range

提供基準值與合理範圍，程式可依需要調整。

**目的不是用精準 px 綁死所有 App，而是避免明顯失衡，例如大按鈕配小字、小按鈕配過大字。**

### 3.3 App Choice

依程式性質自由選擇，例如：

- Blue / Warm / Teal Theme。
- Continuous / Sectioned / Carded Surface Model。
- 表格是否使用 zebra row。
- Compact / Standard / Large density。

### 3.4 App Override 邊界

可調整：字級、控制高度、Theme、Surface Model、row height、是否需要 Card 等。

不可把「可調整」解讀為每支 App 完全重新設計；仍需使用共同角色與比例邏輯。

---

# Part A — Typography

## 4. 字體家族 — `PROVISIONAL`

主要方向：

`Microsoft JhengHei UI`

建議 fallback：

`Microsoft JhengHei` → `Segoe UI` → system sans-serif

原因：

- Windows 繁體中文顯示穩定。
- CYApps 現有多支程式已使用。
- 商務桌面程式閱讀性佳。
- 數字、中文與英文混排相對穩定。

正式定案前仍需以實際 Windows Shell 驗證不同 DPI 與 framework 的 rendering。

### 4.1 字重 — `APPROVED`

優先使用：

- Regular：一般內容。
- Medium / Semibold：表頭、較強 Label、一般重要控制。
- Semibold / Bold：主要標題、大型 Primary Action。

避免所有按鈕、Label、Section Title 全部粗體。

---

## 5. 字級角色 — `APPROVED` hierarchy / `PROVISIONAL` values

固定的是**角色與相對比例**，不是每支程式完全相同的 pt。

| Role | 目前候選基準 | 建議範圍 | 狀態 |
|---|---:|---:|---|
| App Title | 17 pt | 16–20 pt | `PROVISIONAL` |
| Page / Major Title | 14.5 pt | 13.5–16 pt | `PROVISIONAL` |
| Section Title | 12 pt | 11–13.5 pt | `PROVISIONAL` |
| Body / Field | 10.5 pt | 9.5–11.5 pt | `PROVISIONAL` |
| Secondary | 9 pt | 8.5–10 pt | `PROVISIONAL` |
| Standard Button | 9.5–10.5 pt | 約 9–11 pt | `PROVISIONAL` |
| Large Button | 10.5–12 pt | 依高度同步增加 | `APPROVED` 比例 |
| Table Header | 9.5–10.5 pt | 9–11 pt | `PROVISIONAL` |
| Table Body | 9.5–10.5 pt | 9–11 pt | `PROVISIONAL` |
| Badge / Small Status | 9 pt | 8.5–10 pt | `PROVISIONAL` |

### 5.1 已定原則

- `App Title > Page Title > Section Title > Body > Secondary`。
- 大型按鈕必須同步放大文字，避免 46–48 px 高按鈕仍使用 9 pt 小字。
- 高密度發票／帳務工具可以縮小部分 table/body；預覽型工具可以較大。
- 不要求不同 App 使用相同 pt。

---

# Part B — Color System

## 6. Core Neutral Palette — `PROVISIONAL`

色彩角色已接受；以下 HEX 尚未最終目視定案。

| Token | Candidate | 用途 |
|---|---|---|
| `Neutral.White` | `#FFFFFF` | 控制項、表格、必要白色 surface |
| `Neutral.Window` | `#F5F7FA` | 預設視窗／Canvas 背景 |
| `Neutral.Subtle` | `#F8FAFC` | 次要區、不可編輯欄位、摘要底 |
| `Neutral.Border` | `#DCE0E6` | 一般邊框 |
| `Neutral.Divider` | `#E4E7EC` | 分隔線 |
| `Text.Primary` | `#1F2937` | 主文字 |
| `Text.Secondary` | `#667085` | 次要文字 |
| `Text.Disabled` | `#98A2B3` | 停用文字 |
| `State.Success` | `#21825C` | 成功 |
| `State.Warning` | `#A66B10` | 警告 |
| `State.Danger` | `#B43737` | 危險／破壞性操作 |
| `State.Info` | `#356A9A` | 一般資訊 |

---

## 7. Theme Palette — `PROVISIONAL`

Theme 主要改變 Accent 家族，不重新發明一整套 UI。

每套至少包含：

- `Accent`
- `Accent.Hover`
- `Accent.Pressed`
- `Accent.Soft`
- `Accent.Focus`
- `Accent.Selection`

### 7.1 CY Blue — 標準商務

適合發票、帳務、ERP、一般商務工具。

| Token | Candidate |
|---|---|
| `Accent` | `#2E4A71` |
| `Accent.Hover` | `#273F61` |
| `Accent.Pressed` | `#203550` |
| `Accent.Soft` | `#E8EEF5` |
| `Accent.Focus` | `#6F8FB8` |
| `Accent.Selection` | `#E0EAF5` |

### 7.2 CY Warm — 暖灰／米棕商務

適合文件、列印、信封、辦公型工具。

| Token | Candidate |
|---|---|
| `Accent` | `#765746` |
| `Accent.Hover` | `#614638` |
| `Accent.Pressed` | `#503A2F` |
| `Accent.Soft` | `#F3EAE4` |
| `Accent.Focus` | `#A98168` |
| `Accent.Selection` | `#EEE0D7` |

### 7.3 CY Teal — 青綠工具型

適合計算、資料轉換、技術工具。

| Token | Candidate |
|---|---|
| `Accent` | `#2F6F73` |
| `Accent.Hover` | `#275E61` |
| `Accent.Pressed` | `#204E51` |
| `Accent.Soft` | `#E5F1F0` |
| `Accent.Focus` | `#69A1A3` |
| `Accent.Selection` | `#DCEDEB` |

### 7.4 Slate / Sage — `OPEN`

只保留方向，不在沒有實際 App 需求時先增加維護成本。

---

# Part C — Color & Surface

## 8. Surface Role — `APPROVED`

畫面是否使用白底、灰底、次級底色或透明繼承，應由 Surface Role 決定，而不是規定「功能區一定白底」。

| Token | 角色 |
|---|---|
| `Surface.Window` | 整個視窗／Canvas 背景 |
| `Surface.Workspace` | 主要工作區背景 |
| `Surface.Section` | 功能區塊背景，可為 transparent / inherit |
| `Surface.Control` | TextBox、ComboBox、Table 等控制項表面 |
| `Surface.Raised` | 真正需要凸顯的結果、預覽、獨立 Card |
| `Surface.Subtle` | 摘要、不可編輯、次要區塊 |
| `Surface.Border` | 區塊／控制項邊界 |
| `Surface.Divider` | 內容分隔線 |

### 8.1 關鍵原則

`Surface.Section` 允許 `transparent / inherit`。

因此 CYInvoice 類高密度商務程式可以直接讓各功能區使用視窗灰底，再以標題、Divider、白色控制項建立層級；**不必把每一區包成白色 Card**。

問題不是灰底或白底哪個正確，而是同一畫面的 Surface 是否有一致、完整的層級規劃。

---

## 9. Surface Model — `APPROVED` concept / `OPEN` final recipes

### 9.1 Continuous

適合高密度資料輸入、發票、帳務。

- Window：Neutral / Theme 對應背景。
- Workspace：inherit。
- Section：transparent / inherit。
- Control / Table：通常白色或 Control Surface。
- Raised：只在真正需要凸顯的結果／預覽使用。

### 9.2 Sectioned

適合一般桌面工具、左右工作區、預覽型程式。

- 主 Workspace 有清楚區域。
- Section 仍以 inherit 為主。
- 少量真正獨立區塊使用 Border / Raised / Subtle。

### 9.3 Carded

適合流程型、批次工具、結果摘要。

- Card 只代表真正獨立工作單元。
- 不把 Card 當一般排版工具。
- 避免 Card → Card → Card 巢狀。

### 9.4 尚未完成

Blue / Warm / Teal 各自對應 `Window / Workspace / Section / Control / Raised / Subtle` 的**完整實際色值配方仍為 `OPEN`**。

---

# Part D — Geometry & Spacing

## 10. Spacing / Margin / Padding / Alignment — `OPEN`

這一章尚未正式討論完成。

先前草案的 `4 / 8 / 12 / 16 / 24 / 32 px` 只能視為舊候選，不是已定案標準。

尚需討論：

- App / Workspace 外邊距。
- Section 間距與 Section 內欄位間距的比例。
- Control 之間的橫向／縱向節奏。
- Label 與 Control 的距離。
- Button group 的視覺間距（不碰其擺放位置與操作流程）。
- Table / Header / Footer 周邊留白。
- 不同 density 對 spacing 的縮放方式。

核心目標：避免同畫面出現大量 7 / 11 / 13 / 19 px 等無規則間距，但也不以單一固定值鎖死所有 App。

---

## 11. Control Size — `APPROVED` principle / `PROVISIONAL` exact values

尺寸必須和字級成比例；各 App 可依情境選 Compact / Standard / Large。

### 11.1 Button

| 類型 | 建議高度 | 建議文字 |
|---|---:|---:|
| Compact | 30–32 px | 9–9.5 pt |
| Standard | 34–38 px | 9.5–10.5 pt |
| Large | 42–48 px | 10.5–12 pt |

### 11.2 TextBox / Input

因部分原生控制項沒有真正 vertical align，Input 高度應以**視覺置中**為目標，不應為了追求大尺寸造成文字明顯靠上／靠下。

目前建議：

| 類型 | 建議高度 |
|---|---:|
| Compact | 28–31 px |
| Standard | 31–35 px |
| Large | 36–40 px |

精確值仍需按 framework / font rendering 驗證。

---

## 12. Border / Radius — `APPROVED`

### 12.1 Button

- Standard：**4 px** 為代表值。
- Large：可 5–6 px。
- Compact / dense toolbar：可 2–4 px。
- 不大量使用膠囊型 Button。
- 同一畫面同類按鈕保持一致。

### 12.2 Input

- TextBox / ComboBox：**約 3 px 小圓角**。
- 視覺上比 Button 稍方，保持商務資料輸入的俐落感。

### 12.3 Table

- Table / Data Grid 可接近方角。
- 如果 framework 不易低成本做小圓角，方角完全可接受。

### 12.4 Border

- 一般 1 px。
- 不用厚重立體框、漸層或高光。
- 不為 1–2 px 或 radius 差異強迫自繪穩定原生控制項。

---

# Part E — Core Components

## 13. Button — `APPROVED`

### 13.1 寬度與水平 Padding

**原則化，不鎖死。**

- 各 App 依按鈕文字、位置與版面自由調整。
- 避免文字貼近邊緣。
- 同組按鈕可視情況等寬，但不是全域要求。
- 不因 Guide 強制所有按鈕固定寬度。

### 13.2 文字與高度比例

- Compact / Standard / Large 需使用對應字級。
- 大按鈕不能配小字。
- 一般 Button：Regular / Medium。
- Large Primary：可 Semibold / Bold。
- 文字水平、垂直置中；Icon + Text 時整組置中。

### 13.3 Primary

- 實心 Theme Accent。
- 白字。
- 1 px 同色邊框或視覺上無額外邊界。
- Hover：Accent 稍深。
- Pressed：再深一階。
- 不用漸層、一般不使用陰影。

### 13.4 Secondary

- **白底 / Control Surface + 淺灰 1 px Border + 深色文字**。
- Hover：非常淡的 neutral / theme tint。
- 預設不使用實心灰色大塊。

### 13.5 Danger

分兩層：

- 一般危險操作：白底 + Danger border + Danger text。
- 最終不可逆確認：可使用實心 Danger + 白字。

紅色不得作一般強調色。

### 13.6 Disabled

- 淡灰背景。
- 淡灰 Border。
- 中灰文字。
- 仍需清楚可讀，但一眼知道不可操作。

### 13.7 Focus

- 保留清楚 keyboard focus。
- 使用 Accent / Focus outline 或 border。
- 不因 focus 改變控制尺寸或造成 layout 跳動。

### 13.8 Icon + Text

- Icon 可使用，但文字是主要辨識。
- Icon 通常置於文字左側。
- Icon-only 只適合非常成熟、直覺或高密度工具列功能。
- 不要求每顆 Button 都有 icon。

---

## 14. TextBox / Numeric Input / Label — `APPROVED`

### 14.1 TextBox

- `Surface.Control` 背景。
- 1 px Neutral Border。
- 約 3 px 小圓角。
- 不使用內陰影／漸層。

### 14.2 文字垂直位置

**不要求技術上真正 Vertical Center。**

很多原生 TextBox 沒有可靠 vertical align；因此規範改成：

> 透過合適的字級與欄位高度，讓文字在視覺上接近垂直置中。

不應為此重寫穩定原生 TextBox。

### 14.3 Label 預設位置

商務桌面表單預設採**左側 Label + 右側 Control**：

```text
姓名     [____________]
地址     [____________]
聯絡電話 [____________]
```

- 同一 Section 使用固定 Label Column。
- Label 左邊界一致。
- Input 左邊界一致。
- Label Column 寬度由該 Section 的內容決定，不全 CYApps 鎖死。

### 14.4 中文短 Label 對齊

若低成本可行，可用中文字視覺分散方式讓短 Label 接近同寬，例如：

```text
姓　　名 [____________]
地　　址 [____________]
聯絡電話 [____________]
```

不強迫英文、混合字串或長標題使用分散對齊；也不為此建立高風險自繪 Label。

### 14.5 Top Label 例外

預設是 Left Label，但以下情況可例外放上方：

- 大型多行備註。
- 很長欄位名稱。
- 很窄的 Dialog / Pane。
- 明確更適合上方標題的特殊內容。

### 14.6 Read-only / Disabled

**外觀可做相同，不需要兩套視覺語言。**

- 淡灰 / Subtle surface。
- 灰 Border。
- 文字保持可讀。
- 不使用 Accent。

實際是否可複製文字屬功能行為，不靠外觀區分。

### 14.7 Numeric Input

- 預設**不要 spinner / 上下箭頭**。
- 若 framework 的 NumericUpDown 不易安全移除箭頭，可使用一般 TextBox + numeric validation。
- 金額、數量、單價、百分比等真正 numeric value 預設右對齊。
- 員工編號、統編、郵遞區號、發票號碼等 identifier 不因為由數字組成就強制右對齊。

### 14.8 Focus

- Focus 時以 Accent border 表示。
- 優先只換 border 色，不改 border thickness，避免尺寸跳動。
- 不做強烈 glow。

### 14.9 Error

- 1 px Danger border。
- 不整格填紅底。
- 不因 Error 改變控制尺寸。
- 可在欄位附近使用簡短 Danger helper text。

### 14.10 Placeholder

- 使用 Secondary 色。
- 不使用斜體。
- Placeholder 不能取代正式 Label。

---

## 15. ComboBox — `APPROVED`

- 與 TextBox 屬同一視覺家族。
- 高度、字級、背景、Border 邏輯接近。
- 不要求完全 pixel-identical。
- 原生 dropdown arrow 可保留。
- 不為 3 px radius 或箭頭造型強制重寫整個 ComboBox。

---

## 16. Tabs — `APPROVED`

- 低存在感。
- 背景與主要 Workspace 協調。
- Inactive：一般深灰文字。
- Active：Accent 底線（約 2 px）＋較強字重／文字對比。
- 不用大型 pill。
- 不用厚重立體傳統 Tab 外觀。
- 一般高度約 32–40 px；大型導覽才可更高。
- 不為完全一致強制重寫原生 Tab，只要能安全控制字體、Active state 與間距即可。

---

## 17. Section / Group / Divider / Card — `APPROVED`

### 17.1 Section

- 預設 `transparent / inherit`。
- 以 Section Title + spacing 建立層級。
- Divider 視需要使用。
- 不預設每個 Section 都是一張白 Card。

### 17.2 Section Title

- 約 11.5–13 pt 候選範圍。
- Semibold。
- `Text.Primary`。
- 不用 Accent 彩色大標題作為常態。

### 17.3 Divider

- 1 px。
- Neutral / low contrast。
- 不用黑線、雙線、陰影。

### 17.4 GroupBox

- 可以保留功能結構，不禁止原生 GroupBox。
- 視覺上優先轉成 Title + Divider 或輕框 Section。
- 不為了 Guide 強制重寫原本穩定 layout。

### 17.5 Card

Card 只用於真正獨立工作單元，例如：

- Preview。
- Result。
- Drop area。
- 明確獨立的 summary / workflow unit。

不要把 Card 當一般排版工具；避免灰背景上出現大量彼此切割的白色塊。

---

## 18. Table / ListView / DataGrid — `APPROVED`

### 18.1 基本方向

- 偏高密度、低裝飾、清楚掃描。
- 不走 48–60 px 高 row 的 Web 後台風格。

目前候選 row density：

| 類型 | Row Height |
|---|---:|
| Compact | 26–29 px |
| Standard | 30–34 px |
| Comfortable | 35–40 px |

精確值仍需實機驗證。

### 18.2 Header

- 淺 Neutral / Subtle surface。
- SemiBold。
- 不用深藍整條底、漸層或立體按鈕效果。
- Header 高度只需略高於 Body row。

### 18.3 Body

- Table 本體可使用白底，即使 Window 為灰底。
- Zebra row 可選，但差異必須非常淡。
- Grid line 應低對比。
- 若使用垂直欄線，必須精準對齊。

### 18.4 Alignment

- 一般文字：左對齊。
- 金額／純數值：右對齊。
- 短狀態／必要操作欄：可置中。
- Identifier 依欄位性質決定，不因含數字就自動右對齊。

### 18.5 Selection

- 使用 `Accent.Selection` / soft tint。
- 優先淡背景 + 深色文字，而不是高飽和深底反白。

### 18.6 Grid Continuity — MUST

如果 Table 使用欄線：

> Header 與 Body 必須共享同一組 column geometry；表頭格線必須順暢延續到表身。

任何肉眼可見的約 1 px 欄位錯位都視為 UI defect。

需涵蓋：

- Resize。
- 最大化／還原。
- scrollbar 出現／消失。
- 100% / 125% / 150% DPI。

如果某 framework 無法低成本可靠做到垂直欄線，**寧可取消垂直欄線，也不要保留錯位格線**。

### 18.7 CYInvoice Reference — `REFERENCE`

**實作附件：** [CYInvoice Table / List Implementation Reference](docs/CY_UI_REFERENCE_CYINVOICE_TABLE.md)

目前 CYInvoice 已開立發票清單被視為成熟的操作與表格完成品，可參考：

- Header / Body geometry。
- viewport width。
- scrollbar 補償。
- 欄寬計算。
- owner-draw 邊界。
- Resize 時重新 layout。

**但目前 CYInvoice 的顏色、字體、字級、row height、欄寬、padding 都不是新版 Visual Guide 的固定值。**

日後 CYInvoice 本身也可以依新版 Theme / Typography / Density 調整外觀。

---

# Part F — Secondary Controls

## 19. Checkbox / Radio — `APPROVED`

- 原生優先。
- Body 字級一致。
- 文字與控制項視覺垂直對齊。
- framework 低成本支援時可套 Theme Accent。
- Disabled 使用共同灰階語言。
- 不做大型 Web-style checkbox/radio。

---

## 20. Toggle — `APPROVED`

- 只用於真正「立即生效的 On / Off 狀態」。
- 不全面取代 Checkbox。
- 高度約 20–24 px 為候選。
- ON：Accent；OFF：Neutral gray。
- 動畫非必要。
- framework 無成熟 Toggle 時，直接用 Checkbox；不為動畫自繪控制項。

---

## 21. Progress / Loading — `APPROVED`

- Progress 保持細、簡潔。
- 一般高度約 8–12 px；重要流程可 12–16 px。
- 正常處理使用 Theme Accent。
- 文字放在 progress bar 外。
- 不確定進度可使用 framework 原生 spinner / marquee。
- 不為動畫追求統一而增加複雜度。

---

## 22. Scrollbar — `APPROVED`

- **原生優先，不客製 Scrollbar。**
- Table / TextArea / Panel / Preview 均同。
- 但 scrollbar 對 layout 的影響必須納入設計，不能造成欄位錯位、1 px 位移或最後一欄被切掉。

---

## 23. DatePicker / Tooltip / Slider — `APPROVED`

### DatePicker

- 高度與同列 TextBox 接近。
- 字級使用 Field role。
- 外框能低成本套同系統就套。
- Calendar popup 使用 framework 原生。

### Tooltip

- framework 原生即可。
- 用於 icon-only、截斷內容、不直覺小功能的輔助。
- 不能用 Tooltip 補救本身難以理解的主要 UI。

### Slider

- 非第一版核心元件。
- 有實際需求再使用，原生優先。

---

# Part G — Dialog / Status / App Shell

## 24. Dialog / MessageBox / Status — `PROVISIONAL`

這一組已提出完整方向，且先前 UI 樣本中的 Dialog / Danger Action 整體觀感獲使用者正面接受；但尚未逐條做最終確認，所以暫列 `PROVISIONAL`。

目前建議方向：

### 24.1 Dialog

- 原生 Windows / framework title bar 優先。
- CY 自己控制內容區的字體、Surface、Section、Input、Button。
- 不為漂亮全面改成 borderless / custom chrome。
- Footer 可使用淡 Divider；不一定另填灰色底。

### 24.2 MessageBox

- 簡單訊息允許使用原生 MessageBox。
- 複雜、多段、敏感、帳戶／設定／重要確認才使用 CY custom dialog。

### 24.3 Status

- Info / Success / Warning / Error 四角色。
- Soft background + 深色文字／icon。
- 不用整片高彩度紅／綠。
- 狀態不能只靠顏色，需保留文字。

### 24.4 Badge

- Badge 是少數允許 pill / 高 radius 的元件。
- Low-saturation soft background + 深色文字。

### 24.5 Danger confirm

- 一般 Danger：白底紅框。
- 最終不可逆確認：實心紅底白字。

### 24.6 Toast

- 非核心，MAY。
- 不要求所有 framework 實作。

---

## 25. Main Window Shell — `OPEN`

以下只是已提出但尚未正式通過的建議，不得視為定案：

- Native Windows title bar 優先。
- Internal Header 非必需；只有真的有持續資訊價值才使用。
- Header 若使用，傾向 compact 約 42–52 px，不做 70–80 px Hero Header。
- Header 不要只是重複 App 名稱；應顯示公司、環境、使用者、工作模式等有價值資訊。
- Header 背景候選：Neutral / White / Accent.Soft。
- Footer / Status Bar 非必需；若兩者都需要，傾向合併成單一薄 Bottom Strip。
- Footer 高度候選約 20–26 px，使用 Secondary typography。

這一章需後續由使用者正式確認。

---

# Part H — App Icon Family

## 26. Icon Family — `PROVISIONAL`

App Icon 不要求放 `CY`、公司 Logo 或共同字樣。

目標接近 Office / Adobe 的產品家族概念：

- 各 App 自己容易辨識。
- 共同的視覺重量、邊框語言、簡化程度與色彩策略形成家族感。
- 小尺寸 16 / 24 / 32 px 仍清楚。

### 26.1 CYInvoice Icon — `PRESERVE`

**現行 `apps/CYInvoice/assets/CYInvoice.ico` 視為已完成版本，不得為了新 Icon Family 而修改。**

它是其他新 Icon 的母體參考，不是待重製項目。

INV 的重要 DNA：

- 扁平。
- 高對比。
- 大型英文縮寫 `INV`。
- 下方兩條非常簡單的橫線。
- 不靠漸層、陰影、3D 細節。
- 小尺寸仍容易辨識。

### 26.2 目前較偏好的延伸方向

其他 App：

- 保留大型英文縮寫，如 `ACC`、`ENV`、`CAL`、`CVT`、`WM`。
- 縮寫本身不要被複雜圖形搶掉。
- 縮寫下方只搭配**一個極簡功能符號**。
- 功能符號應比 Concept V3 更簡化，避免縮小後糊成一團。
- INV 自己的兩條橫線完全不改。

概念例：

- ACC：大字 `ACC` + 極簡 money / accounting symbol。
- ENV：大字 `ENV` + 極簡 envelope。
- CAL：大字 `CAL` + 極簡 calculator。
- CVT：大字 `CVT` + 極簡 conversion arrows。
- WM：大字 `WM` + 極簡 watermark / stamp symbol。

### 26.3 保留的兩張候選概念圖

只保留兩張，不保留中間迭代：

- **V1 備案：** [icon-family-v1-backup.jpg](design/cy-desktop-visual-guide/icon-concepts/icon-family-v1-backup.jpg)
- **目前候選方向：** [icon-family-current-option.jpg](design/cy-desktop-visual-guide/icon-concepts/icon-family-current-option.jpg)

V1 是較立體、彩色、Office-like 的備案；目前候選則是從現行 INV DNA 延伸的扁平大縮寫方向。

### 26.4 尚未完成

- 各 App 正式 Master source（SVG / PNG）。
- 16 / 24 / 32 / 48 / 64 / 128 / 256 的實際小尺寸優化。
- 正式 ICO 生成與 Windows Taskbar / Explorer 驗證。
- 各 App 最終代表色。

---

# Part I — Cross-framework / Complexity Guardrails

## 27. 跨 Framework 實作原則 — `APPROVED`

- 優先實現**角色一致**，不要求底層 control 完全一樣。
- 原生控制項已足夠接近時，不為 pixel-perfect 重寫。
- 不因 1–2 px、radius、dropdown arrow 或 scrollbar 差異引入不穩定自繪元件。
- DPI、字型 rendering、OS theme 的合理差異可接受。
- 某 visual token 成本過高時，可採合理近似。

一句話：**看起來像同一家產品，比每個 pixel 一模一樣重要。**

---

## 28. Complexity Guardrails — `APPROVED`

1. 不因視覺規範強迫更換 framework。
2. 不因視覺規範重寫穩定原生控制項。
3. 不為精準 1–2 px 差異阻擋功能交付。
4. 不要求每個 App 實作所有 Theme。
5. 不要求每個 App 實作所有 Surface Model。
6. 不要求所有元件都有專用 component library。
7. 新規範先在少數代表 App / Shell 驗證，再擴散。
8. 規範應定義角色、比例與視覺結果，不應把每一個 padding / x-position 寫成硬限制。
9. App Override 應是少量必要差異，不是每支程式重新設計一次。

---

# Part J — App Examples（非正式配置）

## 29. Theme / Surface 搭配示例 — `REFERENCE`

以下只是幫助理解，不是各 App 已定案配置。

| App | Theme 示例 | Surface 示例 | 備註 |
|---|---|---|---|
| CYInvoice | CY Blue | Continuous | UX / 高密度商務參考；外觀仍可新版化 |
| CYAccounting | CY Blue / Teal | Continuous / Sectioned | 成熟 business styling 參考 |
| CYEnvelope | CY Warm | Sectioned | 半成品；未來適合重整工作台視覺 |
| TriINVCalc | CY Teal | Continuous / Sectioned | 保留右側發票視覺身份 |
| CYWatermark / Harness | CY Blue / Slate | Carded / Sectioned | 視覺參考；操作效率不能盲目照抄 |

---

# Part K — UI Shell Validation

## 30. 無引擎 UI Shell — `OPEN`, planned after Visual Guide

Visual Guide 大致定案後，不直接修改正式引擎；先做代表 App 的 UI Shell Prototype。

Shell 原則：

- UI 是真的。
- 資料與引擎是假的。
- 不連正式 API / DB。
- 不開立發票。
- 不做正式浮水印處理。
- 不真的列印或修改正式資料。

可實際測：

- Resize。
- Tab。
- TextBox / ComboBox。
- Table selection。
- Hover / Pressed / Disabled / Focus。
- Dialog。
- Fake progress / fake status。
- 100% / 125% / 150% DPI。

### 30.1 第一輪代表 App 建議

- CYInvoice：高密度商務輸入代表。
- CYWatermark / Harness：流程型工具代表。
- CYEnvelope：資料輸入 + Preview workbench 代表。

### 30.2 技術原則

殼應盡量使用正式 App 將使用的 framework，而不是全部用同一種 prototype framework，避免 mock 看起來漂亮但正式技術做不到。

---

# Part L — Phase Boundary

## 31. Phase 1 範圍

Phase 1 只處理：

- Color / Theme。
- Surface。
- Typography hierarchy。
- Border / Radius。
- Control size relationship。
- Spacing（尚未完成）。
- Button / Input / ComboBox / Tab / Section / Table。
- Secondary controls。
- Dialog / Status 外觀。
- Main Window Shell 外觀。
- App Icon Family。

---

## 32. Phase 2 — `PHASE 2`

刻意不在 Phase 1 決定：

- 功能流程。
- 控制項到底放左／右／上／下。
- Primary Action 最終擺放位置。
- Tab 應有哪些頁面。
- Enter / Esc / Tab 鍵行為。
- Dialog 何時出現。
- 驗證與錯誤處理流程。
- 表單欄位順序。
- 搜尋／查詢流程。
- 工作流程步驟數量。
- 快捷鍵。
- 大型 layout / IA / navigation 重構。

Phase 1 的目的只有一個：

> **先讓不同 CYApps 在不改變既有操作邏輯的前提下，逐步形成一致、現代、簡潔且可維護的商務桌面視覺語言。**

---

# 33. 恢復工作時的最短指引

若後續聊天中斷、換新對話或換執行環境：

1. 讀本文件最上方 CAUTION，確認它仍是 Draft，不得影響現行開發。
2. 讀 `0. 對話續作 Checkpoint`。
3. 不重新討論 `APPROVED` 項目，除非使用者主動要求修改。
4. 優先接續 `OPEN` 項目。
5. `PROVISIONAL` 項目應透過實際 UI mock / Shell / DPI 驗證，而不是只靠文字反覆微調。
6. CYInvoice Table 讀 `docs/CY_UI_REFERENCE_CYINVOICE_TABLE.md`。
7. Icon 只保留兩張候選概念圖；**現行 CYInvoice Icon 不可因本 Guide 被修改。**
8. 在使用者明確核准以前，本文件仍不能變成治理規則或現行開發門檻。
