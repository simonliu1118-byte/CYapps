# CY Desktop Visual Guide — Draft

> [!CAUTION]
> **本文件仍在討論、試作與真機驗證階段，尚不是目前有效的開發規範。**
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
**Phase 1 紙面設計：COMPLETE；下一步為 UI Shell / DPI / Windows 真機驗證**  
**本次 checkpoint：2026-09-23（含 Button V7、Input Lab、Theme Lab、Icon Family Geometry 驗證）**  
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
| 主要字體 | `APPROVED` | `Microsoft JhengHei UI`；fallback `Microsoft JhengHei → Segoe UI → system sans-serif` |
| Typography hierarchy | `APPROVED` | 統一角色與相對層級，不要求所有 App 使用完全相同 pt |
| Standard Input 字級 | `REFERENCE` | WinForms / 96 DPI 已驗證 10 pt + 原生 natural height；不是跨 App 硬規定 |
| Density | `APPROVED — APP CHOICE` | 可用 Compact / Standard / Comfortable 描述密度，但不規定跨 App 固定字級／欄位高度；高密度應自然使用較小字、較小欄位與較緊間距 |
| Theme 數量 | `APPROVED` | **Blue / Teal / Coral / Apricot 共四種；沒有 Warm Theme** |
| Theme HEX | `APPROVED` | Theme Lab 已確認目前 Blue / Teal / Coral / Apricot Accent / Hover / Pressed / Soft / Focus / Selection 組合 |
| State Colors | `APPROVED` | Info / Success / Warning / Danger 與 Theme 分離；Danger 不得跟 Coral 混用 |
| Surface Roles | `APPROVED` | Window / Workspace / Section / Control / Raised / Subtle / Border / Divider |
| Surface Models | `APPROVED` | Continuous / Sectioned / Carded；Card 只用於真正獨立工作單元 |
| Spacing / Margin / Padding / Alignment | `APPROVED` | 使用有限 spacing scale；Section 間距大於 Section 內間距；維持 alignment grid |
| Button | `APPROVED` | 一般按鈕 Native-first；關鍵 Primary / Danger 可用成熟 owner-paint 強化 Theme；寬度 content-driven |
| WinForms Theme Button | `REFERENCE` | V7 已驗證 Native Button subclass + owner-paint；固定 2px radius，不隨高度放大 |
| Dialog Button Size | `APPROVED — ADVISORY` | 優先 Compact / 小 Standard；通常不使用 Large；一般寬度由文字 + padding 決定 |
| Dialog Button Position | `APPROVED — ADVISORY` | 商務程式靠右可作預設參考，但**不寫死**；個案可置中或其他合理排列 |
| TextBox / Numeric / ComboBox | `APPROVED` | Native-first；單行欄位不強拉高度；Label / Input alignment、Numeric / Focus / Error 原則已定 |
| Tabs | `APPROVED` | 低存在感；可保留原生 TabControl 行為並只 owner-draw header |
| Section / Group / Divider | `APPROVED` | Section 預設 inherit/transparent；Card 不作一般排版容器 |
| Table / List | `APPROVED` | 高密度商務表格；Grid Continuity 為 MUST |
| CYInvoice 清單 | `REFERENCE` | 參考 geometry / viewport / scrollbar / owner-draw；現行色值與欄寬不是新標準 |
| Secondary Controls | `APPROVED` | Native-first；Scrollbar 不客製；Toggle 只用於真正 On/Off |
| Menu / Context Menu | `APPROVED — ADVISORY` | Native-first；低密度裝飾；Danger item 可用 Danger text |
| TreeView / Sidebar / Navigation List | `APPROVED — ADVISORY` | 低存在感；Active 使用 soft accent；不預設深色 Sidebar 或 pill navigation |
| Splitter / Pane Divider | `APPROVED` | 視覺線保持細；hit area 可比線寬；不做粗灰 bar |
| Empty / No Data / Drop Area | `APPROVED — ADVISORY` | 簡潔文字與小圖示；Drop Area 可使用淡 dashed border；不做巨大插畫 |
| Toolbar / ToolStrip | `APPROVED — ADVISORY` | 沿用 Button / Icon / Divider 系統；Native-first；功能擺放留 Phase 2 |
| Accessibility baseline | `APPROVED` | Focus 不消失、狀態不只靠顏色、文字保持可讀、不破壞原生 High Contrast |
| Dialog / MessageBox / Status | `APPROVED — ADVISORY` | **簡單訊息／確認優先原生 MessageBox；MessageBox 足夠時不另造 Custom Dialog** |
| Main Window Shell | `APPROVED — ADVISORY` | Minimal / Business / Workbench 只是參考模式，不是強制模板 |
| App Icon Family | `PROVISIONAL` | 以現行 INV 為 family master；幾何／Live Area 基準已建立，新 App master / ICO 尚未製作 |
| Icon Family Geometry | `REFERENCE` | INV 實測 Live Area 約佔 Canvas 寬 80–83%、高 86–88%；48px 層約 39×42px，允許小尺寸 optical adjustment |
| 現行 CYInvoice Icon | `PRESERVE` | **完成版，不得因 Icon Family 設計而改動** |
| Phase 1 紙面設計 | `APPROVED` | 視覺項目盤點完成；不再主動擴張新規範 |
| UI Shell Prototype | `OPEN` | CYInvoice 已進入真機細節驗證；完整代表 App / DPI 驗證尚未完成 |
| UX / Layout / 操作流程 | `PHASE 2` | Phase 1 不鎖死 |

## 0.3 下一步

1. **停止繼續增加 Phase 1 視覺規格。**
2. 延續代表 App 的無引擎 UI Shell，驗證 100% / 125% / 150% DPI。
3. 依真機結果只微調 `PROVISIONAL` 數值，不重新推翻已核准的角色／原則。
4. Icon 正式 Master / ICO 等到實際導入各 App 時製作與驗證；幾何應以現行 INV master 為基準。
5. Phase 1 真機確認後，再進 Phase 2 Layout / Interaction / UX。

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

## 5. Typography Roles — hierarchy `APPROVED`, values `RECOMMENDED RANGE`

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

### WinForms Standard Input — `REFERENCE`

CYInvoice Input Lab 在 Windows 96 DPI / 100% 下確認：

- `Microsoft JhengHei UI 10 pt` 的中文可讀性比 9.5 pt 更穩定；
- 原生 TextBox / ComboBox / DatePicker 不需為了視覺對齊強制同 Height；
- 實測自然高度約為 TextBox 24 px / ComboBox 25 px / DatePicker 24 px；
- 這是 WinForms / 96 DPI reference，不是所有 App / framework 的固定數值。

## 6. Density — `APPROVED — APP CHOICE`

`Compact / Standard / Comfortable` 可以繼續當作描述畫面資訊密度的語言，但**不再定義成跨 App 必須遵守的固定字級、TextBox 高度、Button 高度或 Row Height 套餐**。

核心原則：

- 高密度畫面應自然使用較小字、較小欄位、較緊間距與較高資料量；
- 一般畫面可使用 Standard；低資訊量／Preview 等可較寬鬆；
- 各 App 依資料量、framework natural size、螢幕空間與工作流程自行決定；
- 同一頁不要無理由混用明顯不同密度；
- 不為了符合 Density 名稱而破壞原生 control 的自然高度；
- Dialog 可與主畫面採不同密度，只要自身一致。

因此，不再把「Compact = 9.5 pt / 某固定 px」或「Standard = 某固定 control height」當作 CY 跨程式規範。

## 7. DPI — `APPROVED`

至少驗證：100% / 125% / 150%。

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

## 9. Theme Set — `APPROVED`

**第一版就是四種：Blue / Teal / Coral / Apricot。沒有 Warm Theme。**

Theme 只改 Accent 家族，不重新建立另一套 Typography / Neutral / Control system。

Theme Lab 已在 Windows 真機同畫面比較確認以下四色可作第一版基準。

### 9.1 Blue

明亮、乾淨的商務藍；不使用早期灰沉候選。

| Token | Value |
|---|---|
| `Accent` | `#2563EB` |
| `Hover` | `#1D4ED8` |
| `Pressed` | `#1E40AF` |
| `Soft` | `#DBEAFE` |
| `Focus` | `#60A5FA` |
| `Selection` | `#DBEAFE` |

### 9.2 Teal

清爽青綠，不使用灰沉 teal。

| Token | Value |
|---|---|
| `Accent` | `#0D9488` |
| `Hover` | `#0D8076` |
| `Pressed` | `#0F766E` |
| `Soft` | `#D1F2EB` |
| `Focus` | `#2DD4BF` |
| `Selection` | `#CCFBF1` |

### 9.3 Coral

**珊瑚紅／粉紅方向**；與 Apricot 橘系、Danger 正紅清楚分開。

| Token | Value |
|---|---|
| `Accent` | `#D4657B` |
| `Hover` | `#C4566E` |
| `Pressed` | `#AD485E` |
| `Soft` | `#FCE8ED` |
| `Focus` | `#E39BAC` |
| `Selection` | `#F7DCE3` |

### 9.4 Apricot

| Token | Value |
|---|---|
| `Accent` | `#D8844A` |
| `Hover` | `#C3733F` |
| `Pressed` | `#AA6435` |
| `Soft` | `#FAECDD` |
| `Focus` | `#E0A178` |
| `Selection` | `#F6E3D1` |

## 10. State Colors — `APPROVED` roles / `PROVISIONAL` HEX

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

優先 spacing scale：`4 / 8 / 12 / 16 / 24 / 32 px`。

不是硬性 pixel lock；framework / DPI 可小幅近似。

常見節奏：4 為微距、8 為同組、12 為一般欄位、16 為小區塊、24 為主要 Section、32 為大區塊／頁面留白。

## 14. Margin / Padding — `APPROVED` principle

- Window / Page margin 約 12–32 px 依 density 選擇。
- 高密度商務通常 12–16 px。
- 一般工具 16–24 px。
- Preview / 低密度 24–32 px。
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

以上為 Button 自身常見參考，不代表 App 必須採用某一 Density 套餐。Large 主要給主工作畫面的強 Primary Action，不是 Dialog Footer 的常態。

### 16.2 Width

**Content-driven。**

- 依文字 + 合理水平 padding 決定。
- 一般 horizontal padding 約 14–20 px 可作參考。
- 同組可等寬，但以最長文字的自然需求為基準。
- 不因視窗很寬就把 Button 過度拉長。
- 不為填滿 Footer 做兩顆半寬大型按鈕。

### 16.3 Dialog Button Size — `APPROVED — ADVISORY`

- 優先 Compact / 小 Standard，約 30–34 px 高。
- 常見短按鈕自然寬度約 72–100 px；稍長文字約 100–120 px。
- 這些是參考範圍，不是硬限制。

### 16.4 Native-first / Geometry

- **一般 Secondary / 普通功能按鈕優先使用 OS / framework 原生 Button。**
- 沒有特殊 Theme 色、Danger 強調或其他明確視覺效果需求時，原生 Button 為首選。
- 不為了一般 1–2 px radius 差異重做 Button 行為。
- 不做一般 pill Button。
- 原生 Button 的圓角跟隨 OS / framework，不要求跨 framework 精確相同。
- 關鍵 Primary / Danger 若 Theme 色有明顯視覺價值，可採成熟且已驗證的 owner-paint；仍應保留原生 Button 作為底層控制。

#### WinForms Theme Button — `REFERENCE`

已用 CYInvoice Visual Shell V7 驗證：

- subclass 原生 `Button`，只接管外觀繪製；不以 `UserControl` 重新發明按鈕。
- Standard / Large / Danger 使用**固定約 2 px radius**；不因按鈕高度增加 radius。
- 100% 參考渲染中，以約 **1.5 px 等效垂直 paint inset** 讓可見色塊高度更接近 Windows 原生按鈕。
- AntiAlias；先清 Parent background；Fill / Border 共用同一 rounded path。
- Hover / Pressed 只改顏色，不改 geometry。
- 必須保留 Click / keyboard / focus / tab / accessibility 等原生 Button 行為。
- 若 owner-paint 無法穩定做到乾淨邊角／DPI，直接退回原生 Button。

詳見：[CY WinForms Theme Button Reference](docs/CY_UI_REFERENCE_WINFORMS_THEME_BUTTON.md)

**2 px 是 WinForms 已驗證 Reference，不是 Qt / Win32 / Go 的跨框架硬規定。**

### 16.5 Primary / Secondary / Danger

- Primary：可使用 Accent solid + White text；Hover / Pressed 使用 Theme 階層；一般不使用 gradient / shadow。
- Secondary：一般優先原生 Button；若採客製視覺則 White / Control Surface + Neutral Border + Text.Primary。
- Danger：Danger 與 Theme 分離；需要強調的關鍵 Danger 可使用 owner-painted Danger Red；一般破壞性操作不必每顆都染紅。
- **Native Secondary + Theme Primary / Danger 可以同組混排**，前提是角色層級明確、可見高度與圓角比例協調。
- 不因為可以 owner-paint 就把所有 Button 全部 Theme 化。

### 16.6 Disabled / Focus

Disabled 仍可讀；Focus 明確但不改變 geometry，不做 glow。

### 16.7 Icon + Text

Icon 是輔助，文字是主要辨識；Icon-only 只用於成熟、直覺、高密度功能。

---

## 17. TextBox / Numeric / Label — `APPROVED`

- Background：Surface.Control。
- Border：優先原生／1 px Neutral 視覺。
- **單行 TextBox 不為追求統一框高強制拉高。** WinForms 等原生控制若無法真正垂直置中文字，應讓 control 高度只比文字自然高度多合理餘量，而不是做高框後讓文字貼上緣。
- 同列 TextBox / ComboBox / DatePicker 必須以**實際可見高度與文字基準**對齊，不只看設定的 `Height` 數字。
- 優先依 framework natural / preferred height，再由 layout 對齊；不為垂直置中另外包自繪外框。
- WinForms 一般 Standard Input 可優先參考 `Microsoft JhengHei UI 10 pt + natural height`；其他密度／framework 由 App Choice 決定。
- Disabled / Read-only 可使用相同淡灰視覺；Read-only 內容仍應清楚可讀。
- Focus 不應改 geometry；原生 focus 能力足夠時不另造外框。
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

- 與 TextBox 同視覺家族與同列對齊基準。
- 原生 dropdown arrow 保留。
- **不為 radius / arrow 再包一層自繪外框。**
- 若原生 TextBox 與 ComboBox 設定相同 Height 仍呈現不同可見高度，以 framework natural height / 字體與 layout 微調到視覺一致，不用雙層邊框補救。

---

## 19. Tabs — `APPROVED`

- 低存在感。
- Active：Accent 約 2 px underline + 稍強字重。
- Inactive：Neutral text。
- 約 32–40 px 高為常見參考。
- 不做大型 pill / hero navigation。
- WinForms 可保留**原生 `TabControl` 的 selection / keyboard / notification / page 行為，只 owner-draw Header 外觀**；不要為了漂亮重做整套 TabHost。

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

表格可依 App 實際資料密度自行決定 Row Height；下列只作常見參考，不與全域 Density 名稱綁死：

| 類型 | Row Height 參考 |
|---|---:|
| 高密度 | 約 26–29 px |
| 一般 | 約 30–34 px |
| 寬鬆 | 約 35–40 px |

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

# Part F — Secondary Controls & Supporting States

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

DatePicker 字體／可見高度與同列 Input 接近，popup native；不為圓角另外包自繪殼。Tooltip native。Slider 非核心，有需求再用。

## 27. Menu / Context Menu — `APPROVED — ADVISORY`

- Native-first，不為統一全面自繪 Windows ContextMenu / MenuStrip。
- White / Neutral surface，字級跟 Body 接近。
- Item 高度維持桌面密度，不做大型 Web Menu。
- Hover 使用非常淡的 neutral / Accent.Soft。
- Separator 使用 Neutral.Divider。
- Shortcut 可靠右顯示；submenu arrow 原生即可。
- Danger item 可用 Danger text，但不預設整條紅底。
- Icon 可有可無；同一 menu 內避免混雜多種 icon 語言。

## 28. TreeView / Sidebar / Navigation List — `APPROVED — ADVISORY`

- 不預設做成深色 Sidebar。
- 背景可為 White / Window / Subtle。
- Active item 使用 Accent.Soft + 較強文字／細 indicator。
- Hover 保持低存在感。
- Row 約 30–38 px 可作 density 參考。
- 不把每個項目包成 pill 或 Card。
- Tree 子層級主要靠 indentation；expand arrow 原生優先。
- 哪些項目、順序、導覽架構留 Phase 2。

## 29. Splitter / Pane Divider — `APPROVED`

- 視覺 Divider 通常約 1 px Neutral。
- 可拖曳 splitter 的 hit area 可以比視覺線寬，方便操作。
- Hover 可輕微 Accent，但不做粗灰 bar。
- Resize 時兩側 layout 不得亂跳或破壞 alignment grid。
- 是否可拖曳、初始比例屬 App Choice / Phase 2。

## 30. Empty / No Data / Drop Area — `APPROVED — ADVISORY`

- 桌面工具感優先，不做巨大插畫式 Empty State。
- Title 約 Body～Section 級；Secondary text 說明下一步。
- 小圖示約 32–48 px 即可，有辨識價值才使用。
- Drop Area 可用 1 px 淡 dashed border；drag hover 才使用 Accent.Soft。
- 若有 action，直接沿用正常 Button system。

## 31. Toolbar / ToolStrip — `APPROVED — ADVISORY`

- 沿用既有 Button / Icon / Divider / Density 規則。
- Native ToolStrip / Toolbar 已穩定時可保留。
- 不為了視覺全面重畫。
- Icon 尺寸與間距需一致；separator 使用淡 Divider。
- Toolbar 應放哪些功能、排列與優先順序留 Phase 2。

## 32. Accessibility Baseline — `APPROVED`

第一版不建立大型 Accessibility Design System，但以下是底線：

- Keyboard focus 不得消失。
- Status / Error / Success 不得只靠顏色傳達。
- Disabled / Secondary 文字仍需可讀。
- 不使用低到難辨識的文字對比。
- 原生控制項對 Windows High Contrast / OS Theme 的支援不應被客製繪製刻意破壞。

---

# Part G — Dialog / MessageBox / Status

## 33. Dialog / MessageBox / Status — `APPROVED — ADVISORY`

### 33.1 Dialog Shell

- Native title bar / window behavior 優先。
- CY 統一內容區 typography / surface / input / button。
- 不為漂亮全面改 borderless / custom chrome。
- Footer 可有 1 px divider；不必另做厚重背景。

### 33.2 Native MessageBox

**能由原生 MessageBox 完整表達的簡單訊息／警告／OK／Yes-No／Question／覆蓋確認，優先直接使用原生 MessageBox，不另建 Custom Dialog。**

- 按鈕排列跟隨原生，不重排。
- Window size、DPI、Enter / Esc、Focus 等交給 framework / OS。
- 不為了套 Theme 色而把單純 MessageBox 改成 Custom Form。

### 33.3 Custom Dialog 使用時機

只有 MessageBox 無法合理承載時才使用，例如：表單、密碼輸入、設定、Preview、多項選擇、大量詳細資訊、展開技術細節、多步驟 Wizard、需要額外資料呈現的重要 destructive confirm。

### 33.4 Button Position — `APPROVED — ADVISORY`

**不寫死。**

- 一般 Windows 商務程式常見的右下 Action Group，可作預設參考。
- 若小型 Dialog、內容重心、視窗寬度或 App 本身設計更適合置中，可個案置中。
- Wizard / 大型工作型 Dialog 也可依版面合理排列。
- 不能為了「一致」而犧牲視覺平衡與操作效率。

真正固定的是：按鈕不要過度拉寬、同組間距一致、Primary / Secondary / Danger 語意清楚。

### 33.5 Danger Confirm — MUST

Danger 與 Theme 分離。簡單確認使用原生 MessageBox 時，以明確文案／警告 icon 表達；Custom Dialog 若需要強調最終不可逆操作，可使用 Danger Red Theme Button。

### 33.6 Error

主訊息先用人話說明結果；HTTP code / API response / stack trace 等技術資訊可折疊或另顯示。

### 33.7 Status Banner / Badge / Validation

- Banner：Soft state background + 1 px related border + icon/text；不使用整片高飽和紅／綠。
- Badge：可使用 pill；小型、低彩度、soft background + deep text；不是 Button。
- Inline Validation：1 px Danger border + 附近短訊息；不可造成 layout jump。

### 33.8 Processing / Toast

短動作可把按鈕改為「處理中…」並 Disabled；有明確進度用 Progress；時間不確定用 spinner / marquee。Toast 為 MAY，不要求所有 framework 實作。

---

# Part H — Main Window Shell

## 34. Main Window Shell — `APPROVED — ADVISORY`

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

## 35. Icon Family — `PROVISIONAL`

App Icon 不要求放 `CY` 或公司 Logo。

目標：像 Office / Adobe 一樣，同一家族但各 App 可辨識。

### 35.1 CYInvoice — `PRESERVE`

**`apps/CYInvoice/assets/CYInvoice.ico` 是完成版，不得為 Icon Family 改動。**

INV 是現行 Icon Family 的 **master / geometry reference**。未來 ACC / ENV / CAL / CVT / WM 等新圖示應往 INV 靠攏，而不是反過來修改 INV 以配合新圖示。

INV DNA：

- 扁平。
- 高對比。
- 大型 `INV`。
- 下方兩條簡單橫線。
- 無 gradient / shadow / 3D 細節。
- 小尺寸可辨識。

### 35.2 Family Geometry — `REFERENCE`

**Canvas size 不等於實際圖形尺寸。** 新圖示即使同樣輸出 48×48，如果有人只畫到 30×30、有人畫到 46×46，Windows 桌面上仍不會有同一家族感。

正式 `CYInvoice.ico` 內含原生：

`16 / 24 / 32 / 48 / 64 / 128 / 256 px`

以 Alpha / 可見 artwork 實測得到：

| Canvas | INV Visual Live Area | 寬度佔比 | 高度佔比 | Near-opaque Core |
|---:|---:|---:|---:|---:|
| 16 × 16 | 約 13 × 14 | 81.2% | 87.5% | 約 12 × 12 |
| 24 × 24 | 約 20 × 21 | 83.3% | 87.5% | 約 18 × 20 |
| 32 × 32 | 約 26 × 28 | 81.2% | 87.5% | 約 24 × 27 |
| 48 × 48 | 約 39 × 42 | 81.2% | 87.5% | 約 38 × 41 |
| 64 × 64 | 約 52 × 55 | 81.2% | 85.9% | 約 50 × 55 |
| 128 × 128 | 約 103 × 112 | 80.5% | 87.5% | 約 101 × 110 |
| 256 × 256 | 約 206 × 222 | 80.5% | 86.7% | 約 206 × 222 |

因此目前 Family 的核心視覺量體 reference 為：

> **Live Area 約佔 Canvas 寬 80–83%、高 86–88%。**

這是**視覺量體目標**，不是要求所有 App 把向量座標機械式縮放到完全一樣。

#### 48 px Desktop Reference

48×48 層是一般 Windows Desktop 比較最有用的基準：

- Visual Live Area：約 **39×42 px**。
- Near-opaque core：約 **38×41 px**。
- 左右 optical margin 約 **4–5 px**。
- 上下 optical margin 約 **3 px**。

新 Icon 在 48 px 應直接與 INV 並排比較 apparent size / visual weight，而不是只檢查 canvas 是否同為 48×48。

#### Abbreviation Zone — CORE

- 大型英文縮寫是主要辨識元素。
- `INV / ACC / ENV / CAL / CVT / WM` 等縮寫應有接近的**視覺 bounding box 與 weight**。
- 不要求不同字母組合使用完全相同 font size；若 `INV`、`ACC` 等因字形天然寬窄不同，可做少量 optical adjustment。
- 可微調字級、水平縮放、tracking 或 x-position，但目的只能是讓可見字塊大小更一致，不是每個 App 自行發展另一套字體比例。

#### Symbol Zone — CORE

- 縮寫下方只放**一個極簡功能符號或 motif**。
- Symbol 必須是 secondary，不得搶過 abbreviation。
- 高度區域、與縮寫的間距、整體 weight 應接近 INV 下方橫線 motif 的角色。
- 若功能符號本身太複雜，優先簡化符號，不可藉由放大整個 Icon 破壞 family Live Area。

#### Optical Centering — CORE

- 不要求上下左右 margin 數學上完全相同。
- 不同字形／圓形／斜線／底部偏重符號可做約 1–2 px 級 optical correction。
- 最終驗收以 Windows 實際顯示時「看起來置中、大小相近」為準。

#### Small-size Optimization — CORE

不可只把 256 px master 機械縮小後直接輸出全部 ICO layer。

至少逐一檢查：

`16 / 24 / 32 / 48 / 64 / 128 / 256 px`

小尺寸允許：

- stroke 微調；
- abbreviation / symbol 間距微調；
- 字塊小幅放大／縮小；
- 1 px 級 pixel snapping / centering；
- symbol 簡化；
- anti-aliasing 調整。

但 app identity、主要縮寫、family silhouette、色彩 identity、主次層級與大致 Live Area 比例不得在不同 layer 間改變。

完整量測、術語與驗收規則見：

[CY App Icon Family Geometry Reference](docs/CY_UI_REFERENCE_ICON_FAMILY_GEOMETRY.md)

### 35.3 未來 Family 延伸

其他 App 保留大型英文縮寫（ACC / ENV / CAL / CVT / WM 等），縮寫下方只搭配**一個極簡功能符號**；符號優先為小尺寸辨識服務，不做複雜主圖。

新 Icon 不是「只要塞得進 ICO canvas 就合格」，還必須符合 35.2 的 visual mass / abbreviation / symbol / optical centering 原則。

### 35.4 只保留兩張概念圖

- [V1 備案](design/cy-desktop-visual-guide/icon-concepts/icon-family-v1-backup.jpg)
- [目前候選](design/cy-desktop-visual-guide/icon-concepts/icon-family-current-option.jpg)

中間迭代不保留。

### 35.5 尚未完成

- 各 App Master SVG / PNG。
- 新 App 的 16 / 24 / 32 / 48 / 64 / 128 / 256 小尺寸專層優化。
- 新 App ICO 生成。
- Windows Desktop / Taskbar / Explorer 真機並排檢查。
- 各 App 最終代表色。

這些延後到實際導入 App 時處理，不阻擋 Phase 1 Shell 驗證。

---

# Part J — Cross-framework / Complexity Guardrails

## 36. Cross-framework — `APPROVED`

- 角色一致 > 底層 control 完全一樣。
- Native control 足夠接近時不重寫。
- 不因 radius、arrow、scrollbar、一般 1–2 px 細節引入**不穩定**自繪。
- **例外：已有成熟、經真機驗證的有限 owner-paint，可用於有明確視覺價值的關鍵元件，例如 WinForms Theme Primary / Danger Button。**
- 這類 owner-paint 應盡量只接管外觀，不重寫原生 control 行為。
- DPI / font rendering / OS theme 的合理差異可接受。
- visual token 成本過高可採合理近似。

## 37. Complexity Guardrails — `APPROVED`

1. 不因 Guide 強迫更換 framework。
2. 不因 Guide 重寫穩定原生控制項。
3. 不為一般 1–2 px 差異阻擋功能交付；**Table Grid Continuity 是例外，明顯錯位仍是 defect。**
4. 不要求每個 App 實作所有 Theme。
5. 不要求每個 App 實作所有 Surface Model。
6. 不要求所有元件都建立專用 component library。
7. 先在代表 App / Shell 驗證再擴散。
8. 優先規範角色、比例、視覺結果，不把所有 x/y/padding 寫成硬限制。
9. App Override 是必要差異，不是重新設計。
10. Phase 1 紙面規格完成後，不因「可能還用得到」持續擴張元件清單；真正出現需求再補。

---

# Part K — App Example（非正式配置）

## 38. Theme / Surface Example — `REFERENCE`

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

## 39. 無引擎 UI Shell — `OPEN`

**這是 Phase 1 接下來的主要工作，不再先增加紙面規格。**

不直接改正式引擎；先做 UI Shell Prototype：

- UI 是真的。
- 資料／引擎是假的。
- 不連正式 API / DB。
- 不正式開立發票／列印／浮水印／修改資料。

真機測：

- Resize / maximize / restore。
- Tabs / Inputs / Combo / Table selection。
- Hover / Pressed / Disabled / Focus。
- Dialog / Status / fake Progress。
- Menu / Context Menu / Navigation / Splitter / Empty State（該 Shell 有需要時）。
- 100% / 125% / 150% DPI。

第一輪代表：

- CYInvoice：高密度商務資料輸入。
- CYWatermark / Harness：Workflow 工具。
- CYEnvelope：Input + Preview Workbench。

Prototype 優先使用各 App 正式 framework，避免 mock 做得到、正式 framework 做不到。

### 39.1 已確認的 CYInvoice Visual Shell Findings

- 標準互動 Control 應維持 Native-first；V2 曾因 Button / ComboBox / DatePicker 等自繪過多而產生不一致，已明確淘汰該方向。
- Blue / Teal 實作必須使用已核准的亮藍／青綠樣本，不使用早期灰沉候選；Coral 為珊瑚粉紅，Apricot 為杏橘，Theme Lab 同畫面比較已確認第一版四色。
- WinForms Standard Input 在 96 DPI / 100% 已確認 10 pt + framework natural height 為良好一般參考；Compact / 高密度不訂跨 App 固定數值。
- Tab 可保留低存在感 owner-draw Header，但底層行為維持原生 TabControl。
- 簡單說明／確認用原生 MessageBox，不為展示視覺另外做 Custom Form。
- WinForms 關鍵 Theme Button 的 V7 2px fixed-radius owner-paint 已由使用者確認可定案；一般按鈕仍優先原生。

### 39.2 驗證後可以改什麼

主要調整 `PROVISIONAL`：

- framework / App 個別 Typography 微調。
- Control / Row 實際高度。
- State Colors HEX 的小幅修正。
- 特定 App density。
- 新 App Icon optical adjustment。

原則上不重新推翻已 `APPROVED` 的角色、視覺層級與 Complexity Guardrails，除非真機證明確實有問題或使用者主動改案。

---

# Part M — Phase Boundary

## 40. Phase 1 — 紙面設計完成

已處理：Color / Theme / Surface / Typography / Density / Border / Radius / Spacing / Alignment / Core Components / Secondary Controls / Supporting States / Dialog / Status / Shell Visual / App Icon Family direction / Icon Geometry reference。

目前 Phase 1 剩下的是**真機驗證與 provisional 數值微調**，不是再增加更多紙面設計項目。

## 41. Phase 2 — `PHASE 2`

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

**Dialog Button 的靠右／置中也屬可依個案調整的 Layout 決策，本 Guide 只提供商務桌面常見建議，不做硬限制。**

---

# 42. 恢復工作時最短指引

1. 先讀最上方 CAUTION：本文件仍是 Draft，不得干擾現行專案。
2. 讀 `0. 對話續作 Checkpoint`。
3. Phase 1 紙面設計已完成，**不要再主動擴張新元件規範**。
4. 不重新討論 `APPROVED`，除非使用者主動要求修改。
5. `PROVISIONAL` 以 UI Shell / Windows 真機驗證為主，不靠文字無限微調。
6. Table 參考 `docs/CY_UI_REFERENCE_CYINVOICE_TABLE.md`；WinForms Theme Button 參考 `docs/CY_UI_REFERENCE_WINFORMS_THEME_BUTTON.md`。
7. Icon Geometry 參考 `docs/CY_UI_REFERENCE_ICON_FAMILY_GEOMETRY.md`；現行 CYInvoice Icon 是 master / `PRESERVE`，新 Icon 往 INV 靠，不反過來改 INV。
8. Theme 第一版就是 **Blue / Teal / Coral / Apricot**，不要恢復舊 Warm Theme；Blue / Teal 不回到灰沉舊候選。
9. Density 是 App Choice；WinForms 一般 Input 可參考 10 pt + natural height，但不可把 Compact / Standard 固定數值套死所有 App。
10. Shell / Dialog / Button Position / Navigation 等多數為 advisory；不要為了 Guide 限制正常程式設計。
11. 標準互動 controls 仍以 Native-first；有限 owner-paint 只在成熟、穩定且有明確價值時使用。
12. 下一個實際工作：繼續 **UI Shell Prototype / DPI 真機驗證**。
13. 在使用者明確核准以前，本文件仍不能變成治理規則或現行開發門檻。