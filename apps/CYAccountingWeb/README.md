# CYAccountingWeb

志遠記帳系統 Web 版。此專案與 `apps/CYAccounting/` Windows 版分開維護；Windows 版仍是獨立正式產品線，Web 版不得因功能移植而覆蓋或破壞桌面版。

> Current integration candidate: **V0.21.6 Build 1**（2026-09-30）。Production CYID cutover still requires explicit approval.
>
> Current continuity handoff: [`HANDOFF_2026-09-30.md`](./HANDOFF_2026-09-30.md)

## 專案定位

CYAccountingWeb 是以 Cloudflare 為正式執行平台的多人 Web 記帳系統。

目前核心原則：

- Cloudflare D1 是 CYAccountingWeb 帳務資料的唯一 authoritative live database。
- 帳務 schema 以 `migrations/` 為正式來源。
- Desktop 維持高資訊密度與鍵盤效率，但 presentation 採現代、簡潔的商務 Web 風格；Tablet / Mobile 使用同一網站的 RWD + Adaptive UI，不另拆第二套前端。
- Public Git 不保存正式帳務資料、runtime secret、正式 Cloudflare resource identifiers 或 backup payload。

## 正式技術架構

```text
Browser
  ↓
Cloudflare Worker + Static Assets
  ├─ DB       → CYAccountingWeb D1
  ├─ IDENTITY → CYCloudIdentity provider
  ├─ BACKUP_R2
  └─ GCS runtime secrets/provider
```

主要技術：

- 前端：HTML / CSS / JavaScript
- API：Cloudflare Workers
- 靜態內容：Workers Static Assets
- 帳務資料庫：Cloudflare D1，binding 固定為 `DB`
- 帳號服務：private Service Binding `IDENTITY`
- 備份：Cloudflare R2 + Google Cloud Storage
- 部署：GitHub Actions + `wrangler.template.jsonc` 產生暫時 deploy config

正式 deployment-specific 值由 GitHub Deployment Environment / Secrets 注入，不固定寫入 public source。

## Production access / Custom Domain

正式使用者入口：

```text
https://acc.chihyuancm.com
```

技術備援仍保留：

```text
https://cyaccounting-web.simonliu1118.workers.dev
```

目前 deployment contract：

- Cloudflare Custom Domain：`acc.chihyuancm.com`；
- GitHub Environment variable：`CF_CYACCOUNTINGWEB_CUSTOM_DOMAIN`；
- `wrangler.template.jsonc` 以 `custom_domain` route 管理正式 hostname；
- `workers_dev: true` 明確保留 workers.dev fallback；
- Custom Domain rollout：PR #205；
- workers.dev fallback retention：PR #207；
- 2026-09-29 production deploy / user smoke test 已通過。

Domain namespace 的跨 App 規劃仍以 `chihyuan-web/docs/DOMAIN_STRATEGY.md` 為協調來源；CYAccountingWeb 不自行改動其他 App 的 hostname。

## 帳號與 Identity 邊界

V0.21.6 將 CYAccountingWeb 正式接到 **CYCloudIdentity (CYID)** consumer contract。CYID 是 Workspace / Employee / Credential / Workspace Role / App Access / provider Session / Email verification / Recovery 的唯一 Identity authority；CYAccountingWeb 不再建立第二套 Identity session。

Runtime 流程：

```text
Browser
  ↓ app-scoped HttpOnly + Secure + SameSite=Lax cookie
CYAccountingWeb Worker
  ↓ IDENTITY Service Binding
CYCloudIdentity
  ├─ login
  ├─ session/resolve
  ├─ logout
  └─ password recovery
```

固定規則：

- Application ID / Workspace ID 只由 deployment/runtime variables 注入，不寫入 Public source；
- raw provider Session token 只存在 HttpOnly cookie 與 CYID request Authorization，不進 JS storage、URL、log 或帳務資料表；
- protected request 由 CYID current authority resolve，CYACC 不保留 local `web_sessions` fallback；
- V0.21.6 cutover 只移除 active authority；舊 `web_sessions` 實體 table 暫時保留，避免 Worker deploy 失敗時舊版 runtime 因 migration 先行而失效。完成 development + production acceptance 後，再以後續獨立 forward migration 清除；
- CYID App Access 決定能否進入 CYAccountingWeb；CYACC 自己負責帳務 business authorization；
- `SUPER_ADMIN` / `ADMIN` 保留既有可寫入能力，既有 Super-Admin-only 功能仍只允許 `SUPER_ADMIN`；
- 有 CYACC App Access 的 `USER` 可登入，但只可檢視帳務資料與匯出 Excel；所有帳務 mutation、設定、匯入、移轉與 backup/restore 都由 server-side gate 拒絕；
- 未登入使用者只會載入獨立 `/login`；`/` / `/index.html` 先由 Worker 驗證 CYID Session，不再使用 full-app login overlay；
- 非核心 consumer 不處理 first-login / Email verification；若帳號尚未完成首次流程，必須回 CY Web 帳號管理入口；
- Password Recovery 直接委派 CYID，密碼長度採 8–16 Unicode code points，UI 不揭露是否存在帳號或 masked email；
- Tablet Safari compatibility 暫保留 `SameSite=Lax + Expires + navigation-safe`，直到真機驗收證明可收緊。

## CYID development smoke

在 development registry 與 runtime variables 設定完成後，可從受信任環境執行：

```bash
CYACC_SMOKE_BASE_URL="https://<development-host>" \\
CYACC_SMOKE_EMPLOYEE_NO="<4-digit-employee-no>" \\
CYACC_SMOKE_PASSWORD="<password>" \\
CYACC_SMOKE_EXPECTED_ROLE="USER" \\
node scripts/smoke-cyid-development.mjs
```

可選 `CYACC_SMOKE_MONTH=YYYY-MM`；未指定時使用執行當月。實際 hostname、Employee No、密碼與其他 runtime values 不得 commit、不得寫入 Actions log，也不得放進 Public artifact。

Smoke runner 驗證 standalone login、CYID Session cookie、`/api/auth/me`、bootstrap、交易讀取、USER mutation 403、Excel 匯出與 logout。它不會自行變更 Role / App Access，也不會觸發 Password Recovery 寄信；App Access revoke、Role 變更與 Session immediate invalidation 仍需在 development CYID 管理流程中人工切換後立即重試既有 Session。

Canonical references：

- shared standard：`apps/CYCloudIdentity/docs/CONSUMER_INTEGRATION_STANDARD.md`；
- adopted consumer version：`apps/CYAccountingWeb/CYID_CONSUMER_VERSION`；
- provider contract version / minimum：`apps/CYCloudIdentity/CONSUMER_CONTRACT_VERSION` / `CONSUMER_MIN_COMPATIBLE_VERSION`；
- CYACC-specific migration guide：`apps/CYCloudIdentity/docs/consumers/CYACC_INTEGRATION_HANDOFF.md`。

## 已完成核心功能

目前已完成：

- **V0.21.6 CYID integration**：獨立登入入口、CYID provider Session authority、USER 唯讀 + Excel export、server-side write gate、active source 移除 `web_sessions` authority、延後實體 table drop 的 rollback-safe cutover、runtime Application/Workspace ID deploy gate；

- 基本記帳新增、編輯、刪除；
- 帳戶、收入／支出大分類與科目管理；
- 期初餘額與逐月鎖帳；
- 常用摘要與設定；
- CYCloudIdentity 共用員工登入、provider Session、App Access 與 Email Password Recovery；
- 月份切換、摘要搜尋、月統計、逐筆餘額與帳戶分組；
- 記帳資料列直接編輯；
- 日期鍵盤快速輸入；
- 單月 `.xlsx` 匯出；
- `.xlsx` 匯入、欄位對應、預覽、驗證與重複略過；
- **V0.19.0 CYAccounting SQLite 帳本移轉工具**：瀏覽器本機解析 `.db`、schema/integrity 驗證、保守合併預覽、重複資料判斷、衝突阻擋與 D1 atomic commit；僅 `SUPER_ADMIN` 可執行；
- **V0.19.0 Build 1 D1 寫入安全修正**：bulk insert 改採 JSON1 展開，限制單一 JSON payload 與 batch statement 數，符合 D1 bound-parameter、2 MB string/row 與 Free plan 每 invocation query 上限；
- **V0.21.3 桌面帳本日期相容修正**：移轉時接受 CYAccounting 桌面版既有的 `YYYY/MM/DD`、`YYYY/MM` 日期／月份格式，送入 Web 帳本前正規化為 `YYYY-MM-DD`、`YYYY-MM`；
- **V0.21.4 操作回饋改善**：帳本移轉完成後顯示明顯完成視窗；記帳資料餘額可查看該筆後各帳戶餘額；預設帳戶與常用科目改為先更新畫面、儲存失敗再還原；
- **V0.21.4 Build 1 recovery**：V0.21.5 手機版故障回退後，暫時停用 HTML/JS 瀏覽器快取並替登入／主 overlay script 加 recovery cache key；登入成功後先驗證 Web session，再重新載入頁面，避免舊前端造成假性登入失敗；
- **V0.21.5 Build 1 Mobile safe retry**：重新實作手機四列兩欄新增記帳與單列看帳；改採既有 render output + mobile-only CSS，不使用 V0.21.5 Build 0 的自我重畫 MutationObserver；帳戶名稱依字數拆成上下兩行，手機餘額浮窗只顯示各帳戶；
- **V0.21.5 Build 2 Mobile layout correction**：手機新增記帳改為一列一件事並限制在單一 viewport 內；收入／支出使用整個記帳區綠／紅外框；看帳完整清除舊卡片式 `grid-row` 定位，回復真正一筆一列的固定欄位清單；
- **V0.21.5 Build 3 Mobile refinement**：記帳頁收入／支出改為邊緣向內約 20% 的綠／紅漸層、常用科目／摘要恢復固定高度橫向 chips、欄位文字統一置中；看帳上方月份／統計／搜尋框架收斂，交易列改為右滑露出編輯、左滑露出刪除，滑動本身不直接執行；
- **V0.21.5 Build 4 Mobile edit + ledger toolbar**：手機看帳工具列改為 `餘額｜<｜月份｜>｜更多`，更多只保留鎖帳與 Excel；月統計改為小卡並以淨利綠／淨損紅呈現；摘要搜尋改為單列 search field、鍵盤 Search/Enter 直接送出；手機右滑編輯改用共用新增記帳表單，儲存成功回原月份／原清單位置，中途離開則視為取消且不寫入資料庫；存檔成功提示移至儲存鍵下方約 2.5 秒淡出；
- **V0.21.5 Build 5 Mobile fix**：修正 Build 4 手機編輯接管可能因動態 script 載入時機而未初始化；手機標題列下方硬橫線移除；收入／支出恢復 Build 3 較強的四邊漸層；月統計小卡改為單排 `期初｜收入｜支出｜期末｜淨利/淨損`；
- **V0.21.5 Build 6 Tablet auth hotfix**：平板登入 overlay 改為鍵盤安全的 `dvh` 高度與可捲動配置，避免 iPad/Android 平板鍵盤彈出後登入卡被遮住且無法操作；auth stylesheet 加 cache key 強制更新；
- **V0.21.5 Build 7 Mobile toolbar + clear/cancel fix**：手機收入／支出舊版 3px 上緣硬線改以相同 specificity 明確清除；看帳月份列先保持穩定三欄，等餘額／更多工具完成建立後再切成 `餘額｜<｜月份｜>｜更多` 五欄；新增記帳儲存鍵下方新增「清空」，編輯模式自動改成「取消」，取消時不寫入資料庫並回原本看帳年月與清單位置；
- **V0.21.5 Build 8 Tablet auth + Mobile fix**：撤回 Build 6 造成 iPad Safari 觸控異常的 Tablet auth overlay 改動；登入成功後改由完整頁面導向後再驗證 session，auth fetch 改用 `credentials: include`，Web session cookie 改為 `SameSite=Lax` 並補 `Expires`；手機收入／支出 slider 外層背景改為透明並裁切圓角；看帳月份 picker 保留在原本 `#ledgerMonthSlot`，五欄工具列固定為 `餘額｜<｜月份｜>｜更多`；
- **V0.21.5 Build 9 Mobile month control**：只修手機看帳月份列，不碰 auth/session/CYID；畫面上的 `YYYY年M月` 改由 Web 自行繪製，原生 `input type=month` 保留為透明觸控層負責叫出手機原生月份選擇器，避免 iPhone Safari 原生 month control 溢出欄位與 `>` 重疊；
- **V0.21.5 Build 10 Mobile tools refinement**：手機 `餘額` 維持期初餘額設定，但改為緊湊的手機版編輯介面並預設帶入目前看帳月份；`更多` 收斂為帳戶設定、科目設定、月份鎖帳、匯出 Excel；帳戶／科目在手機只開被選取的設定頁，月份鎖帳改為獨立小確認視窗，不再把整個設定介面拉出；auth/session/CYID 本輪不動；
- **V0.21.5 Build 11 Mobile account menu cleanup**：手機右上角使用者選單移除「設定」，只保留身分資訊與「登出」；手機需要的設定功能仍保留在看帳頁 `更多`：帳戶設定、科目設定、月份鎖帳、匯出 Excel；Tablet / Desktop 設定入口不變；
- **V0.20.0 RWD / Adaptive UI Phase 1**：建立 Desktop / Tablet / Mobile presentation 分層；
- **V0.20.1 Mobile refinement**：收斂交易卡片資訊層級、inline edit 可視性、Header／搜尋／設定操作密度與窄手機 presentation；
- **V0.21.0 Desktop Business UI**：Desktop `>=1024px` 改為現代、簡潔的商務 Web presentation；
- **V0.21.1 Desktop interaction cleanup**：收斂自製日期／年月控制、產品 Dialog 與舊 manager 行為污染；
- **V0.21.2 category-management rewrite**：科目管理改為較緊湊的管理器，新增加大分類／科目入口集中於頂部，保留 drag reorder / cross-group move；
- Tiered Backup Phase A / B；
- Phase C R2 + GCS parallel dual-provider production path 與狀態 UI；
- `acc.chihyuancm.com` Custom Domain production rollout。

V0.19.0 SQLite 移轉功能雖已完成自動測試與正式部署，仍需以最新 `TODO.md` / production evidence 判斷真實帳本 acceptance 是否完成；不得只因程式存在就自動視為結案。

詳細待辦與未來方向以 [`TODO.md`](./TODO.md) 為準。

## 桌面 SQLite 帳本移轉

V0.19.0 的資料移轉設計：

```text
選擇 CYaccounting.db
  ↓ 瀏覽器本機 sql.js / WebAssembly 解析
SQLite integrity / foreign-key / schema 檢查
  ↓
正規化帳戶、分類、科目、交易、期初餘額、鎖帳
  ↓ 只有正規化資料送到 Worker；原始 .db 不上傳
SUPER_ADMIN server-side 驗證
  ↓
與目前 D1 建立保守合併預覽
  ↓
使用者確認
  ↓
D1 atomic batch commit
```

主要安全規則：

- 原始 SQLite 檔不傳到 Worker；
- 支援桌面 schema v1 / v2；
- 交易金額仍受 1～9,999,999 限制；
- 既有 Web 交易不因移轉而刪除；
- 同內容交易按「既有／來源出現次數」判斷重複，避免誤刪合法的重複交易；
- 同月份／帳戶的期初餘額若金額不同，視為衝突並阻擋；
- 同名科目若已存在於不同大分類，既有 Web 帳本採阻擋而不偷偷改分類；
- 鎖帳只會維持或變得更嚴格，不會因來源帳本而解鎖既有月份；
- 同一來源檔 SHA-256 已有成功移轉紀錄時，預設阻擋再次提交；
- bulk D1 write 採單一 JSON bind + `json_each(?)` 展開並限制 batch/payload 規模。

桌面版使用 SQLite WAL；選擇目前使用中的 `Data/CYaccounting.db` 前應先關閉 CYAccounting，或使用最近完成且已驗證的桌面備份，避免只取得尚未 checkpoint 的主資料庫檔。

## 備份目前狀態

CYAccountingWeb 正在 **Tiered Backup Phase C production acceptance**。

目前 production 行為：

```text
D1 authoritative live DB
  ↓ export once
one logical backup / one backupId / one immutable package digest
  ├─ R2  verified copy
  └─ GCS verified copy
```

Phase C 期間：

- schedule：每日 03:30（台灣時間）；
- R2 application retention：30 天；
- GCS：仍維持每日 + 14 天；
- manual paired production acceptance：**2026-09-27 已通過**；
- scheduled acceptance gate：**連續 14 次 production scheduled paired backup**；
- 手動備份不列入 `14` 次計數；
- 在 `14/14` 通過前不得進入 Phase D；
- `x/14` 必須讀 production catalog / UI，不得依日期推算。

Phase D 才會切成：R2 每日、GCS 每週三／週日 cross-cloud DR replication，GCS retention 26 週 / 182 天。

完整狀態與 migration guardrail 見 [`BACKUP_ARCHITECTURE_HANDOFF.md`](./BACKUP_ARCHITECTURE_HANDOFF.md)。

## UI / UX 方向

目前採單一網站的 Adaptive UI，資料與 API 共用，不建立獨立 PC／手機網站。

目前 presentation 分層：

- Desktop `>= 1024px`：V0.21.x 採現代、簡潔的商務 Web presentation，同時維持高資訊密度與鍵盤高速輸入；目前視覺要求為「精緻、商務、簡潔」；
- Tablet `768–1023px`：沿用 Adaptive UI；真實平板已確認目前 touch/focus/login 不可接受，Build 6/8 legacy auth hotfix 未取得 acceptance；在 CYID governed integration 收斂前不再疊 Tablet-specific legacy auth patch；
- Mobile `< 768px`：目前 formal baseline 為 Build 11；Build 10 direction 繼續保留，`餘額` = 期初餘額設定，`更多` = 帳戶設定／科目設定／月份鎖帳／匯出 Excel，右上角使用者選單只留身分資訊＋登出；仍需真實手機 final acceptance；
- Desktop 與 Mobile/Tablet presentation 分層維護，Desktop 改版不得反向覆寫 `<1024px` Adaptive UI；
- Desktop、Tablet、Mobile 仍需持續以真實裝置／尺寸做視覺 acceptance；自動測試只驗證 presentation boundary 與結構，不取代人工畫面驗收。

目前 workstream / acceptance / CYID integration 交接集中記錄於 [`HANDOFF_2026-09-30.md`](./HANDOFF_2026-09-30.md)；接手時不要從舊 Build 對話逐項重播。

CYAccountingWeb 是 Web project，**不自動套用 Windows Desktop Visual Guide 的 WinForms 尺寸／元件規則**。

## 本機開發

```bash
npm install
npm run dev
```

`npm run dev` 會先下載並驗證固定版本的 `sql.js` browser runtime；產生的 `public/vendor/sqljs/` 是 build/runtime 衍生物，不提交 Git。

正式 deploy 不使用 commit 到 Git 的 production Wrangler 檔；CI/CD 由 `wrangler.template.jsonc` 與 Deployment Environment 產生暫時設定。

## 文件責任

- `PROJECT_RULES.md`：CYAccountingWeb 專案補充／永久規則。
- `REPOSITORY_RULES.md`、`REPO_POLICY.md`：repository 共通治理與 Public repo 安全規則。
- `TODO.md`：目前完成狀態、待辦與未來方向，不是永久規則。
- `BACKUP_ARCHITECTURE_HANDOFF.md`：目前 Tiered Backup migration / acceptance handoff。
- `HANDOFF_2026-09-30.md`：Current conversation / workspace continuity note，不是永久規則。
- `HANDOFF_2026-09-29.md`：Historical continuity checkpoint；不得當作 current state。
- `README.md`：專案入口與現況摘要。

若文件描述與實際程式版本不一致，先讀目前 `main`、`VERSION` / `BUILD`、migrations、source 與最新 Actions，再更新狀態文件；不得只依舊 README 或舊 handoff 直接修改 production。
