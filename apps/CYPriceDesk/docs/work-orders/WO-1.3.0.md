# WO-1.3.0 追蹤商品：定期自動查價、同規格比價與自己的售價紀錄（第三階段）

- 狀態：已實作，待審查
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

### 8.1 驗收清單結果

- [x] 2.3 自動分組與 2.4 人工判斷：`src/grouping.js` 純函式，`tests/tracking.test.js` 表格式涵蓋同規格、其他包裝（12 入 vs 6 入／1 盒）、多規格標題（0.5 吋／1 吋）、多型號、缺數量、規格屬性不明或不同進待確認，型號不同（15301 對 1530、1530-1 對 1530-0）與不含型號為無關；人工「不是同一商品」下次查價仍排除，標題變了回到待確認。
- [x] 排程：`runScheduled`／`runTracked` 有測試——時間窗外（22:00～07:59 臺北）不執行、一次只處理 1 個到期商品（依 `next_run_at`）、查完 `next_run_at` 落在 7–14 天且 08:00–22:00、新增為 0–7 天、被節流時延後 1 小時且其他來源結果照存；手動「立即查價」呼叫同一個 `runTracked`。
- [x] 儲存：`price_observations`、`group_summaries`、`tracked_runs` 只增不刪；`latest_candidates` 依來源覆蓋（測試檢查兩次查價後筆數與 run 數）。
- [x] 售價修改寫 `my_price_changes`＋audit；追蹤商品新增／修改、判斷新增／恢復、立即查價皆在同一個 `DB.batch()` 寫 audit（`tracked.*`、`judgment.*`），revision 衝突回 409。
- [x] 本機：`npm ci`、`npm test` 32/32、`node --check`、`npm run build`、public-package scan、fetcher unittest 18/18、placeholder 設定 `wrangler deploy --dry-run`（渲染後設定含 `triggers.crons: ["*/10 * * * *"]`，Worker 匯出 `scheduled`）通過。既有 `tests/ui.test.cjs`（Playwright）更新選擇器後通過。**PR CI 尚未確認**（未開 PR）。
- [x] `VERSION`=1.3.0、`BUILD`=0、`package.json`／`package-lock.json` 1.3.0；README／TODO／WORK_HANDOFF／CHANGELOG 已更新。
- [x] 介面符合 3.4（見 8.4 的取值與一項偏離）。
- [x] 截圖（1440／390）見 8.5。
- [x] commit 身分 `simonliu1118-byte`。全程未連線 momo、飛比或任何購物網站；測試與截圖的 fetcher 皆為假實作。

### 8.2 審查者提供的追加決定

- 介面主色為**靛藍**（icon 漸層 `#3B4CCA` → `#2F3DB0`）：主要按鈕、側欄作用中項目、連結、焦點外框、選取狀態使用 `#3B4CCA`（hover `#2F3DB0`，淡底 `#ECEEFB`）；不再使用舊的深綠／萊姆色。其餘維持 CY Web 的中性灰白。favicon、登入頁與側欄品牌方塊為暫用佔位（靛藍底「CY」或長條圖），之後換正式圖示。

### 8.3 偏離與需審查決定的事項

1. **主色**：3.4 寫「主要動作實心深色」，依 8.2 的追加決定以靛藍實心；CY Web 本身的主色是另一個藍（`#3D63D2`），未採用。
2. **規格屬性不同進待確認**：工單未規定「型號、數量、單位都相同但規格屬性（例如 1 吋對 0.5 吋）不同」要進哪組，實作為待確認（由人判斷「不是同一商品」即不再顯示）。
3. **型號前綴**：追蹤商品型號 `1530` 時，標題型號 `1530-0` 視為同型號家族（再由規格屬性決定）；`15301`、`1530-1` 對 `1530-0` 仍為無關。這是為了使用者描述的「1530 有 1 吋也有 0.5 吋」情境；若要嚴格相等，刪 `grouping.js` 的 `startsWith(model + '-')` 一行。
4. **延後 1 小時的條件**：工單只寫「節流或冷卻」，實作為「任何來源狀態不是 `ok`／`no_match`／`no_price`」（含 `blocked`、`provider_refused`、`network_error`），避免連線失敗後被排到 7–14 天後。
5. **排程不寫 audit**：自動排程沒有 CYID principal，`tracked_runs.trigger='schedule'` 即為 append-only 紀錄，避免一天 100 筆灌滿操作紀錄；手動「立即查價」寫 `tracked.run` audit（操作者為登入者）。
6. **判斷只能針對 `latest_candidates` 內的商品**（標題快照取自伺服器，不採用 client 傳入）；判斷或恢復時同時更新該列的分組，所以詳情頁立即反映，但不回改已存的 `price_observations`／`group_summaries`，下一次查價才進入同規格歷史。
7. **欄位名稱**：`latest_candidates` 的分組欄位為 `grp`（`group` 是 SQL 保留字）；`next_run_at`／`last_run_at` 為 epoch 毫秒（與 1.2.0 的快取時間一致），其他新表時間為 ISO 文字。`tracked_runs.id` 為 `crypto.randomUUID()`。
8. **列表統計**：「同規格最低價與中位數」由 `latest_candidates` 的同規格列即時計算（所以人工判斷後立即更新），「售價相對最低價」定義為（自己的售價 − 最低價）÷ 最低價。
9. **編輯追蹤商品不能改售價**（售價只能經「修改售價」，才會有紀錄）；工單未提供刪除，只有啟用／停用。
10. **介面**：登入後側欄僅「比價總覽、追蹤商品、自動查價、操作紀錄」；「匯入」與「匯出」在比價總覽工具列，頂部沒有主題切換、通知（CY Web 有，工單未要求）。表格改為 CY Web 式灰底表頭資料表，窄版（≤680px）逐筆卡片；移除行銷文字、eyebrow、統計大卡片、價格長條與平台圖示，統計改成一行文字。自動查價的單價彙整改為表格，移除範圍長條圖。`ui.test.cjs` 因按鈕／class 改名同步更新選擇器。
11. 未修改 workflow（`cypricedesk-*.yml` 不需要變動，deploy 的 `wrangler deploy` 會帶出 crons）。`PROJECT_RULES.md` 未動。

### 8.4 指令輸出摘要（apps/CYPriceDesk，Node v22）

- `npm test`：32 tests，pass 32、fail 0（原 26＋追蹤商品 6：分組、人工判斷、排程時間、售價與 audit、立即查價與判斷套用、排程流程）。
- `npm run build`＋`python3 ../../.github/scripts/scan-public-package.py dist`：`Public artifact safety scan passed.`
- `/tmp/cypd-venv/bin/python -m unittest discover -s fetcher/tests`：18 tests OK。
- placeholder 設定 render＋`npx wrangler deploy --dry-run`：bindings 為 DB、IDENTITY、ASSETS 與四個 vars，`--dry-run: exiting now.`；渲染設定含 crons。

### 8.5 截圖（不在 repo 內）

`/tmp/claude-0/-home-user/04d2ad10-f9b3-5958-aff8-f556061b5368/scratchpad/wo130/`（本機 server＋假 fetcher＋種子假資料「3M 紙膠帶 半吋 12入／1530-0」，含同規格、其他包裝、待確認、無關 15301、已排除、歷次價格與 3 筆售價紀錄；假身分「測試員工」）。每張都有 `-1440.png` 與 `-390.png`：

- `login`：登入頁；`overview`：比價總覽；`auto-query`：自動查價（momo 結果與單價彙整）
- `tracked-list`：追蹤商品列表；`tracked-detail`：商品詳情（含待確認、其他包裝、已排除）
- `judge-dialog`：「其他包裝」判斷對話框；`judged`：判斷操作後畫面
- `price-dialog`：修改售價；`price-history`：售價修改紀錄；`tracked-edit-dialog`、`tracked-add-dialog`：編輯與新增；`tracked-list-after`：立即查價後的列表

全部流程無 JavaScript 錯誤、各頁無水平溢出（腳本逐張檢查）。

## 9. 審查紀錄（審查者填）

### 2026-11-16 審查（Claude）— 結論：通過

- 分組（`src/grouping.js`）：同規格／其他包裝／待確認／無關／已排除符合 2.3、2.4；15301 為無關、多規格標題進待確認、標題變動回到待確認，皆有測試。偏離 2（規格屬性不同進待確認）、3（`1530` 涵蓋 `1530-0` 家族）同意。
- 排程（`src/tracking-run.js`）：08:00–22:00、每 tick 1 個、條件式 claim、7–14 天隨機落在時間窗、節流或失敗延後 1 小時，符合 2.6；與 `/api/search` 共用 `querySource`，沒有第二套節流。
- 已知取捨：某來源長期被擋時，該商品每小時重試，另一來源的同規格觀測會每小時重複存一次；每 tick 只跑 1 個商品，每日總量仍在原估算範圍內，先不處理，實測後再評估。
- 寫入：追蹤商品、售價、判斷、手動查價皆有 audit；一次查價的 run、觀測、摘要、最近候選在同一個 batch。
- 介面：CY Web 風格與靛藍主色已套用。審查者直接修正：比價總覽平台篩選下拉被截成「所有平」（加最小寬度）；追蹤詳情「上次查價」與來源狀態黏在一起（狀態改換行）。
- 本機重跑：`npm test` 32/32、build、public-package scan 通過；截圖已檢視（總覽、追蹤列表、追蹤詳情）。
- 審查後追加（使用者 2026-11-16）：主色由靛藍 `#3B4CCA` 改為石油藍 `#0F5E73`（hover `#0B4B5C`），價格觀測室為獨立網站，不沿用 CY Web 藍色；PRC 圖示同步改色（AITeam #86）。
