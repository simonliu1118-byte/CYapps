# CYAccountingWeb 工作交接

更新：2026/10/29（台灣時間）。描述正式狀態與驗收，永久規則依正式三層來源。

## 最新狀態：V0.25.13 已部署，V0.25.14 備份修正待部署

- V0.25.13／schema13 已由 [PR #575](https://github.com/simonliu1118-byte/CYapps/pull/575) 合併，main source `6d238eb54faa188f9bbc0ece4cb92de871f4628d` 的 [dispatch run 40838160660](https://github.com/simonliu1118-byte/CYapps/actions/runs/40838160660) validate／deploy 成功。0012／0013 已套用，原資料與綁定核對通過；期初快照命中已有正式低讀取回讀。備份／實際還原與資源證據只保存在本機私人維運目錄。
- V0.25.14 沿唯一 backup owner 補上「副本成功、健康檢查中斷」的 08:00 補驗；當天已健康則不重驗，補驗不重建備份、不改帳務。狀態頁合併副本查詢，固定 16 個 operational key 以四筆並行／五秒上限讀取；逾時保留有效備份狀態，真實健康失敗不隱藏。
- schema13、CYID contract1.0.6、現有資源與權限保持；本批沒有 migration。需要必要 CI、當次受控部署及正式回讀，部署成功不等同下一次健康檢查已完成；相關待辦見 TODO。
- 下列 V0.25.5 及更早的段落保留為歷史驗收，不作目前版本或目前綁定的證據。

## 歷史：部署模式變更

使用者於 2026/10/16 改採與 CY Web 相同的合併／發布分離：**本批合併後 main 不再自動部署**。PR 保留自動驗證；Work 在收到當次發布指示後，將 `deploy/cyaccountingweb-production` 前進到已審核的當前 main，或從 main dispatch `cyaccountingweb-deploy.yml`，才會上線。完整指令、GitHub UI 備援與成功判定統一看 [README 正式部署操作](README.md#正式部署操作)，避免後續只合併 PR 卻誤以為已部署。

發布來源必須是當前 main 的完整 SHA；排隊後 main 已更新會停止。正式部署鎖固定且 cancel=false，PR 驗證獨立取消；原有 Environment、CYID／migration／登入／資產檢查保留。Stable Release 改查相同 SHA 的成功 deploy job，validation-only／skipped 不算已部署。

AITeam `docs/infrastructure/CLOUDFLARE_CONSOLIDATION_PLAN.md` 於 2026/10/12 記錄的「保留 main 自動部署」是先前決策；本次使用者已改變部署模式，後續操作以本專案更新後的 PROJECT_RULES 與 README 為準。

這次只調整部署流程與文件，產品仍為 V0.25.5。既有下列 main 自動部署 run 均是**舊模式的歷史驗收證據**；本次 PR 本身不部署網站，首次新模式的實際發布仍待當次發布指示與成功 run 回讀。

## 歷史：V0.25.5 基準及驗證

| 項目 | 已確認狀態 |
| --- | --- |
| 產品 | **V0.25.5**／PR #473 已合併；正式 migration／Worker／登入與桌機設定目視回讀成功。已移除部署檢查的 retired preview marker，最終 Actions 證據見本批收尾 PR；產品 source／版本不變 |
| 日期修復 | [PR #441](https://github.com/simonliu1118-byte/CYapps/pull/441)；本版 merge／main Actions 與正式瀏覽器回讀為驗收依據 |
| 統計返修 | PR #409、#410、#411 已合併；Build 2 merge `19e5318446ce2295d1993f41c5b187a1ac22ea63` |
| 統計部署 | [run 38160506567](https://github.com/simonliu1118-byte/CYapps/actions/runs/38160506567)，validate／deploy 成功 |
| 後續部署設定 | Provider base-name 改讀既有共通 Secret；[main run 38191534386／#608](https://github.com/simonliu1118-byte/CYapps/actions/runs/38191534386)成功。只改部署 input，產品 VERSION／BUILD、Identity authority 與帳務 runtime 不變 |
| 公開 Release | **V0.22.15**，tag `cyaccountingweb-v0.22.15`；不與正式網站版本混用 |
| D1 | schema **11**，退休舊 Session 表並新增衍生備份檢查狀態；既有帳務／受控復原稽核保留 |
| CYID | consumer **1.0.1**；canonical 已發布 contract **1.0.5**、minimum **1.0.0**。實際環境部署另核對，不以原始碼版本推定 |
| 共通規則 | AITeam main 與本 repo Common Rules **2.8.0** 已直接核對；repo 治理以根 VERSION／CHANGELOG 為準 |

本次 V0.25.0 新增獨立 D1 受控復原，Minor 升版、Build 歸零；PR #450 已合併（da0c618a9ff6cc2da91ab3d99033f4b75b69e5c6），main run 38671076691 validate／deploy／migration／正式 semantic probes 全部成功。真實 GCS → 獨立 D1 演練已完成，完整欄列／稽核／政策／資料庫檢查通過。不建立 Stable Release、不切換正式帳本、不更動手機／平板原版面。

## V0.25.5 備份監測與清理

- `backup-service.js` 沿既有 daily Cron，在備份完成或失敗後各驗證最新應有 R2／GCS copy；Phase D 不要求 GCS 每日都有新副本。共用原 immutable digest／catalog／fresh SQLite verifier，核對全部欄列、integrity／FK，不寫正式帳務。失敗、過期或不完整證據均不回退舊成功。
- 唯一 `backup_health_state` 保存衍生狀態、lease、active incident 及寄信結果；不存收件信箱，亦不混入 portable accounting／recovery evidence。修復清除 active incident；相同未恢復事件不重寄，失敗在下次排程重試。復原驗證工作區不啟動背景備份／檢查。
- CYID 0.3.10／contract 1.0.6 的 private `ConsumerNotifications` 解析目前受保護超管信箱，沿原 Email adapter／額度，固定通知內容、delivery slot 與冷卻。CYACC `BACKUP_NOTIFICATIONS` 掛到與 IDENTITY 相同 provider，沒有複製郵件憑證或 Identity authority。
- Backup UI 移除手動驗證／結果說明；底部左還原操作、右緊急復原入口。超管 boot 只讀健康狀態，異常以唯一 alertdialog 提醒；正常不增加檢查說明區。統計移轉設定沿原 form／設定 API，緊密雙欄、窄畫面單欄。
- 刪除手機平板 preview bootstrap、session state、menu／return action、Excel preview 分支及對應 CSS；真實平板 classification、layout、native picker／share 不改。forward migration 0011 只 DROP 舊 web_sessions，新增檢查狀態；CYID 仍是唯一 Session authority，帳務／稽核保留。新 schema 11、舊 schema 6～10 正規化／還原保留，sandbox 套用全 migrations。
- 正式桌機 1363×936 已登入回讀 V0.25.5；統計設定左右等寬、列高約 30px，無水平溢出；備份底部左右各 236.5px、同高，手動檢查控制與結果節點皆已移除。首次 main run 38846351061 尾端因殘留 preview probe 失敗，收尾 PR 移除舊 marker 並補 architecture regression；不回復已退休程式。
- 本地測試包含真實 SQLite roundtrip、排程失敗、R2-only 日、損壞／過期、通知重試／去重／lease、超管限制與復原 hold；provider 98 tests、auth／first-login local Worker 驗證通過；完整本地 80 項 workflow checks 通過，補充 forward migration 全帳務保留與 DB 中斷獨立通知回歸。共用通知 PR #459 已合併；正式部署門檻依支援範圍修正追蹤 #471。正式首次 daily check 仍需排程後觀察，不發假異常信或更動正式備份作驗收。

## V0.25.4 首次登入引導

- PR #435 依最新主線重新整合，不帶回舊記帳／備份 source；共用登入頁說明先於 CY Web 驗證 Email、設定正式密碼後再登入。只讀 /api/auth/account-portal 沿 identity-adapter 輸出安全 HTTPS root，不查帳務或建立 Session。
- 入口連結不附員工編號、密碼、Session 或 first-login ticket；新分頁採 noopener noreferrer。非列舉式帳密錯誤可提示首次登入步驟，FIRST_LOGIN_REQUIRED 明確引導；真正 provider 5xx 保留服務錯誤分類，不建立第二啟用流程。
- CY Web PR #154／正式 run 38712562070 已修正 authority；後續 run 38717773511（2026/10/15 18:07 台灣時間）回讀 canonical production、Workspace、core App、consumer 1.0.5／health 一致、productionPortalReady=true。帳務 D1 未切換，歷史 development environment 名稱不是 authority 判斷依據。
- CYACC 自己的既有正式部署 run 38678168558 attempt 2 已於 2026/10/15 20:36 通過完整 portal／CYID authority／Workspace／consumer health 檢查；未變更 VERSION／BUILD。首次登入版本 production deploy 再強制 --require-account-portal，完整 gate 通過才注入 metadata。既有 provider runtime guard 始終強制，不把 URL 或單純 CI 綠燈當正式 readiness。
- 使用者於 2026/10/15 確認收信及啟用已驗收，後續歸 CYWEB 維護；不把不同裝置的一般登入／Session 或 Password Recovery 銜接自動視為均已驗收。手機／平板共用原登入 shell、native form／Session／recovery；只增加此功能必要說明與入口。

## V0.25.3 新增記帳即時顯示與失敗回復

- app.js 沿唯一 saveTransaction／state／loadTransactions：待存筆使用暫存負 ID，立刻顯示並清空摘要／金額，允許接續下一筆。既有 POST 在相同寫入後回傳 canonical row，成功只替換該筆，不重讀相同月份或跳回舊月份。
- 失敗撤回該筆；只有尚未輸入／清空／切換編輯的新草稿才回填失敗值，較晚結果不清除新內容或搶焦點。beforeunload 在仍有待確認新增時保留瀏覽器離頁警告；不自動重送，連線中斷仍須重新整理確認實際結果。
- Input Confirmation 消費明確 entry-submitted／entry-result ID lifecycle，維持依送出順序最近十筆，支援多筆交錯完成與失敗，不再以單一 pending slot 及通用訊息猜測結果。Quick Entry 在送出當下只 focus 摘要，成功才更新常用摘要。
- 月份 GET 等待該月份既有 create 完成並以同一 mutation revision guard 重讀過期快照，避免同筆 server row／暫存 row 重複；不依內容去重，合法相同交易仍保留。更早月份新增成功時，沿既有 owner 刷新目前月份期初。
- Ledger Renderer 單一 owner 呈現待存狀態，正式 ID 回來前不允許編輯／刪除。沒有新 API owner、writer、observer、schema、wrapper 或裝置專用 business path；手機／平板原版面保留。
- 本批驗收追蹤對應 PR／main Actions；自動回歸包含四種 viewport 共用寫入、亂序回應、相同交易、多筆失敗、草稿保留、換月與快照競態、SQLite 實際 POST row 回讀及鎖帳拒絕；不以 production semantic probes 宣稱真機完成全部操作。

## V0.25.2 舊備份相容與首次登入前置

- portable schema 6～10 沿同一 `backup-restore.js normalizeAccountingRestoreData`；只在原始 bytes／digest 驗證後轉換，舊物件不回寫。schema 6 色碼依 migration 0007 的 created_at／id 排序補入；6／7 沒有的 Excel import audit 初始化空陣列，矛盾的非空稽核拒絕，不放寬 schema 8+ 缺欄位檢查。
- 記憶體 SQLite verifier 與獨立 D1 writer 都比對相同 normalized payload，保留交易、期初與全部既有稽核／設定；回報 normalization metadata，preview 明示已轉換。來源 schema 6～9 缺 context 時仍需原 D1，不宣稱已具離線災難復原 context。
- 既有 production resolver 在 protected deploy 唯讀核對 provider ACCOUNT_PORTAL_URL → Cloudflare managed domain → 實際 CY Web Worker binding／Workspace／core consumer health。重用 CYID 的 requireProductionCoreConsumer，不新增 Identity authority、Secret、workflow 或廣權限；只回報 generic 成敗、不輸出實際資源。首次登入 PR #435 要等此 live gate 通過才合併。

## V0.25.1 桌機設定及備份介面

- 一般／統計損益／月份鎖帳／資料管理／備份復原。Settings manager 以明確 registration／open／viewport lifecycle 同步 owned nav 的 DOM／鍵盤順序與名稱；Quick Entry constructor 呼叫同一 hook，touch 回復原名稱及順序。移除 header 的第二 click／timer，openSettings 直接委派既有 settings manager prepare，quick tab 由 canonical setSettingsTab 載入設定。
- renderSettings 對原 lockedThrough input 發 change，同步既有桌機 month picker label；不新增 observer／第二月份狀態。桌機已鎖狀態以 picker 呈現，保留未鎖與實際操作結果。精簡重複說明、保留必要風險及驗證提示。
- Backup UI 同一 module 是唯一 dialog/form／preview/action owner；settings 只保留底部 launcher 與緊急復原入口。表單只建一次，close／Escape 清 preview／typed confirm 並作廢晚到結果；commit 高風險期間禁用欄位與關閉／重複操作，server 原 gate／writer 不變。
- 立即備份與重新整理同列；桌機卡片 hint 移除不對稱外距並垂直置中；GCS 未排程不畫 badge；驗證按鈕改「檢查備份可還原性」。移除被取代的 settings 內還原表單、backup copy cleanup selectors 及死樣式，沒有第二 API、writer、observer 或 wrapper。
- 本地 79 項 syntax／application／contract 檢查及新增回歸通過；PR #453 已合併，main run 38673907858 validate／deploy 完整成功。正式桌機已回讀選單正確順序、鎖帳 picker 顯示實際年月、置中卡片、頂部立即備份、GCS 空欄與單一獨立還原 dialog；Escape 關閉及重新開啟清空已確認，未送出還原或鎖帳。

## V0.25.0 受控復原

- Backup service 仍是唯一 HTTP／orchestration owner；`backup-recovery.js` 是空白目標 writer，沿用 `backup-restore.js` 的 accounting mapping，`backup-recovery-context.js` 集中維運表 allowlist。復原頁只有 presentation，呼叫相同 API、採相同 CYID SUPER_ADMIN guard。
- schema 10 新備份的 manifest 內含 catalog、copies、restore checks、policy events 與舊 backup runs；保留完整帳務及期初／匯入 audit。排除 Session／OAuth／機密及復原工作區自身的新預覽／執行紀錄，不宣稱是所有 D1 表的物理複製。
- 空白工作區原子 claim／種子快照 guard／完整寫入；失敗 rollback。還原後逐欄逐列及 context 比對、D1 quick_check／FK；來源 SQLite 仍 integrity_check。目標帶 recovery_hold，不自動成為正式帳本。
- 部署以既有 protected job 準備獨立 D1、注入 RECOVERY_DB；原 DB 不清空。GCS 復原入口及預覽不依賴原 D1，可處理原帳本中斷；傳統 schema 8／9 沒有 context 時仍需原 D1。
- 資料庫正式 promotion 需要維運依 BACKUP_ARCHITECTURE_HANDOFF 的切換步驟進行。本次僅授權演練，保留原帳本與所有 audit，不執行流量切換。

### 2026/10/15 正式驗收

- PR #450 main run 38671076691、返修 PR #451 main run 38671618879 的 validate／deploy／migration／semantic probes 全成功，正式主站回讀 V0.25.0 Build 1。
- 真實 schema 10 paired backup 已回讀 R2／GCS 雙成功；以該份 GCS 備份經獨立入口預覽、勾選及文字確認，成功重建已 provision 的獨立 D1。正式結果確認完整欄列、稽核、備份政策及資料庫檢查通過，目標 append-only verified audit 已由主站備份頁回讀。
- 原主站仍可登入／讀月帳，演練前後原月份合計一致；沒有切換正式 binding 或修改財務列。還原後目標不可再預覽／覆寫，Phase C 完成進度及啟用控制仍隱藏。
- 原 D1 關閉／僅 GCS、約束失敗 rollback、實際 workerd／全新 D1 及重建後帳戶／交易讀取屬本地驗收。正式環境沒有故意停用 R2／原 D1，也沒有進行 production traffic promotion；不能將本次演練寫成正式切換已驗證。
- 本地 77 項 syntax／application 檢查與 CYID／build metadata 檢查通過，返修相關回歸及 PR／main CI 亦通過。未建立 Release、未修改其他 App；實際帳務、備份識別／digest、resource metadata 不進 Public Git。

## V0.24.1 正式狀態與記帳快捷

- Phase D 之後隱藏已完成的 Phase C 驗收進度與啟用控制，備份按鈕改為正式用詞；原 server gate／歷史紀錄保留，不把 UI 退場當成刪除稽核。
- 日期 Ctrl＋↑↓ 改由既有 Quick Entry form keydown owner 統一處理。桌機日期文字、摘要、金額共用 canonical date setter、只發一次 change、保留原焦點；移除 Adaptive picker 內重複 Ctrl handler。IME／其他修飾鍵／非 entry 欄位不攔截，手機／平板原生日期快捷及原版面保留。
- 單／多月統計、損益表、看帳及 Excel 按金額正負顯示淨利／淨損，零元損益；不改移轉排除或任何財務計算。
- 三項修正已由 PR #449 合併，main run 38613728651 成功；正式 V0.24.1 已確認 Phase C UI 退場、摘要 Ctrl＋↑／金額 Ctrl＋↓ 改日期且保留焦點、零元「損益」。完整正負零規則有行為測試。未儲存測試資料。

## V0.24.0 備份政策與隔離還原

- 正式舊版備份頁已回讀 Phase C **14／14**；不再依日期推測。新增 Phase D R2 每日 30 天、GCS 台灣時間週三／週日 03:30 保留 182 天；手動及 Excel 匯入前仍雙副本，GCS 未排程日不記失敗。
- `backup-service.js` 沿用唯一 orchestration，`backup_policy_events` 是生效 policy authority 與 append-only 操作者稽核。啟用前重新檢查 14 次、同一備份雙 provider 隔離還原且證據未逾 24 小時；切換不清物件，後續 cleanup 採新 retention。保留原 topology 與 credentials／bindings，不另建 workflow 或擴張 IAM。
- 隔離 SQLite 使用實際 canonical migrations 與 exporter，驗證所有帳務欄列、重複交易、封存帳戶、設定、期初及匯入 audit，檢查 integrity／FK。V0.24.0 當時支援來源 schema 8／9；結果 metadata append-only、來源為 server principal。V0.25.0 新增獨立 D1 writer 及 schema 10，現況見上節；V0.25.2 已補 portable schema 6／7 normalization。
- 版本 stamp 改由 VERSION／migrations 產生 `src/build-metadata.json` 並在 CI check，修正舊新備份固定寫 0.22.2；不回寫歷史備份。server WASM 固定 sql.js 1.14.2，真實 workerd 重複請求確認每次新 DB。
- 本地 **32 項檢查**（30 application suites、CYID contract 與 build metadata）通過；CI 另含 syntax、D1 migration、bundle 及正式 semantic probes。正式 R2／GCS 副本已分別完成隔離還原，備份頁顯示完整資料比對與資料庫檢查通過、正式帳本未更動；啟用按鈕由 server gate 開放。使用者於另一瀏覽器完成啟用後，2026/10/15 已重新整理正式備份頁，回讀 **Phase D 已啟用**：R2 每日 30 天；GCS 台灣時間週三／週日 03:30、182 天。啟用結果由 server policy 回讀確認，不以口頭回報或部署狀態推定。首次 R2-only 新排程已於下方記錄；GCS 新政策週期排程仍待觀察。
- 新增操作只在桌機備份頁；手機／平板維持既有版面與只讀備份資訊，狀態文字反映實際保留政策。

## V0.23.4 桌機記帳輸入、編輯及日期

- 收支及問號同列，field width 讓出問號空間；唯一 `#saveMessage` 改在桌機確認標題列右側，原十筆說明只在桌機隱藏。十筆 retention 與排列不變。
- 原存檔先 `amount.focus()`、ledger-rendered 的 timer 再 `summary.focus()`，形成兩個 post-save focus owner；移除 timer、pending focus flag 及 ledger result 猜測，改由 canonical successful-save lifecycle 呼叫 Quick Entry `cyAfterEntrySaved`，直接回摘要並刷新常用摘要。不能以 Linux／Chromium 驗證冒稱 Windows IME 模式通過；待使用者確認。
- 既有 Adaptive entry-edit owner 改 semantic `setupEntryTransactionEditing`，涵蓋桌機並保持原手機／平板導航與提示。桌機帶回左側共用表單、維持原筆 kind，清空變取消；取消／Escape／換筆／切月份／開其他工作視窗／pagehide／裝置切換均不自動寫入並回復原新增草稿。儲存修改一律 `persistTransactionUpdate` PUT／optimistic／rollback；App 的既有 row action 直接呼叫 entry editor API；刪除被取代的 inline editor module、modal edit state／dialog、CSS、listener 及 asset probe。
- Desktop date owner 直接建立可編輯文字＋日曆按鈕，兩者寫回原 `#txDate`；四碼月日採今年、八碼完整日期、YYYY/MM/DD、Ctrl＋↑↓ 跨月／年、Enter 到摘要、無效日期不得留下舊值。共用 Quick Entry parser／setter；移除 owned DOM observer／synthetic keyboard retry bridge。RWD picker teardown abort 舊 input／document listeners，touch native 日期保留。
- 桌機單月統計只移除說明括號設定路徑，touch／report 說明及移轉排除計算不改。新增桌機右下角圓形回到頂端按鈕，向下捲動後出現；只捲動整頁，遵循 reduced-motion。
- 本地及 PR CI 新增 `entry-workflow.mjs`，驗證取消不寫入、原草稿及歷史 options 復原、原筆 PUT／失敗 rollback、成功只 focus 摘要；日期回歸涵蓋手輸四／八碼、閏日、非法日期、Ctrl 跨年與日曆同步。共 28 組 application tests；正式部署及瀏覽器 gate 追蹤本次 PR／main Actions，不以舊版驗收代替新版。

## V0.23.3 提示、確認清單與排序

- 存檔提示仍由 `app.js showMessage` 寫入唯一 `#saveMessage`；既有 Adaptive placement owner 於 confirmation-ready／裝置切換，把同一 node 放到桌機確認標題列中央、手機／平板原位置，不複製第二個訊息或新增 observer。
- Input Confirmation 改消費 `cyacc:entry-message` 明確 lifecycle，移除存檔訊息與自身 list 的兩個 MutationObserver；render 自行更新筆數及 drawer。每次 submit 收集一筆結果，最新在前，第十一筆淘汰最舊；資料仍只存在本次頁面記憶體。
- 桌機改 23px 單行清單、11.5px 文字、成功／失敗標記，長內容可查看完整文字；取代 17 個舊 confirmation 字級／卡片樣式規則，集中為一個 Desktop presentation owner，不增加 override chain。
- 日期表頭為 native button，切換 ascending／descending；日期比較只影響 displayed rows，相同日期及 balance calculation 沿用 chronological owner。分組同時遵守日期方向；桌機帳戶標籤改「帳戶 [分組]」，觸控保留原標籤。
- 裝置呈現切換由既有 Adaptive lifecycle 呼叫 Ledger render owner，避免桌機倒序／表頭控件帶到手機和平板；transaction snapshot、每筆餘額、月合計、搜尋及 API 不改。
- 行為回歸涵蓋最新十筆淘汰、成功／失敗、逃脫文字、唯一訊息節點 placement，及日期順序／同日收支／分組／搜尋下的正確餘額與裝置切換。正式驗收見 PR #442：唯一提示置中、最新十筆淘汰、日期倒序與分組／每筆餘額一致已回讀。Build 1 移除舊 grid gap 及符號 baseline 留白；密度驗收追蹤收尾 PR 與 main Actions。

## V0.23.2 日期修復

- 原 picker markup 的 `data-desktopUi-date-*` 經 HTML parser 全部轉小寫，dataset key 與 handler 不符；選日把 native `#txDate` 清空，label fallback 今天，造成必填驗證阻擋儲存，日／月／年導覽也讀不到數值。正式舊版已在瀏覽器重現此資料／畫面落差。
- 在原 Adaptive date presentation owner 統一改為 lowercase kebab-case `data-desktop-ui-date-*`，不新增 renderer、writer、observer 或 fallback。既有 `change` lifecycle、lock 判斷及 submit 讀同一 input。
- 行為回歸涵蓋選日後 canonical value／label、單次 change、儲存 payload、前後月與跨年、指定年月、閏日、今天、外部變更及重複初始化。舊 source 會使該測試失敗；修復後通過。
- Adaptive script cache revision 改為 `desktop-date-dataset`，正式 semantic verification 使用同一 URL 並確認新的日期 attribute。本版部署與正式瀏覽器驗收證據追蹤 [PR #441](https://github.com/simonliu1118-byte/CYapps/pull/441) 及對應 main Actions，不把舊版重現當成新版通過。

## 已交付內容與實際驗收界線

- 統計以完整期間 snapshot 彙總，不受清單筆數限制；保留封存帳戶及歷史未匹配科目。移轉科目按 `categories.id` 保存於既有 settings，隨備份保存，不依名稱猜測。
- 單月與多月共用「圖表／統計」頁籤。桌機統計共用整頁垂直捲動，寬視窗三欄、窄視窗接在看帳下方；手機第三頁籤、平板雙方向同一入口。
- 圓餅直接標名稱／金額／百分比；小區段引線排列。手機／平板不在餅上放金額，完整圖例仍保留；不使用 tooltip。
- 統計合計、圖表、分類／科目與損益皆排除指定移轉，單月區明示；看帳現金流包含移轉。期初不計入損益。
- 損益表左右並排收入／支出，合計與淨利損在底部，移轉最後另列。依可讀高度必要時分頁，所有科目及整月合計保留；沒有報表內部捲軸。
- 移除 Adaptive identity 重畫及 role observer；`public/auth.js` 為唯一帳號 renderer。等待資料時的隱藏規則已置於真正載入的 `adaptive-ui.css`，保留既有角色填色及對齊。
- 26 組本地 application tests、PR CI／governance 及正式 deploy 通過。正式桌機瀏覽器 **1363×936** 已驗證：帳號由 pending hidden 到 ready visible、圖表／統計互斥、排除移轉說明、單月只顯示一個月份，抽查月份完整一頁且無水平／垂直捲軸。
- 上述抽查不等於所有月份／視窗尺寸均一頁，也不等於手機／平板真機驗收。完整剩餘 gate 見 TODO。

Excel V0.22.32 沿用 inspect／preview／commit owner；指定期間替換先完成 R2＋GCS paired backup 及回讀，再以 snapshot guard 執行原子 D1 batch，保留重複發生交易、期間外資料與 append-only audit。功能部署證據 [run 38062352888](https://github.com/simonliu1118-byte/CYapps/actions/runs/38062352888)不代表可以推定實際匯入狀態。

## 已完成交接與下一步

| 工作 | 現況及接續條件 |
| --- | --- |
| PR **#435** 首次登入引導 | 已以 V0.25.4 合併部署，main run 38736782701 完整成功；使用者確認收信／啟用已驗收，後續由 CYWEB 維護 |
| optimistic create | 已由 PR #457／V0.25.3 完成並部署，舊實驗線不是接續基準 |
| 真機統計／記帳驗收 | 手機、平板雙方向、鍵盤／picker／分享及大科目報表仍有待驗證項目 |
| Backup／Restore／DR | Phase D 生效、首次 R2-only 自動排程與手動雙副本成功；正式 GCS → 獨立 D1 完整還原／對帳完成，原正式帳本未切換。schema 6／7 normalization 已補；budget 已由使用者建立，GCS 新政策週期排程與 lifecycle 仍待辦 |
| 實際帳務操作 | 先讀私人檢查點與當次指示，再唯讀核對；公開文件不宣告尚未或已完成某次匯入，不據舊 TODO 重跑 |

## GitHub 清理 — 2026/10/13

- PR **#436** 已合併；[清理 run 38383025033](https://github.com/simonliu1118-byte/CYapps/actions/runs/38383025033) completed／success，作業回讀確認 **34 個過期 branch、16 個已退場流程的 22 筆 completed runs** 已清理。候選 branch／run／到期 Artifact 殘留均為 **0**；沒有需要處理的到期 Artifact，共 56 次刪除。
- 舊文件 PR **#268、#291、#383** 已關閉，現行文件已涵蓋有效內容；PR 歷史保留。已完成分支清單與盤點 SHA 可由 #436 commit 追溯。
- 當時 **#435 首次登入引導**與 **optimistic create** 未合併工作線保留（目前均已完成，見上方現況）；執行中 run、現行部署／Release 全部歷史及 Stable Release／tag／assets、其他 App／共通流程保留。
- 一次性 maintenance workflow、script 及 manifest 已從本次收尾 source 移除；主線僅保留正式 `cyaccountingweb-deploy.yml` 與 `cyaccountingweb-release.yml`。本次清理的驗證／執行紀錄保留作維護證據，不留下長期清理權限入口。
- 文件、Backup schema、平板現況與 TODO 已彙整；產品維持 **V0.23.1 Build 2／schema 8**。部署驗證不代表更動帳務資料，也不合併未完成的 #435。

## Canonical owner

| Concern | 正式 owner |
| --- | --- |
| Identity transport／Session | `src/identity-adapter.js`；前端帳號 render `public/auth.js` |
| Worker route／帳務權限／API orchestration | `src/app.js` |
| 期初 carry-forward／人工調整 | `src/opening-balances.js` |
| 帳戶 lifecycle | `src/account-lifecycle.js` |
| Backup package／topology／provider | `src/backup-package.js` → `src/backup-service.js` → provider adapters；隔離 SQLite domain `src/backup-restore.js`、WASM host `src/backup-restore-runtime.js` |
| 統計 snapshot／report settings | `src/statistics.js` |
| SQLite normalization／migration | `src/desktop-migration-core.js`／`src/desktop-migration.js` |
| Transaction state／month load／mutation／rollback | `public/app.js` |
| Ledger rows／row lifecycle／Toolbar | `public/ledger-tools.js` |
| Entry edit presentation／draft／cancel | `public/adaptive-ui.js setupEntryTransactionEditing`，寫入委派既有 mutation |
| Quick-entry presentation | `public/quick-entry.js` |
| Settings lifecycle／render／action | `public/adaptive-ui.js` 的 `window.cySettingsManager` |
| RWD／native picker／gesture | `public/adaptive-ui.js` |
| Statistics render／report | `public/statistics-ui.js`；裝置 placement 仍由 Adaptive UI owner |
| Input confirmation drawer | `public/input-confirmation.js` |

## 接續資料操作

真實交易、金額、來源帳本、備份識別、token、正式 resource identifiers 及執行紀錄由私人檢查點保存，不放 Public Git／PR／CI Artifact。本交接只記錄程式能力與驗證，不能作為是否重跑匯入、調整期初、恢復鎖帳或重做備份的依據。

CYID 共通參數及舊 Variable 退場已於 2026/10/12 完成驗證；詳細共通 rollout 見 CYID canonical 文件及相關 App 現行交接。本 App production scope 不借給其他 App；中央 development 更新不自動改變本 App production authority。

2026/10/15 正式備份頁已回讀 Phase D 首次 R2-only 自動排程成功，GCS 正確顯示本日未排程；GCS 在 Phase D 後的首次週三／週日排程仍待實際觀察。V0.25.0 真實手動 paired backup 已回讀 R2／GCS 雙成功，新備份含 schema 10 recoveryContext。實際識別、筆數與 digest 不放 Public Git。

V0.25.0 正式 native 復原表單因 no-referrer 產生空 Origin 而被既有 guard 拒絕，未進入預覽／寫入；Build 1 將頁面 referrer-policy 改為 same-origin，保留同來源 guard 與 CSP，補自動測試。Build 1 main run 38671618879 完整成功後，正式 native 表單 preview／雙重確認／commit 已實際通過。

V0.25.2 初次 main run 38674853444 validate 成功，deploy 在唯讀 portal gate 回報 PRODUCTION_CORE_CONSUMER_NOT_READY，尚未 migration／Worker deploy，線上保持 V0.25.1。Build 1 將尚未啟用的 navigation metadata 僅作預檢；真正 first-login feature deploy 必須帶 --require-account-portal，失敗維持 fail closed。provider 原 runtime guard 永遠強制。新增診斷只列 AUTHORITY／ENVIRONMENT／APPLICATION／VERSION／HEALTH 差異標籤，不輸出實際值。

## 本批結案與外部待辦

- V0.25.2 Build 1 已正式部署；schema 6／7 normalization 的本地 SQLite、workerd、D1 roundtrip 及其餘 79 項 workflow checks 通過。沒有為驗收而覆寫正式帳本或已用過的復原目標。
- 首次登入阻擋已由 CY Web 修正，證據見 V0.25.4 小節；本 App 仍沿同一 canonical guard 強制驗證，不採 development Identity 或放寬 guard。
- Google Cloud 每月低額預算已由使用者在 Console 建立，清單截圖確認帳單帳戶範圍、指定金額及實際支出 50／90／100% 門檻；依使用者決定包含優惠，追蹤折抵後支出。截圖未標明幣別／Email 收件者，不推定為 TWD 或已驗收寄信。不是自動硬上限。
- GCS 新政策首次週期排程及 bucket lifecycle 仍待確認；不新增無必要 workflow，不擴張 IAM，不建立 Release。
