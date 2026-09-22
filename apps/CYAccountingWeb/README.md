# CYAccounting Web

志遠記帳系統 Web 版。此專案與 `apps/CYAccounting/` Windows 版分開維護；Windows 版仍是既有桌面正式產品，Web 版不直接改寫其 PySide6 程式。

## 技術架構

- Cloudflare Workers：同網域 API
- Workers Static Assets：HTML / CSS / JavaScript
- Cloudflare D1：帳務資料庫
- GitHub：原始碼與 migrations 版本管理

Cloudflare Workers 設定以 `wrangler.jsonc` 為 source of truth。

## V0.1.0 範圍

第一階段建立可部署骨架與最核心記帳流程：

- 日期、帳戶、收入／支出、科目、摘要、金額
- 金額限制 1～9,999,999
- 新增交易
- 依月份顯示交易
- 月收入／支出／收支摘要
- 刪除交易
- D1 初始 schema 與預設「現金／一般收入／一般支出」
- 桌機與手機基本 responsive UI

尚未搬入：登入／權限、鎖帳、期初餘額管理、帳戶與科目管理、表格直接編輯、常用摘要、Excel 匯入匯出、既有 SQLite 資料遷移、正式備份策略。

## 本機開發

```bash
npm install
npm run dev
```

目前 `wrangler.jsonc` 尚未放入正式 D1 binding。建立 Cloudflare D1 後，再加入 `DB` binding，並執行 migration。

## D1 命名

- Worker：`cyaccounting-web`
- D1 database：`cyaccounting-web-db`
- Worker binding：`DB`

正式 Cloudflare database ID 不得自行猜測；以 Cloudflare 建立 D1 後回傳的 UUID 為準。

## 安全

`CYapps` 是 Public repository。不得提交真實帳務資料、SQLite/D1 正式資料 dump、Cloudflare API token、OAuth secret、refresh token、`.env`、`.dev.vars`、runtime log 或 backup。

## 與 Windows CYAccounting 的關係

Web 版資料模型初期刻意接近現有 SQLite schema，降低日後匯入既有帳本的風險；但 Web 版將以 D1 migrations 管理 schema，不共用本機 SQLite 檔案。
