# CYPriceDesk — 價格觀測室（原型）

**階段：需求探索原型。** 目前是獨立的私有 Sites 網站，CYWEB 僅提供外部連結。使用者於 2026-10-12 決定：需求定案後，再選擇併入 CYWEB 或獨立發展；現有技術與資料儲存方式不是正式架構決策。

- 網站：[價格觀測室](https://price-desk-simon.workspace-110444.chatgpt.site/)
- CYWEB 入口：`https://admin.chihyuancm.com/#programs` 的「價格觀測室（原型）」；需 CYWEB 登入，再另開網站。
- 正式原始碼位置：`simonliu1118-byte/CYapps` 的 `apps/CYPriceDesk/`。
- Source baseline 沿用初始原型 `package.json` 的 `1.0.0`，`BUILD=0`；這只是工程識別，不表示已有正式 GitHub Release 或產品驗收。

## 功能與比較語意

- 輸入商品、規格、平台、賣家、連結、商品價格、運費、臺北查價時間與備註。
- 以「商品名稱＋規格」分組；不同容量、型號或組合不混算。
- 可以先存連結為「待查價」，未知價格不列入排名。
- 顯示最低價、並列最低價、金額／百分比差異及最高與最低價差。
- 可切換商品價格或含運總價。含運總價＝商品價格＋運費，未知運費的紀錄排除；免運需明確填 0。
- 平台篩選只隱藏其他列，最低價基準仍取同商品規格的全部平台。
- 搜尋、新增、編輯、刪除；超過 7 天的查價提示更新。
- CSV / TSV 貼上、CSV 檔案匯入、預覽驗證與 CSV 匯出。相同商品規格與連結保留最新查價，較舊資料不覆蓋新資料。
- 範例資料獨立於正式使用者資料，範例操作不會寫入使用者紀錄。

## 現階段限制

- 採手動查價，不自動擷取蝦皮、momo 等平台價格，也沒有排程、購買或聯絡賣家功能。
- 金額固定為新臺幣；優惠券、會員價、數量或限時條件記在備註，未自動扣抵。
- 儲存於此網站來源的瀏覽器 `localStorage`（`price-desk:v1`），沒有雲端資料庫、跨瀏覽器或跨裝置同步。清除網站資料會移除紀錄，請定期匯出 CSV。
- Sites 目前僅擁有者可存取，使用 ChatGPT 登入。CYWEB 的 CYID 登入不會自動登入此網站，也不因加入程式目錄就擴大網站權限。
- 原型不是 CYID consumer，沒有新增 CYID Application、D1、Cloudflare Worker 或共享 Session。

## 原始碼分工

| 檔案 | 責任 |
| --- | --- |
| `public/core.js` | 驗證、臺北時間、分組、價格比較、CSV、最新紀錄更新 |
| `public/app.js` | 單一前端 state、mutation、render、表單及本機儲存 |
| `public/index.html` / `styles.css` | 網頁結構與 RWD |
| `scripts/build.mjs` | 可重現靜態建置至 `dist/` |
| `tests/core.test.js` / `app-flow.test.js` | 計價與真實事件 handler 的無瀏覽器測試 |
| `tests/ui.test.cjs` | Playwright 實際瀏覽器引擎驗證腳本；須另備 Playwright 與 Chromium |
| `TODO.md` | 唯一目前工作狀態與待確認需求 |
| `WORK_HANDOFF.md` | 下一個對話／本機接手的入口 |

## 本機驗證與建置

```sh
cd apps/CYPriceDesk
npm test
npm run build
python3 -m http.server 4173 --directory public
```

不需要安裝第三方套件即可完成單元／事件流程測試與建置。預設 CI 僅驗證與建置，不公開網站、不自動部署，也不建立 GitHub Release。

可用 Playwright／Chromium 的本機環境另執行 `node tests/ui.test.cjs`；需要時以 `NODE_PATH` 指向 Playwright 的套件目錄。無瀏覽器事件測試不能取代真實裝置、版面與登入驗收。

## Sites 部署與唯一來源

CYapps 是程式的維護來源；Sites 自帶的 source repository 只承接受控發布的鏡像，不另行維護功能。這次匯入不改既有網站網址、資產或儲存 key。

`deploy/hosting.template.json` 僅提供無環境識別資訊的設定模板。需要發布時，在受控部署工作區複製為 `.openai/hosting.json`，透過 Sites 查詢既有網站、沿用其 `project_id`，再從確認過的 CYapps commit 建置、推送 Sites source mirror 並私有部署。實際 `project_id`、來源倉庫憑證、token 或使用者查價紀錄不放入本 Public repository。

若之後改網址或儲存方式，先安排使用者 CSV 匯出／匯入；瀏覽器資料不會因 GitHub 原始碼搬移而跨來源搬移。

Copyright © 2026 C.C. Liu, Chihyuan Co. All Rights Reserved. 授權以 repository 根 `LICENSE` 為準。
