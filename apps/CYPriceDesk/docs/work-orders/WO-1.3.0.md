# WO-1.3.0 追蹤商品：定期自動查價、同規格比價與自己的售價紀錄（第三階段）

- 狀態：待執行
- 分支：`cypricedesk/feature-tracked-products`（自 `cypricedesk/feature-auto-query` 分出）　PR：（執行者填）
- 依據：`apps/CYPriceDesk/PROJECT_RULES.md`（governance 2.3.60）、WO-1.1.0、WO-1.2.0、本目錄 `README.md`

## 0. 執行限制（必讀）

- 不合併、不部署、不發 Release；只在指定分支提交並 push。
- 可修改 `.github/workflows/cypricedesk-*.yml`；其他 workflow 不動。
- 測試只用假資料與 fake fetcher；**不得連線 momo、飛比或任何外部網站**。
- 不改變已定案事項；確需偏離時在完成報告說明。
- 不加多餘的測試與防護（共通規則 §11.4）。

## 1. 背景（使用者原話，2026-11-16）

- 「我希望能做到我設定特定商品（比方我蝦皮的某個商品），然後定期自動用那個商品去查價給出結果，作為調整定價的依據」
- 「查價頻率一周到兩周一次就好……希望這個查價網能夠自己去分配查價時間，避免短時間高頻查價」
- 「商品數量目標大概會在 500～1000」「查價數量想保留至少 5 年」
- 「自己的售價自己手動輸入，一樣會留價格調整紀錄可以做分析」
- 「3M 紙膠帶 1530 有 1 吋也有 0.5 吋，不一定能分規格查詢……那就列出這個價格由我或之後測試 Grok BOT 去看規格價格」「我如果是一盒裝，那散裝貨兩盒大包裝的就不會放在一起比價，但是可能可以顯示出來有這樣的販售方式價格多少讓我參考」「程式自己可以判斷的就會自己歸進去分析，我或 bot 只去判斷程式無法判斷的」
- 「如果在查詢的時候查到一個 15301……我可能就會去看過發現他根本不是同一樣商品，就會回報這個不對，就存下來下次不要把這個結果帶出來」
- 儲存採方案 B：長期只保存同規格各筆價格與其他規格組摘要；完整候選清單只保留最近一次。

## 2. 已定案事項

1. **每次都重新查詢**，不固定追蹤特定賣場商品；消失的商品不再處理，新出現的自動納入。
2. **追蹤商品**欄位：名稱、型號、規格屬性（例如寬度 1 吋）、包裝數量與單位（例如 1 盒＝12 入）、自己的售價、自己的商品連結（選填）、momo 查詢關鍵字、飛比查詢關鍵字、啟用／停用。
3. **自動分組**（以 `public/catalog-core.js` 的 `parseTitle` 解析標題，不另寫解析器）：
   - **同規格**：型號相同，且包裝數量、單位、已設定的規格屬性都與追蹤商品一致 → 進入比價分析。
   - **其他包裝**：型號相同但包裝數量或單位不同 → 依「數量＋單位」分組顯示價格範圍供參考，不與自己的售價比較。
   - **待確認**：解析不出型號或數量、標題同時出現多個規格（如「0.5 吋／1 吋」）、或規格屬性不明 → 等人工判斷。
   - **無關**：標題不含型號 → 不顯示（計數即可）。
4. **人工判斷**（之後 Grok BOT 用同一個 API）：對待確認或自動分組有誤的商品標記「同規格」「其他包裝（填數量＋單位）」或「不是同一商品」。判斷**以「追蹤商品＋來源＋商品識別」為單位保存**（momo 品號、飛比來源連結），下次查價自動套用，不再詢問；「不是同一商品」下次不顯示。判斷時記下當時標題，**標題變了就回到待確認**。已排除的可在「已排除」清單恢復。所有判斷寫 audit。
5. **自己的售價**手動修改，每次修改留紀錄（時間、操作者、改前、改後、備註），並寫 audit。
6. **排程**（Worker Cron `*/10 * * * *`）：
   - 只在臺北時間 08:00–22:00 執行；每次最多處理 1 個到期商品（`next_run_at <= now`，依 `next_run_at` 排序）。
   - 一個商品查 momo 第 1 頁＋飛比一次（沿用 WO-1.2.0 `/api/search` 的快取、15 秒間隔、30 分鐘冷卻邏輯，抽成共用函式，不另寫第二套）。若某來源被節流或冷卻，該商品 `next_run_at` 延後 1 小時，其他來源結果照存。
   - 查完：`next_run_at = now + 隨機 7–14 天`，時間隨機落在 08:00–22:00 之間。新增追蹤商品時 `next_run_at` 隨機落在未來 0–7 天，讓 1000 個商品自然分散。
   - 追蹤頁有「立即查價」按鈕，走同一個流程。
7. **儲存（方案 B）**，保留至少 5 年，不自動刪除：
   - `price_observations`：每次查價中「同規格」的每一筆（來源、商品識別、賣家、標題、價格、連結、查價時間）。
   - `group_summaries`：每次查價中每個「其他包裝」組與「同規格」組的筆數、最低、中位數、最高。
   - `latest_candidates`：每個追蹤商品最近一次的完整候選清單（含分組結果），每次查價覆蓋。
   - `tracked_runs`：每次查價的時間與各來源狀態。
8. 飛比結果照 WO-1.2.0 標示「第三方商品級參考價」，分開顯示來源；同規格分析中 momo 與飛比各自列出、可合併看總最低價，但每筆都標來源。
9. 版本 `1.2.0` → `1.3.0`、`BUILD=0`。
10. **整站介面改為 CY Web 的簡潔商務風格**（使用者 2026-11-16：「介面的話請盡量做成符合 CYWEB 這樣的簡潔商務介面」），適用登入頁、比價總覽、自動查價、追蹤商品、所有對話框。

## 3. 範圍

### 3.1 D1（`migrations/0003_tracked_products.sql`）
- `tracked_products`（id、name、model、attributes_json、package_quantity、package_unit、my_price、my_url、momo_keyword、feebee_keyword、active、next_run_at、last_run_at、revision、created_at/by、updated_at/by）。
- `my_price_changes`（id、product_id、at、actor_employee_id、actor_name、old_price、new_price、note）。
- `listing_judgments`（product_id、source、listing_key、verdict `same|other_package|not_same`、package_quantity、package_unit、title_snapshot、decided_at、decided_by、PRIMARY KEY(product_id, source, listing_key)）。
- `tracked_runs`（id、product_id、started_at、finished_at、momo_status、feebee_status、trigger `schedule|manual`）。
- `price_observations`（id、run_id、product_id、source、listing_key、seller、title、price、url、observed_at；索引 `(product_id, observed_at)`）。
- `group_summaries`（run_id、product_id、group_key、label、count、min_price、median_price、max_price）。
- `latest_candidates`（product_id、source、listing_key、title、price、seller、url、group `same|other|pending|unrelated|excluded`、parsed_json；PRIMARY KEY(product_id, source, listing_key)）。

### 3.2 Worker
- `src/tracking.js`：追蹤商品 CRUD（revision 條件更新、audit）、改售價（寫 `my_price_changes`＋audit）、判斷 API（`PUT/DELETE /api/tracked/:id/judgments`）、查詢 API（列表、單一商品詳情：最近候選、同規格歷史、其他包裝摘要、售價紀錄）。
- `src/tracking-run.js`：執行一次追蹤查價（呼叫共用搜尋函式 → 分組（套用判斷）→ 寫入 3.1 各表，同一 batch）。
- `src/index.js`：加 `scheduled()` handler 與路由。`wrangler.template.jsonc` 加 `triggers.crons`。
- 抽出 WO-1.2.0 `search.js` 的「快取＋間隔＋冷卻＋呼叫 fetcher」為共用函式，`/api/search` 與排程共用。

### 3.3 前端
- 側欄新增「追蹤商品」頁：
  - 列表：名稱、規格／包裝、自己的售價、同規格最低價與中位數（最近一次）、與自己售價差額％、待確認筆數、上次／下次查價時間、啟用狀態；搜尋與篩選（有待確認、已停用）。
  - 新增／編輯對話框（2.2 欄位）。
  - 詳情：自己的售價與修改紀錄；同規格（依來源分開列出最近一次各筆，與歷次最低價／中位數走勢表）；其他包裝組（數量＋單位、筆數、價格範圍）；待確認清單（每筆三個按鈕：同規格／其他包裝／不是同一商品）；已排除清單（可恢復）；「立即查價」。
- 沿用既有樣式與對話框元件；不加圖表套件（走勢先用表格）。

### 3.4 介面改版（2.10）
- 參考 CY Web（`simonliu1118-byte/chihyuan-web`，本機 `/home/user/rv517`）：`src/app.css`、`src/ui/shell/`、`src/ui/data/data-view.css`、`src/ui/overlays/`（Dialog、`overlay.css`）、`src/ui/forms.css`、`src/ui/records/`；參考畫面 `/tmp/claude-0/-home-user/04d2ad10-f9b3-5958-aff8-f556061b5368/scratchpad/h/tab-1440.png`、`one-1440.png`、`tab-390.png`。只參考視覺語言與 CSS 數值，不搬 React 元件、不引入框架。
- 原則：
  - 白底左側導覽列＋頂部標題列（頁名＋右上角使用者選單：姓名、登出）；內容區以白色面板、細框線、資料表格為主。
  - **拿掉行銷式文字與裝飾**：大標語（「好價格，一目瞭然。」）、英文 eyebrow、說明段落、側欄標語卡、統計大卡片的裝飾圖示。必要的狀態訊息與飛比參考價警示保留，用一行簡短文字。
  - 表格：灰底表頭、細格線、緊湊列高、數字靠右；長文字折行，不用「…」截斷。
  - 表格內不出現方框輸入框；需要編輯就用對話框或格內編輯。
  - 對話框：小功能用最小寬度、不放說明文字；右上角 × 與「關閉」按鈕只保留一個；底部按鈕靠右（取消、主要動作）。
  - 按鈕：主要動作實心深色，其餘白底細框；分頁切換用分段按鈕。
  - 字體、顏色、間距、圓角取 CY Web 的值；手機版 390 寬可用、無水平捲動。

### 3.5 文件
- README（追蹤商品、排程規則、儲存方式與估算：1000 商品約每日 100 次查價、200 次請求；5 年約 150 萬筆、300MB）、TODO、WORK_HANDOFF、CHANGELOG（V1.3.0）。

## 4. 不做
- Grok BOT 串接（只保留判斷 API 可供之後使用）。
- 蝦皮賣家 API 自動帶入自己的售價。
- 圖表、通知、匯出追蹤資料。
- 刪除 sandbox 專案。

## 5. 實作指引
- 分組判斷寫成純函式（輸入：追蹤商品、解析結果、既有判斷；輸出：group），放 `public/catalog-core.js` 旁或 `src/`，並以表格式測試覆蓋 2.3、2.4 的情境：同規格、其他包裝（12 入 vs 6 入）、多規格標題進待確認、型號不同（15301 vs 1530）為無關、人工「不是同一商品」不再顯示、標題變了回到待確認。
- 排程測試：時間窗外不執行、每次只處理 1 個、查完 `next_run_at` 落在 7–14 天且在 08:00–22:00、被節流時延後 1 小時。
- 截圖（1440／390）：登入頁、比價總覽、自動查價、追蹤商品列表、商品詳情（含待確認與其他包裝）、判斷操作後畫面、售價修改紀錄。輸出到 `/tmp/claude-0/-home-user/04d2ad10-f9b3-5958-aff8-f556061b5368/scratchpad/wo130/`。

## 6. 驗收清單
- [ ] 2.3 自動分組與 2.4 人工判斷規則全部符合，含 15301 情境與標題變動回到待確認。
- [ ] 排程符合 2.6；手動「立即查價」走同一流程。
- [ ] 儲存符合 2.7；`latest_candidates` 每次覆蓋，其他表只增不刪。
- [ ] 售價修改有紀錄與 audit；追蹤商品與判斷的寫入都有 audit。
- [ ] `npm test`、fetcher unittest、build、public-package scan、`wrangler deploy --dry-run`（含 crons）通過。
- [ ] `VERSION`=1.3.0、`BUILD`=0；README／TODO／WORK_HANDOFF／CHANGELOG 已更新。
- [ ] 整站介面符合 3.4，與 CY Web 參考畫面並排比較一致。
- [ ] 截圖如 §5。
- [ ] commit 身分 `simonliu1118-byte`。

## 7. 實機驗證要求
部署後以 1～3 個追蹤商品觀察一輪排程；確認各來源單次請求、間隔與分組結果。

## 8. 完成報告（執行者填）

## 9. 審查紀錄（審查者填）
