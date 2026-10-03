# CYAccountingWeb

志遠記帳系統 Web 版；與 `apps/CYAccounting/` Windows 版分開維護。

更新：2026/10/03（日本時間）。本文件是專案入口與現況摘要，永久規則依根 `REPOSITORY_RULES.md`、`REPO_POLICY.md` 與本專案 `PROJECT_RULES.md`。

## 目前狀態

| 項目 | 狀態與證據 |
| --- | --- |
| 正式網站 | [acc.chihyuancm.com](https://acc.chihyuancm.com) |
| 正式版本 | **V0.22.1 Build 1**；main `d822afd779642fbeae745d0f643c5d7d8fb1f1ab`，PR #290；[部署 #370](https://github.com/simonliu1118-byte/CYapps/actions/runs/37035827619) 成功 |
| 公開穩定 Release | [V0.22.0](https://github.com/simonliu1118-byte/CYapps/releases/tag/cyaccountingweb-v0.22.0)；公開 Release 與網站部署分開 |
| 平板開發 | **V0.22.2 Build 0**；`cyaccountingweb/tablet-interface`，[Draft PR #292](https://github.com/simonliu1118-byte/CYapps/pull/292)，尚未合併、部署或公開 Release |
| 平板驗證 | 程式與自動測試通過；真實 iPad／Android 觸控、鍵盤、登入及排版仍待驗收 |
| 桌面帳本移轉 | 使用者於 2026/10/03 確認內容 OK，內容驗收已完成 |
| 備份 | Phase C；手動雙副本驗收通過，排程連續成功 `x/14` 尚需讀正式 catalog／UI，未推算進度 |

上列 main SHA 指功能部署基準；後續純文件提交不代表新版功能已部署。接手時以最新 branch／PR／Actions 再核對。

## 文件入口

| 文件 | 用途 |
| --- | --- |
| [PROJECT_RULES.md](PROJECT_RULES.md) | 專案唯一永久規則補充 |
| [WORK_HANDOFF.md](WORK_HANDOFF.md) | 目前交接、分支、驗證證據與下一步 |
| [TODO.md](TODO.md) | 尚未完成的工作與驗收清單 |
| [docs/TABLET_UI_DESIGN.md](docs/TABLET_UI_DESIGN.md) | 平板雙方向設計、目前實作與驗收差距 |
| [BACKUP_ARCHITECTURE_HANDOFF.md](BACKUP_ARCHITECTURE_HANDOFF.md) | 備份模組、格式、Phase C 與復原邊界 |
| [CHANGELOG.md](CHANGELOG.md) | 版本里程碑；歷史變更不作目前操作指南 |
| [docs/archive/](docs/archive/) | 舊交接快照，已標記歷史，接手不需逐份重播 |

## 已上線功能

- 記帳新增、編輯、刪除；月份切換、摘要搜尋、月統計、逐筆及各帳戶餘額。
- 帳戶新增、改名、預設、排序、封存／解封；SUPER_ADMIN 永久刪除符合條件的帳戶。
- 收入／支出大分類與科目、常用科目、分類上下排序及科目拖曳跨分類。
- 自動承接期初餘額；人工例外需理由及 append-only 稽核，記錄 CYID 操作者、時間及實際前／後值。
- 月份鎖帳；Excel 匯出共用期初計算；Excel 匯入有預覽、驗證與重複略過。
- 桌面 SQLite 帳本在瀏覽器本機解析，原始 `.db` 不上傳；SUPER_ADMIN 預覽、確認後原子寫入。
- CYID 共用登入、Session、App Access 與 Password Recovery contract；USER 唯讀並可匯出 Excel。
- D1 匯出一次，R2／GCS 分別保存與驗證同一 logical backup；復原及災難復原尚未完成。

帳戶名稱最多八個 Unicode 字元；手動摘要最多 40 weighted units（20 個全形／40 個 ASCII 字元），金額 1～9,999,999。當月有效期初覆寫顯示「調整」標籤，已清除覆寫則不顯示。

SUPER_ADMIN 永久刪除條件為已封存、無交易、最新有效期初為零；早期非零基準及稽核保留，不能重用已稽核帳戶名稱。Migration 0006 已把舊期初表轉成 override／audit，現行 schema 為 6。

## 跨裝置介面與共用流程

正式版手機保留新增／看帳兩頁、滑出後再點的編輯／刪除、共用彈窗及 `餘額／更多` 工具。記帳收支區使用純色收入 `#f4fbf6`、支出 `#fff6f5`；科目欄顯示「大分類／科目」，送出的資料值仍為原科目名稱。封存帳戶由「已封存帳戶」開啟獨立、隨內容高度變動的視窗。

桌機保留鍵盤快速輸入及列內編輯。平板開發版橫向左記帳右看帳，直向看帳為主、底部記帳欄可收合／保持展開；帳戶、日期及月份優先用原生元件。詳細範圍見平板設計。

所有裝置共用 Worker、CYID、權限、API、帳務計算及 `persistTransactionUpdate`。交易編輯與一般設定變更先呈現結果，失敗還原；期初基準、刪除、鎖帳、移轉等高風險操作等待伺服器。一般新增記帳仍沿用共用的既有送出流程，尚未改為 optimistic，不將它誤列成已完成項目。

## 架構與執行設定

前端使用原生 HTML／CSS／JavaScript；Worker 入口為 `src/app.js`。D1 binding 為 `DB`，CYID private Service Binding 為 `IDENTITY`；備份使用 `BACKUP_R2` 與 GCS provider。正式 resource identifiers、帳務資料、憑證及 Session 不放入 Public Git。

Identity 技術規範直接引用 [CYID consumer standard](../CYCloudIdentity/docs/CONSUMER_INTEGRATION_STANDARD.md)。CYACC 採用 `1.0.1`，provider 支援 `1.0.0–1.0.2`。登入走獨立 `/login`、navigation-safe server redirect 與 provider Session；沒有裝置專屬登入 authority。舊 `web_sessions` 只有實體表待退休，已不是 runtime fallback。

正式部署由 GitHub Actions 配合 `wrangler.template.jsonc` 及 Deployment Environment 暫時產生 config；`workers_dev: true` 備援仍保留。Domain 協調來源為 `chihyuan-web/docs/DOMAIN_STRATEGY.md`。開帳自動計算起始年月尚待使用者補齊資料後提供，這次沒有修改正式帳務基準。

## 開發與驗證

```bash
npm ci --no-audit --no-fund
npm run dev
```

`npm run dev` 準備固定版本、驗證 hash 的 sql.js browser runtime 並產生本機 config；`public/vendor/sqljs/` 是衍生物，不提交 Git。正式 CI 測試清單由 `.github/workflows/cyaccountingweb-deploy.yml` 維護，CYID 相容檢查為 `node scripts/validate-cyid-consumer-version.mjs`。

隔離 Identity 預覽使用 `wrangler.cyid-development.template.jsonc`：獨立 Worker／D1、development CYID binding，沒有正式 route、Cron 或備份 binding。低風險 smoke 可執行 `scripts/smoke-cyid-development.mjs`，所需帳號／密碼只從受保護 runtime 提供；完整 Role／App Access／Session live acceptance 已於 CYID development run #96 通過。真實 Password Recovery Email/browser 驗收仍獨立待辦。
