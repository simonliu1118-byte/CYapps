# WO-1.6.0 Grok Bot 建檔補完與候選規格判讀（試點）

- 狀態：審查通過，待合併
- 分支：`cypricedesk/v1.6.0-grok`（自 `main` 分出）　PR：[#651](https://github.com/simonliu1118-byte/CYapps/pull/651)
- 依據：`apps/CYPriceDesk/PROJECT_RULES.md`（含 governance 2.3.67 的 Grok 機器寫入例外，CYapps #650）、`apps/CYPriceDesk/docs/GROK_BOT_DESIGN.md`、WO-1.3.0～WO-1.4.0、本目錄 `README.md`
- 開工前提：#650 與本工作單所在 PR 已合併 `main`。

## 0. 執行限制（必讀）

- 不合併、不部署、不發 Release；只在指定分支提交、push 並開 PR。
- 測試只用假資料、fake fetcher 與假 Grok；**不得連線 momo、飛比、酷澎、Grok 或任何外部網站**。
- 不改變已定案事項；確需偏離時在完成報告說明，交由審查決定。
- 不加多餘的測試與防護（共通規則 §11.4，使用者 2026-10-29）：只做本單要求的功能與實際會發生的錯誤處理；不加重複驗證、不會發生的情境檢查、層層 fail-closed、自設上限、安全開關，也不為這些寫測試。審查時會直接刪除。
- **用量上限本版不做**（使用者 2026-11-27：先把測試流程跑通再訂）。不要自行加每日次數、筆數或再查次數限制。
- 只可修改本單列出的 workflow（§3.5）；不改 `PROJECT_RULES.md`（已由 #650 處理）。

## 1. 背景（使用者原話，2026-11-27）

> 我想要做的是把現在的查價進一步，因為程式沒辦法辨識規格，所以交由 grok bot 用他的瀏覽器去查。

> 酷澎不是改查官網，而是程式找好的飛比，他由連結進去判斷規格和選項價格。

> grok 基本上不自己搜尋，他是補完。那個擬關鍵字的部分是要補齊這個視窗的（新增追蹤商品），要我自己全部填滿有點困難，尤其是關鍵字，所以看是不是我給出條件之後由他補完。

> 蝦皮要等我幫 bot 弄個帳號之後才有辦法，放在 TODO。

使用者已在 Grok 手動試 1530-1：可開 momo 與酷澎商品頁、選規格、讀到價格（2026-11-27）。webhook 觸發與 Grok 帶憑證呼叫本站 API 尚未實測。

## 2. 已定案事項

### 2.1 定位

- Grok **不自己搜尋**。候選照舊由排程或「立即查價」產生（fetcher → `src/grouping.js` → `latest_candidates`）。
- Grok 只做兩種任務：**建檔補完**（`draft`）與**候選判讀**（`judge`）。
- Grok 回填一律是待確認資料；使用者同意或修正後才寫入正式資料（`tracked_products`、`listing_judgments`、`price_observations`），操作者為該使用者。
- 不另建行情頁或第二套比價；比價、走勢圖、待確認頁沿用現有畫面與 `public/compare.js`。
- 平台：momo、酷澎（飛比酷澎候選）。**蝦皮本版不送 Grok**。

### 2.2 建檔補完（`draft`）

- 「新增追蹤商品」視窗頂部加一個「條件」文字框（例：「3M 紙膠 1530-1，1 吋，一盒 12 捲，以盒賣」）與「交給 Grok 補完」按鈕。已填的欄位一併送出。
- Grok 補完：名稱、型號、規格屬性、包裝數量、包裝單位、查詢關鍵字、momo 關鍵字、飛比關鍵字，並附每個欄位的依據與試搜結果（關鍵字、來源、筆數）。
- 不由 Grok 填：三平台我的售價與連結、CY Web 品號、啟用定期查價。
- 視窗開著時顯示「補完中」並每 5 秒更新；完成後把結果填入欄位，**Grok 改過的欄位加標示**，依據顯示在欄位下方小字。使用者修改後按「儲存」才建立追蹤商品，建立者為使用者。
- 視窗可以關閉；未套用的補完結果列在「追蹤商品」頁上方「Grok 補完」區，點「開啟」以結果預填新增視窗，可「捨棄」。
- 若 Grok 提問（例如條件寫 1530-1、照片或頁面顯示 1530-0），問題顯示在視窗（或該區）內，使用者選答案或自填後送出，Grok 續做。

### 2.3 候選判讀（`judge`）

- 商品詳情與待確認頁（每個商品群組）加「交給 Grok 判讀」按鈕。預設範圍：該商品當輪 `pending` 與 `other` 群組的 momo、coupang 候選；也可在候選表勾選特定列再送。
- 任務保存送出時的商品條件（型號、規格屬性、包裝數量與單位）。Grok 回填時若商品這些條件已改，該回填以 409 拒收並標為「已過期」。
- Grok 對每個候選回填：判讀（`same`／`other_package`／`not_same`／`insufficient`）、選取的選項原文、實際規格（型號、規格屬性、包裝數量與單位）、價格（可為 null）、價格條件、賣家、商品頁網址、查看時間、依據；也可回報該平台受阻（登入牆、驗證碼、讀不到）。
- 候選表與待確認頁中，有 Grok 回填的列顯示「Grok：<判讀>」、選項、價格、依據摘要（可展開）與網址，操作：
  - **同意**：以 Grok 的判讀寫入 `listing_judgments`（與現有手動判斷同一路徑）；判讀為 `same` 且有價格時，以 Grok 讀到的價格寫入該輪 `price_observations`（同輪同候選已有觀測就以 Grok 價格取代）。`insufficient` 沒有「同意」。
  - **修正並採用**：開現有判斷操作，預填 Grok 判讀與包裝，使用者可改判讀、包裝數量／單位、價格後寫入；保存 Grok 原判讀與人工結果。
  - **請 Grok 再查**：填原因，只重查該筆。
  - 未處理的 Grok 回填不進入任何比價或走勢圖。
- 平台受阻時，該平台候選維持現狀（酷澎維持飛比參考價），其他候選照常回填。

### 2.4 刪除當輪候選

- 待確認頁與商品詳情候選表的判斷操作後加「刪除」，確認對話框顯示平台、商品名稱、價格。
- 只刪 `latest_candidates` 的該列：以 (商品, 來源, 候選識別, 當輪 run) 定位；當輪已換新時回 409「清單已更新，請重新整理」。
- 不保存刪除名單、不建排除規則、不轉成人工判斷、不刪歷史觀測；下一輪來源再回傳就重新出現。
- 刪除後該列的 Grok 晚到回填以 404 拒收，不得重新插入。
- 寫 audit（`candidate.delete`），刪除後即時更新清單與待確認徽章。

### 2.5 任務與 webhook

- 任務狀態：`queued`（已建立、待送出）→ `sent`（webhook 回 2xx）→ `working`（Grok 已領取）→ `done`／`partial`／`blocked`；webhook 失敗為 `send_failed`，畫面可「重送」（沿用原 `eventId`）。
- Worker 呼叫 `GROK_WEBHOOK_URL`，header `Authorization: Bearer <GROK_WEBHOOK_TOKEN>`，body：

  ```json
  {"schemaVersion":1,"event":"pricedesk.task.ready","eventId":"<uuid>","taskId":"<id>","taskVersion":1}
  ```

  事件：`pricedesk.task.ready`（新任務）、`pricedesk.question.answered`（附 `questionId`）、`pricedesk.review.recheck`（附 `resultId`）。webhook 不帶商品資料。
- Grok 呼叫本站帶 `Authorization: Bearer <GROK_API_TOKEN>`。
- 三個值都是 Worker secret。未設定時「交給 Grok」按鈕停用並顯示「Grok 尚未設定」，其餘功能照常（同 fetcher 未設定時的作法）。

### 2.6 稽核

- 使用者操作：沿用 CYID principal（建立任務、回答、同意、修正、再查、刪除、捨棄補完）。
- Grok 寫入：同 batch 寫 audit，操作者固定為 `employeeId='grok-bot'`、`displayName='Grok Bot'`（PROJECT_RULES §4 例外）。
- 操作紀錄頁要能正確顯示新動作的說明文字。

## 3. 範圍

### 3.1 D1（`migrations/0005_grok.sql`）

- `latest_candidates` 新增 `run_id TEXT NOT NULL DEFAULT ''`；`runTracked` 寫入當輪 `runId`。
- 新表（欄位可依實作調整，需在完成報告列出）：
  - `grok_tasks`：id、kind（`draft`／`judge`）、product_id（draft 為 NULL）、status、event_id、conditions_json（送出時的條件或使用者輸入）、draft_json（補完結果）、platform_status_json、error、created_at、created_by、updated_at。
  - `grok_task_items`：task_id、product_id、source、listing_key、run_id、status（`waiting`／`answered`／`reviewed`／`expired`）、result_json、review_json（人工結果）、reviewed_at、reviewed_by；主鍵 (task_id, source, listing_key)。
  - `grok_questions`：id、task_id、item 識別（可空）、question、options_json、answer、answered_at、answered_by、created_at。

### 3.2 Worker

- **Grok 機器接口**（不經 CYID，只認 `GROK_API_TOKEN`；在 `src/index.js` 中於 CYID resolve 之前分派），新模組 `src/grok-api.js`：
  - `GET /api/grok/tasks/:taskId`：任務類型、條件、候選清單（來源、識別、標題、連結、價格、賣家）、已有問答與再查原因。首次讀取時狀態由 `sent` 轉 `working`。
  - `POST /api/grok/tasks/:taskId/progress`：平台狀態（`working`／`done`／`blocked`＋原因）；全部平台結束時任務轉 `done`／`partial`／`blocked`。
  - `POST /api/grok/tasks/:taskId/questions`
  - `PUT /api/grok/tasks/:taskId/draft`（只限 `draft`）
  - `PUT /api/grok/tasks/:taskId/results/:source/:listingKey`（只限 `judge`；候選不在任務內或已從 `latest_candidates` 刪除 → 404；條件已變 → 409 並把 item 標 `expired`）
  - 只能寫 `grok_*` 表與 audit。
- **使用者接口**（經 CYID），新模組 `src/grok-tasks.js`：
  - `POST /api/grok-tasks`（建立 draft 或 judge 任務並送 webhook）、`GET /api/grok-tasks`（補完草稿與進行中任務）、`GET /api/grok-tasks/:id`、`POST /api/grok-tasks/:id/resend`、`POST /api/grok-tasks/:id/questions/:qid/answer`、`DELETE /api/grok-tasks/:id`（捨棄補完）。
  - `POST /api/grok-tasks/:id/items/:source/:listingKey/approve`、`.../recheck`（body 含原因）。「修正並採用」走現有 `PUT /api/tracked/:id/judgments`，加帶 Grok item 識別與（同規格時）價格，使 item 一併標為已審。
- **判斷寫入共用**：把 `src/tracking.js` 的判斷寫入整理成可共用的函式，讓手動判斷、同意、修正並採用走同一路徑；同意／修正時的 `listing_judgments`、`price_observations`、`grok_task_items` 更新與 audit 在同一個 `DB.batch()`。
- `DELETE /api/tracked/:id/candidates/:source/:listingKey`（body 帶 `runId`），見 §2.4。
- `GET /api/tracked/:id`、`GET /api/pending` 的候選加回 `runId` 與該候選最新的 Grok item（若有）。
- webhook 呼叫放 `src/grok-client.js`；測試以假 `fetch` 取代。

### 3.3 前端

- `public/tracked.js`：新增視窗的條件框、「交給 Grok 補完」、補完中狀態、欄位標示與依據、提問回答；追蹤商品頁上方「Grok 補完」區。
- `public/candidates.js`、`public/pending.js`：「交給 Grok 判讀」、勾選送出、Grok 回填顯示、同意／修正並採用／請 Grok 再查／刪除、任務狀態與平台受阻提示、重送。
- 沿用 `.panel`、`.data-table`、`.btn`、`.dialog-surface`；Grok 標示用中性色，不新增大面積色塊。桌面 1440 與手機 390 都要可用，頁面不得橫向捲動。

### 3.4 Grok routine 說明

新增 `apps/CYPriceDesk/docs/GROK_ROUTINE.md`：使用者貼進 Grok routine 的工作說明（中文），內容含：

- 收到 webhook 後如何讀任務、回報進度、提問、回填（路徑、欄位、範例 JSON，網址以 `https://<價格觀測室網域>` 表示）。
- 判讀規則：以「平台、賣家、原商品、實際銷售選項」為單位；momo 同頁多選項須實際選取再讀價；酷澎由候選連結進入，不另行搜尋；看不出原盒或散裝就回 `insufficient`；受阻即回報、繼續其他候選；不自行搜尋新候選；不登入任何網站。
- 建檔補完規則：依條件補欄位，關鍵字須實際在 momo／飛比試搜並回報筆數；條件矛盾時提問，不自行決定。
- 不寫任何真實金鑰。

### 3.5 部署

`.github/workflows/cypricedesk-deploy.yml` 比照 `CYPD_FETCHER_SECRET`：從 GitHub Environment secret 寫入 Worker secret `GROK_WEBHOOK_URL`、`GROK_WEBHOOK_TOKEN`、`GROK_API_TOKEN`，未設定則略過並印出一行說明（不印值）。`tests/deploy-workflow.test.js` 若有對應檢查一併更新。

### 3.6 文件

`VERSION`=1.6.0、`BUILD`=0、`package.json` version、`CHANGELOG.md`、`README.md`（Grok 架構、接口、secret 設定步驟）、`TODO.md`（勾選本版項目、保留蝦皮與用量上限等後續）、`WORK_HANDOFF.md`、本目錄 `README.md` 階段表加入第 5 階段、`docs/GROK_BOT_DESIGN.md` 狀態改為「已依 WO-1.6.0 實作」。

## 4. 不做

- 蝦皮送 Grok（待使用者準備 Bot 帳號）。
- 商品照片（需 R2）。
- Grok 自行搜尋新候選。
- 用量上限（試點跑通後另訂）。
- 單捲／多盒換算、行情區塊。
- 已判斷為同規格的候選，下一輪查價仍以搜尋結果價格寫觀測（現有行為）；是否每輪再請 Grok 讀選項價，試點後再定。
- fetcher（Vercel）、排程規則、CY Web 匯入不改。

## 5. 實作指引

- 機器接口的 token 比對用固定時間比較；token 錯誤一律 401，不區分原因。
- `runTracked` 只多寫 `run_id`；不要改變分組、快取、間隔與冷卻。
- 判斷寫入整理為共用函式後，既有 `tests/tracking.test.js` 的判斷測試必須照舊通過。
- 「條件是否已變」以型號、規格屬性、包裝數量、包裝單位比較；售價、連結、關鍵字變動不算。
- 測試（`node:test`，不追求數量）至少涵蓋：
  1. 建立 judge 任務送出 webhook（header、body、狀態 `sent`）；webhook 失敗為 `send_failed`，重送沿用原 `eventId`。
  2. 機器接口：錯誤 token 401；讀任務轉 `working`；回填寫入 item 與 audit（actor `grok-bot`）；不在任務內的候選 404；條件已變 409／`expired`。
  3. 同意 `same`：`listing_judgments`、該輪 `price_observations`（Grok 價格）、item、audit 同一 batch；修正並採用保存 Grok 原判讀與人工結果。
  4. draft：回填補完結果、提問與回答觸發 `question.answered` webhook。
  5. 刪除當輪候選：18 筆刪 1 筆後待確認數為 17；`runId` 不符 409；刪除後晚到回填 404；下一輪 `runTracked` 來源再回傳時重新出現。
  6. `tests/ui.test.cjs` 加入：新增視窗補完流程（以假任務資料）、候選列 Grok 回填顯示與同意、刪除候選。
- 截圖（1440／390，deviceScaleFactor 1）：新增視窗（補完中、補完後含標示）、Grok 補完區、候選表含 Grok 回填、待確認頁含 Grok 回填與刪除確認、提問回答、任務受阻提示、操作紀錄含 Grok 動作。輸出到執行者 scratchpad 目錄下的 `wo160/`，不放進 repo。

## 6. 驗收清單

- [x] §2.1～2.6 全部符合。
- [x] Grok 機器接口只寫 `grok_*` 表與 audit；正式資料只由使用者操作寫入，且與 audit 同 batch。
- [x] `wrangler d1 migrations apply DB --local` 從空庫套用 0001～0005 成功。
- [x] `npm test`、`tests/ui.test.cjs`、fetcher unittest、build、public-package scan、`wrangler deploy --dry-run` 通過。
- [x] `docs/GROK_ROUTINE.md` 完成，且不含真實金鑰或網域以外的部署識別值。
- [x] 部署 workflow 依 §3.5 更新，不印出 secret。
- [x] 文件依 §3.6 更新。
- [x] 截圖如 §5。
- [x] commit 身分 `simonliu1118-byte`（noreply email）。

## 7. 實機驗證（合併部署後，使用者與 Claude）

1. 在 GitHub Environment 設定三個 secret，部署；Grok routine 貼上 `GROK_ROUTINE.md` 並設定 webhook 與金鑰。
2. 建檔補完：只輸入「3M 紙膠 1530-1，1 吋，一盒 12 捲，以盒賣」，確認欄位與關鍵字補齊、儲存後建立者為使用者。
3. 立即查價後送判讀：momo 同頁含半吋與 1 吋時回填所選規格價格；酷澎由飛比連結進入判讀；同意後比價與走勢圖反映。
4. 刪除一筆待確認，徽章減 1；再立即查價確認來源回傳時重新出現。
5. 記錄這輪 Grok 實際任務數、候選數與耗時，作為訂用量上限的依據。

## 8. 完成報告（執行者填）

2026-11-28 完成 V1.6.0 Build 0，基準為 `main` 的 `5084f0d`。由 `cypricedesk/v1.6.0-grok` 送審，含 Grok 補完、判讀、人工複審、候選刪除與部署所需設定。

### 實作與資料欄位

- `latest_candidates.run_id` 隨每個有更新的來源寫入。遷移以各來源最後一個可用的查價輪次回填舊候選。刪除以商品、來源、候選識別與輪次定位，保留歷史觀測。
- `grok_tasks` 在清單欄位外另存 `event_json`，讓派工失敗時以原事件重送。草稿另用 `applied` 和 `discarded` 狀態保存已使用及捨棄紀錄。
- `grok_task_items` 另存派工當下的候選資訊 `snapshot_json` 及再查原因 `recheck_reason`，人工複審結果保存在 `review_json`。再查事件的 `resultId` 以 `source`、`listingKey` 定位。`grok_questions` 以可空的 `source`、`listing_key` 關聯候選。
- Grok 回填與稽核同批寫入。人工同意及修正採用走既有判斷寫入，同批更新當輪候選、同規格價格觀測、Grok 複審與稽核。當輪價格觀測採查價輪次時間，Grok 實際查看時間保留在原回填。
- [Grok routine](../GROK_ROUTINE.md) 說明機器接口、Webhook、試搜、規格判讀及問答。部署 workflow 自 Environment 讀取三項選填 secret，缺值略過並只印出名稱。

### 驗證

| 檢查 | 結果 |
| --- | --- |
| `npm test` | 46 項通過，含派工與原事件重送、機器 actor、過期、同意、修正、問答、刪除後 18 → 17 與下一輪回復 |
| `tests/ui.test.cjs` | 1440／390 通過。含 1530-1 建檔補完、選答、重開草稿並儲存、候選判讀及同意、平台受阻、刪除確認與徽章更新 |
| Fetcher unittest | 18 項通過 |
| D1 本機空庫遷移 | Wrangler 套用 0001～0005 通過 |
| 靜態產物與 Worker | JavaScript 語法檢查、build、公開產物掃描與 Wrangler dry-run 通過 |
| 提交作者 | `simonliu1118-byte <286269326+simonliu1118-byte@users.noreply.github.com>` |

使用本機假資料、假 fetcher 及假 Grok。18 張試點截圖在執行者 scratchpad 的 `wo160/`，檔名以 `wo160-` 開頭，各有 `desktop` 與 `mobile`。正式環境和實際 Grok routine 驗證依 §7 進行。

## 9. 審查紀錄（審查者填）

### 2026-11-28 審查（Claude）— 結論：通過

- 對照 §2～§3 檢視主要程式（`src/grok-api.js`、`grok-tasks.js`、`grok-store.js`、`grok-client.js`、`tracking.js` 的 `writeJudgment`、`tracking-run.js`、`0005_grok.sql`、部署 workflow）與 `docs/GROK_ROUTINE.md`：
  - Grok 機器接口在 CYID 之前分派，以固定時間比對 `GROK_API_TOKEN`；只寫 `grok_*` 表與 audit（actor `grok-bot`），符合 PROJECT_RULES §4 例外。
  - 同意與修正並採用走共用的 `writeJudgment`，`listing_judgments`、當輪 `price_observations`（Grok 價格）、`grok_task_items` 與 audit 同一 batch；條件已變或候選已刪除時回 409／404。
  - 刪除當輪候選以 `runId` 定位，不留名單、不刪歷史觀測。部署 workflow 三個 secret 皆選填、不印值。
  - Routine 說明明訂只開任務提供的候選連結、以公開頁面瀏覽、遇登入牆即回報；未自行搜尋新候選。
- 驗證（審查者重跑）：`npm test` 46／46；`tests/ui.test.cjs`（`SEED=1 NO_GAP=1 FAKE_GROK=1`）通過並重新產生 36 張截圖逐張檢視；build、public-package scan、語法檢查通過；`wrangler d1 migrations apply --local` 0001～0005 成功；`wrangler deploy --dry-run` 通過；PR CI（governance、check）通過。
- 另以本機 Wrangler D1 實測「`changes()=0` 時以 `json('…')` 中止 batch」的寫法（草稿重複套用、複審衝突用），確認第二次執行整批回滾。
- 接受的差異：回填欄位名稱為 `option`、`priceConditions`（工作單示例為 `selectedOption`、`priceCondition`），Worker 與 routine 說明一致即可。
- 建議（不阻擋合併，列入後續）：
  1. 操作紀錄中 Grok 動作只顯示「建立任務 · Grok」等通用文字，沒有商品名稱；刪除當輪候選顯示候選識別而非商品名稱。之後可在 audit 的 after 資料帶商品名稱。
  2. 新增的 Grok 模組多為壓縮寫法（少空白、多個敘述擠在一行），與既有程式風格不同；之後修改這些檔案時順手整理。
  3. 以 `malformed JSON` 錯誤訊息判斷中止 batch 並轉為 409，日後若有其他 JSON 解析錯誤也會被顯示為 409；之後有更直接的寫法時再替換。

### 2026-11-28 Build 1（Claude 直接修正）

- 部署後首次實機派工（建檔補完）顯示「派工失敗」，Grok routine 未收到請求；原程式不記錄失敗原因。比照 CY Web 小編推播（`worker/bot/review-push.ts`）記錄 HTTP 狀態碼或連線錯誤名稱，顯示在任務錯誤文字；轉址改為 `redirect:'manual'` 以記錄 3xx。`BUILD`=1。

