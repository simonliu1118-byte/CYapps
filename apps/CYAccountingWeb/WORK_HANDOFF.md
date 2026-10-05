# CYAccountingWeb 目前工作交接

更新：2026/10/05（日本時間）。本文件只描述目前狀態，不新增永久規則。接手順序仍為根 `AGENTS.md` → `REPOSITORY_RULES.md` → `REPO_POLICY.md` → 本專案 `PROJECT_RULES.md`，之後才讀本文件、README、TODO 與其他狀態文件。

## 正式基準、部署與治理

| 範圍 | 最新已確認狀態 |
| --- | --- |
| 正式功能基準 | **V0.22.17 Build 0**；PR #321 已合併；手機／平板登入頁採共用 keyboard-aware viewport 處理 |
| Production Deploy | CYAccountingWeb Validate and Deploy **#471**（run `37315686210`）成功；validate、D1 migration、Worker/static assets、secure login 與 semantic frontend assets 全部成功 |
| 公開穩定 Release | **V0.22.15**，tag `cyaccountingweb-v0.22.15`；Stable Release **#2**（run `37254391804`）成功；前一個 V0.22.0 Release/tag 已移除 |
| Governance | Common Rules **2.8.0**；CYapps Governance **2.3.28**；AITeam 與 CYapps 的 `REPOSITORY_RULES.md` 已核對為同一 blob |
| CYID | CYACC consumer **1.0.1**；CYID contract **1.0.2**；minimum compatible **1.0.0** |
| D1 schema | **7**；最新 migration `0007_account_color_slots.sql` |
| 下一工作線 | 本次交接沒有指定新的功能 branch；下一個獨立工作應由目前 main 另開 branch，不從未合併的舊實驗 branch 延續 |

純文件更新不升 `VERSION`／`BUILD`。正式版本來源仍是專案根 `VERSION` 與 `BUILD`。

## 近期版本收斂

- **V0.22.2 ～ V0.22.3 Build 3**：完成平板雙方向介面、照片返修、原生日期／月份 owner 收斂及 desktop interaction 判定整理。PR #297 的 V0.22.2 Build 4 **未合併**，已被後續 V0.22.3 系列取代，不是有效基準。
- **V0.22.4 ～ V0.22.8**：完成架構整理，移除 V0214 retry patch、收斂 Settings、transactionRows renderer/lifecycle、Ledger Toolbar，以及剩餘 V06/V09/V19/V20/V21／Build 式 runtime 殼與版本式命名。
- **V0.22.9**：手機新增記帳預設改為收入；手機日期畫面固定 `YYYY/MM/DD`，資料 contract 仍為 `YYYY-MM-DD`；常用摘要改走明確 bootstrap lifecycle，移除重複 API owner。
- **V0.22.10**：先以 shared Ledger renderer 導入帳戶顏色辨識。
- **V0.22.11**：配色升級為正式帳戶色號 lifecycle，新增 `accounts.color_slot` 與 schema 7；改名／排序／封存／解封不換色，永久刪除後才釋出，新帳戶優先取得最小空 slot。1–20 淡色深字、21–40 對應深色淺字，41 起每 40 個循環；不改 ledger 欄寬。
- **V0.22.12**：手機「更多」新增 SUPER_ADMIN-only 唯讀備份資訊，共用既有 `/api/backup/status` 與 Backup UI model；Ledger message row 從 transaction-card presentation 分離；Backup UI 移除函式覆寫 patch chain。
- **V0.22.13**：iPhone 實機返修。手機記帳日期撤回自製 `YYYY/MM/DD` overlay，恢復原生 `type=date`；恢復新增「儲存／清空」與編輯「儲存修改／取消」，取消沿用既有 return context 回原月份／原位置；空月份提示置中且無底線；帳戶／科目增加小幅間距；備份資訊移除多餘說明文字。
- **V0.22.14 Build 3～5**：手機記帳列高與字級微調，並修正底部操作區定位。最終 Build 5 移除 Save 的 `grid-row:auto` 衝突，讓金額後的彈性 spacer 真正依螢幕高度伸縮；清空／取消貼近底部頁籤、儲存緊鄰其上。手機帳號按鈕改等 `cyacc:session-ready` 與真實 `currentUser` 後才顯示，不再閃過「帳號」placeholder。
- **V0.22.15**：使用者指定正式公開 Release，Build 歸零；PR #318、Production Deploy #461、Stable Release #2 全部成功。公開 tag 為 `cyaccountingweb-v0.22.15`，舊 V0.22.0 Release/tag 已移除。
- **V0.22.16**：手機／平板登入頁導入 `visualViewport`、touch 無 autofocus、body scroll reset 與 keyboard-aware shell，解決 iOS 鍵盤遮擋與收起後長捲軸。
- **V0.22.17**：依實機照片返修登入 presentation。手機固定使用同一套 compact 尺寸；平板直式維持較大固定尺寸；平板橫式改為 touch-tablet 雙欄布局，左品牌、右表單，員工編號／密碼並排以壓低高度。PR #323 的 Governance Check #1039 與 Validate #470 成功，Production Deploy #471 成功。

完整版本歷史見 `CHANGELOG.md`。

## 現行 canonical owner

### Backend / domain

| Concern | Canonical owner |
| --- | --- |
| CYID transport、Session/login/logout/recovery adapter | `src/identity-adapter.js` |
| Worker route、accounting authorization、API orchestration | `src/app.js` |
| 期初餘額 carry-forward / override snapshot | `src/opening-balances.js` |
| 帳戶封存／解封／永久刪除規則 | `src/account-lifecycle.js` |
| Backup package / topology | `src/backup-package.js` → `src/backup-service.js` → provider adapters |
| Desktop SQLite migration | `src/desktop-migration-core.js` + `src/desktop-migration.js` |

### Frontend

| Concern | Canonical owner |
| --- | --- |
| Transaction state、month load、canonical transaction mutation、optimistic rollback | `public/app.js` |
| Ledger transaction rows、row lifecycle、Ledger Toolbar | `public/ledger-tools.js` |
| Inline edit presentation | `public/ledger-inline-edit.js`，寫入仍委派 canonical mutation |
| Quick-entry / frequent presentation | `public/quick-entry.js` |
| Settings lifecycle / renderer / action surface | `public/adaptive-ui.js` 的 `window.cySettingsManager` |
| RWD / native picker / gesture / device presentation | `public/adaptive-ui.js` |
| Input confirmation drawer | `public/input-confirmation.js` |

目前沒有核准永久第二套 business/data owner。修 Bug 先找上述 owner；若開始需要第二 renderer、internal MutationObserver、retry bootstrap、DOM relocation、compatibility wrapper、duplicate device business component 或新的 override chain，套用 Architecture Review Trigger，不直接疊下一層。

## 帳務、帳戶與資料庫現況

- Migration 0006 已將 `opening_balances` 轉為 `opening_balance_overrides` + append-only `opening_balance_audit`；Migration 0007 新增 `accounts.color_slot`，目前 schema 7。
- 期初餘額預設自動承接；人工調整需理由、append-only audit、實際前／後值及 CYID principal。高風險期初調整等待 server 成功。
- SUPER_ADMIN 永久刪除條件仍為：帳戶已封存、零交易、最新有效期初為零；較早非零基準與 audit 保留，已留 audit 的名稱不得重用。
- 帳戶 `color_slot` 由 D1 trigger 統一分配，避免一般新增、Desktop Migration 或不同裝置各自建立第二套 allocator。
- Backup v2 format contract 未因色號升版；目前產生的 schema 7 payload 可帶 additive `colorSlot` metadata。舊 schema 6 備份屬歷史相容資料，不改寫既有物件。

## 跨裝置現況

- **手機**：新增／看帳兩頁；新增預設收入；日期使用原生 `type=date`；新增為「儲存／清空」、編輯為「儲存修改／取消」且取消回原 ledger context；操作區在底部頁籤上方，金額與儲存之間依可用高度彈性伸縮；右上帳號等 CYID Session 真實 identity ready 後才顯示；登入頁依 `visualViewport` 處理鍵盤高度，手機有無鍵盤維持同一 compact 尺寸；交易滑出後再點編輯／刪除；`更多` 中 SUPER_ADMIN 可查看唯讀備份資訊；共用 `餘額／更多`、設定與 canonical writer。
- **平板**：橫向左記帳／右看帳，直向看帳主區＋底部記帳 rail；日期／月份與手機共用 touch/native owner，不建立平板第二套資料 state 或 writer；登入頁同樣共用 keyboard-aware login owner；直式固定較大尺寸，橫式用雙欄 presentation，皆不另建登入流程。
- **桌機**：保留鍵盤高效率輸入、inline edit 與桌面 layout。
- 所有裝置共用 CYID、Role/App Access、Worker API、D1、期初計算、transaction mutation、帳戶色號與 server authorization。RWD 只負責 presentation。

## 已確認驗證

V0.22.17 PR #323 的 Governance Check #1039 與 CYAccountingWeb Validate and Deploy #470 均成功。合併後 Production run #471（`37315686210`）validate 與 deploy 全部成功。V0.22.15 的 Stable Release run #2（`37254391804`）仍是目前公開 Release 證據。已確認：

- JavaScript syntax 與 application tests；
- mobile native-date、底部儲存／清空或取消、identity readiness，以及 login viewport／keyboard regression；
- D1 migrations local validation 與 production migration；
- Worker dry-run 與正式 Worker/static assets deployment；
- secure login entry 與 semantic frontend asset verification；
- Release source package 的公開套件安全掃描；
- source archive SHA-256 產生與自我驗證；
- `cyaccountingweb-v0.22.15` Release/tag 成功建立，舊 V0.22.0 Release/tag 成功移除。

這些自動證據不取代真實 iPad／Android 的所有觸控、鍵盤、原生 picker、分享與版面情境驗收。

## 尚未完成

待辦以 `TODO.md` 為唯一目前清單，重點仍包含：

1. 平板與手機／桌機的剩餘真機交叉驗收。
2. 一般新增記帳的共用 optimistic update／rollback。
3. 使用者補齊資料後再決定自動計算起始年月。
4. Password Recovery 真實 Email/browser 完整驗收。
5. CYID 穩定後以 forward migration 退休 `web_sessions` 實體表。
6. Backup Phase C 正式 catalog `x/14`、後續 Phase D、Restore 與 DR 演練。

不要按日期推算 Phase C 進度，也不要把未合併的歷史 branch／PR 當成目前 source。
