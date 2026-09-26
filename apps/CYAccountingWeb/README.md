# CYAccountingWeb

志遠記帳系統 Web 版。此專案與 `apps/CYAccounting/` Windows 版分開維護；Windows 版仍是獨立正式產品線，Web 版不得因功能移植而覆蓋或破壞桌面版。

> Current formal baseline: **V0.19.0 Build 1**（2026-09-27）

## 專案定位

CYAccountingWeb 是以 Cloudflare 為正式執行平台的多人 Web 記帳系統。

目前核心原則：

- Cloudflare D1 是 CYAccountingWeb 帳務資料的唯一 authoritative live database。
- 帳務 schema 以 `migrations/` 為正式來源。
- 桌面操作維持高資訊密度與鍵盤效率；Mobile 後續以同一網站的 RWD + Adaptive UI 擴充。
- Public Git 不保存正式帳務資料、runtime secret、正式 Cloudflare resource identifiers 或 backup payload。

## 正式技術架構

```text
Browser
  ↓
Cloudflare Worker + Static Assets
  ├─ DB       → CYAccountingWeb D1
  ├─ IDENTITY → 目前的共用員工帳號服務 contract
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

## 帳號與 Identity 邊界

目前 CYAccountingWeb 使用的共用員工帳號權威仍暫由 **CYInvoice Cloud** 提供，但 CYAccountingWeb **不直接讀取 CYInvoice D1**。

目前流程：

```text
CYAccountingWeb
  ↓ IDENTITY Service Binding
CYInvoice Cloud Web Auth contract
  ↓
回傳 employee identity / role / credential metadata
  ↓
CYAccountingWeb 自己的 D1 建立本系統 web session
```

跨 App 的 Identity / SSO 正在由 **CY-WEB workstream** 逐步規劃抽離與共用化。涉及下列底層項目時，不在 CYAccountingWeb 單獨決定：

- Identity / SSO authority；
- 跨 App 帳號、角色、App access contract；
- 跨 App D1 / database ownership；
- Service Binding 與 shared Worker；
- shared Backup Service 與 app-scoped dataset routing。

這些項目實作前必須先同步 CY-WEB 最新決策。CYAccountingWeb 的帳務 D1 仍保持獨立，不因共用帳號而合併資料庫。

## 已完成核心功能

目前已完成：

- 基本記帳新增、編輯、刪除；
- 帳戶、收入／支出大分類與科目管理；
- 期初餘額與逐月鎖帳；
- 常用摘要與設定；
- CYInvoice Cloud 共用員工登入、Session、Email 忘記密碼；
- 月份切換、摘要搜尋、月統計、逐筆餘額與帳戶分組；
- 原列 inline editing（Enter 儲存、Esc 取消）；
- 日期鍵盤快速輸入；
- 單月 `.xlsx` 匯出；
- `.xlsx` 匯入、欄位對應、預覽、驗證與重複略過；
- **V0.19.0 CYAccounting SQLite 帳本移轉工具**：瀏覽器本機解析 `.db`、schema/integrity 驗證、保守合併預覽、重複資料判斷、衝突阻擋與 D1 atomic commit；僅 `SUPER_ADMIN` 可執行；
- **V0.19.0 Build 1 D1 寫入安全修正**：bulk insert 改採 JSON1 展開，限制單一 JSON payload 與 batch statement 數，符合 D1 bound-parameter、2 MB string/row 與 Free plan 每 invocation query 上限；
- Tiered Backup Phase A / B；
- Phase C R2 + GCS parallel dual-provider production path 與狀態 UI。

V0.19.0 SQLite 移轉功能完成自動測試與部署後，仍需使用真實桌面帳本進行 production acceptance；在完成實機驗收前不視為資料遷移工作正式結案。

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
- Build 1 將 bulk D1 write 改為單一 JSON bind + `json_each(?)` 展開，交易以最多 400 筆／statement、期初餘額最多 1,000 筆／statement 寫入，並在送出前限制整體 batch statement 數。

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
- 在 `14/14` 通過前不得進入 Phase D。

Phase D 才會切成：R2 每日、GCS 每週三／週日 cross-cloud DR replication，GCS retention 26 週 / 182 天。

完整狀態與 migration guardrail 見 [`BACKUP_ARCHITECTURE_HANDOFF.md`](./BACKUP_ARCHITECTURE_HANDOFF.md)。

## UI / UX 方向

已完成 Desktop UI/UX Phase 1～3。後續原則：

- Desktop：高資訊密度、鍵盤高效率輸入；
- Tablet / Mobile：同一網站 RWD，不做兩套獨立網站；
- Mobile 以查詢、確認、快速輸入與簡單修改優先；
- 功能穩定後再集中進行完整 UI/UX 重整，避免反覆返工。

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
- `README.md`：專案入口與現況摘要。

若文件描述與實際程式版本不一致，先讀目前 `main`、`VERSION` / `BUILD`、migrations 與 source，再更新狀態文件；不得只依舊 README 或舊 handoff 直接修改 production。
