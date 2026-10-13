# CYPriceDesk 接手入口

最後整理：2026-10-12（Asia/Taipei）。本文件協助新對話接手；目前狀態以 `TODO.md` 與 GitHub `main`／本輪 PR／Actions 為準，不新增永久規則或平行進度表。

## 使用者已確認的範圍

- 先保留為獨立需求原型，未決定併入 CYWEB 或獨立發展。
- 原型原始碼歸入 `simonliu1118-byte/CYapps:/apps/CYPriceDesk/`。
- CYWEB 只在「CY 程式」列出「價格觀測室（原型）」並另開連結；不嵌入、反向代理、搬入 CYWEB 業務模組或改 CYID。
- Sites 仍為 owner-private，現有 ChatGPT 登入／原網址／瀏覽器資料保留。
- 不購買商品、不聯絡賣家、不擴大網站可見性。

## 從哪裡開始

1. 依既有治理順序讀根 `REPOSITORY_RULES.md`、`REPO_POLICY.md` 與本目錄 `PROJECT_RULES.md`，比對 AITeam `main` 共通規則版本。
2. 讀 `README.md`、`TODO.md`，確認本輪 PR 已合併、適用 CI 與 CYWEB development 部署結果。若未完成，沿用原分支接續，不再建立第二份原型。
3. 在 `apps/CYPriceDesk/` 執行 `npm test`、`npm run build`；真正瀏覽器驗收須另備 Playwright／Chromium。
4. 下一輪先由使用者定義需求，不將目前框架、資料庫缺席、localStorage 或 Sites 視為正式產品必須長期沿用的選擇。

## 網站與儲存

- 私有網站：`https://price-desk-simon.workspace-110444.chatgpt.site/`。
- 透過 Sites 依既有網站 slug／目前帳號查詢，取得既有 `project_id`；不要重新建立網站、捏造 ID 或把憑證寫入 Public source。
- 使用者資料僅在各瀏覽器此來源的 `localStorage`，key 為 `price-desk:v1`。原型沒有服務端資料庫。
- 變更網址或改雲端保存前，安排 CSV 備份／資料遷移；原始碼位置變更本身不遷移瀏覽器資料。
- Sites source repository 是發布鏡像；功能修改回到 CYapps，從驗證過的 source commit 重新建置並私有發布。

## CYWEB 的最小關聯

CYWEB repository：`simonliu1118-byte/chihyuan-web`。

- `shared/program-catalog.ts` 保存網站目錄 entry，以及選擇網站／正式桌面下載 action 的共用邏輯。
- `src/runtime/modules/ProgramCatalogPage.tsx` 同時供桌面列與行動卡片使用；沿用一個 renderer，不新增另一套目錄。
- `worker/programs/release-catalog.ts` 的 GitHub Release refresh 只更新已登記 release prefix 的產品。此原型不加入 release prefix，沒有虛構 release／download／發版日。
- `worker/acceptance/program-catalog-acceptance.ts` 與 `scripts/program-entry-links.test.mjs` 驗證原型入口保留、網站不依賴正式 Release、桌面未發版不得提供下載。
- `/api/programs` 原有 CYID 登入閘門不變；目錄 entry 不授予 Sites 存取權。
- CYWEB 長期網址為 `https://admin.chihyuancm.com`，目前使用既有 development 部署；不要為此建立 Worker 或更動其他程式的部署設定。

## 驗證界線

本機事件 handler／資料規則測試、匿名登入頁觀察及部署資產 HTTP 確認都有各自範圍，不能替代擁有者在網站裡的完整實際操作、多角色、裝置或自動查價驗收。無測試結果時保持待驗收，不把舊對話推測寫成成功。
